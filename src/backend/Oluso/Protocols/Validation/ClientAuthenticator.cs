using System.IdentityModel.Tokens.Jwt;
using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;
using System.Text;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Caching.Distributed;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Microsoft.IdentityModel.Tokens;
using Oluso.Core.Domain.Entities;
using Oluso.Core.Domain.Interfaces;
using Oluso.Core.Protocols;
using Oluso.Core.Protocols.Models;
using Oluso.Core.Protocols.Validation;
using Oluso.Core.Services;
using Oluso.Protocols.Mtls;

namespace Oluso.Protocols.Validation;

/// <summary>
/// Default implementation of client authenticator
/// </summary>
public class ClientAuthenticator : IClientAuthenticator
{
    private readonly IClientStore _clientStore;
    private readonly IIssuerResolver _issuerResolver;
    private readonly OidcEndpointConfiguration _endpointConfig;
    private readonly IDistributedCache _cache;
    private readonly IClientCertificateExtractor _certExtractor;
    private readonly MtlsOptions _mtlsOptions;
    private readonly ILogger<ClientAuthenticator> _logger;

    // Cache for JTI replay protection (5 minute window)
    private const string JtiCachePrefix = "oluso:client:jwt:jti:";
    private static readonly TimeSpan JtiCacheDuration = TimeSpan.FromMinutes(5);

    public ClientAuthenticator(
        IClientStore clientStore,
        IIssuerResolver issuerResolver,
        IOptions<OidcEndpointConfiguration> endpointConfig,
        IDistributedCache cache,
        IClientCertificateExtractor certExtractor,
        MtlsOptions mtlsOptions,
        ILogger<ClientAuthenticator> logger)
    {
        _clientStore = clientStore;
        _issuerResolver = issuerResolver;
        _endpointConfig = endpointConfig.Value;
        _cache = cache;
        _certExtractor = certExtractor;
        _mtlsOptions = mtlsOptions;
        _logger = logger;
    }

    /// <inheritdoc />
    public async Task<ClientAuthenticationResult> AuthenticateAsync(
        string clientId,
        string? clientSecret,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrEmpty(clientId))
        {
            return new ClientAuthenticationResult
            {
                Error = OidcConstants.Errors.InvalidClient,
                ErrorDescription = "client_id is required"
            };
        }

        // If secret provided, validate it
        if (!string.IsNullOrEmpty(clientSecret))
        {
            return await ValidateClientSecretAsync(
                clientId,
                clientSecret,
                ClientAuthenticationMethod.ClientSecretPost,
                cancellationToken);
        }

        // No secret - check if public client
        var client = await _clientStore.FindClientByIdAsync(clientId, cancellationToken);
        if (client == null)
        {
            return new ClientAuthenticationResult
            {
                Error = OidcConstants.Errors.InvalidClient,
                ErrorDescription = "Unknown client"
            };
        }

        if (client.RequireClientSecret)
        {
            return new ClientAuthenticationResult
            {
                Error = OidcConstants.Errors.InvalidClient,
                ErrorDescription = "Client credentials required"
            };
        }

        return new ClientAuthenticationResult
        {
            Client = client,
            Method = ClientAuthenticationMethod.None
        };
    }

    /// <inheritdoc />
    public async Task<ClientAuthenticationResult> AuthenticateAsync(
        HttpRequest request,
        CancellationToken cancellationToken = default)
    {
        // Try multiple authentication methods in order of preference

        // 1. Try client_secret_basic (Authorization header)
        var basicResult = await TryBasicAuthenticationAsync(request, cancellationToken);
        if (basicResult != null)
            return basicResult;

        // 2. Try client_secret_post (form body)
        var postResult = await TryPostAuthenticationAsync(request, cancellationToken);
        if (postResult != null)
            return postResult;

        // 3. Try private_key_jwt / client_secret_jwt (client_assertion)
        var jwtResult = await TryJwtAuthenticationAsync(request, cancellationToken);
        if (jwtResult != null)
            return jwtResult;

        // 4. Try tls_client_auth / self_signed_tls_client_auth (mTLS)
        var mtlsResult = await TryMtlsClientAuthAsync(request, cancellationToken);
        if (mtlsResult != null)
            return mtlsResult;

        // 5. Try public client (client_id only, no secret)
        var publicResult = await TryPublicClientAsync(request, cancellationToken);
        if (publicResult != null)
            return publicResult;

        return new ClientAuthenticationResult
        {
            Error = OidcConstants.Errors.InvalidClient,
            ErrorDescription = "Client authentication failed"
        };
    }

    private async Task<ClientAuthenticationResult?> TryBasicAuthenticationAsync(
        HttpRequest request,
        CancellationToken cancellationToken)
    {
        var authHeader = request.Headers.Authorization.FirstOrDefault();
        if (string.IsNullOrEmpty(authHeader) || !authHeader.StartsWith("Basic ", StringComparison.OrdinalIgnoreCase))
            return null;

        try
        {
            var encodedCredentials = authHeader.Substring("Basic ".Length).Trim();
            var credentialBytes = Convert.FromBase64String(encodedCredentials);
            var credentials = Encoding.UTF8.GetString(credentialBytes);
            var separatorIndex = credentials.IndexOf(':');

            if (separatorIndex < 0)
                return null;

            var clientId = Uri.UnescapeDataString(credentials.Substring(0, separatorIndex));
            var clientSecret = Uri.UnescapeDataString(credentials.Substring(separatorIndex + 1));

            // RFC 6749 §2.3.1: an empty password means the client presented no secret.
            // IdentityModel's OidcClient uses this form (Basic base64("client_id:")) for
            // public clients under its default AuthorizationHeader credential style. Treat
            // it as a public-client attempt rather than a failed client_secret_basic.
            if (string.IsNullOrEmpty(clientSecret))
                return await ValidatePublicClientAsync(clientId, cancellationToken);

            return await ValidateClientSecretAsync(clientId, clientSecret, ClientAuthenticationMethod.ClientSecretBasic, cancellationToken);
        }
        catch
        {
            return null;
        }
    }

    private async Task<ClientAuthenticationResult?> TryPostAuthenticationAsync(
        HttpRequest request,
        CancellationToken cancellationToken)
    {
        if (!request.HasFormContentType)
            return null;

        var form = await request.ReadFormAsync(cancellationToken);

        var clientId = form["client_id"].FirstOrDefault();
        var clientSecret = form["client_secret"].FirstOrDefault();

        if (string.IsNullOrEmpty(clientId))
            return null;

        if (string.IsNullOrEmpty(clientSecret))
            return null; // If client_id is present but no secret, might be public client

        return await ValidateClientSecretAsync(clientId, clientSecret, ClientAuthenticationMethod.ClientSecretPost, cancellationToken);
    }

    private async Task<ClientAuthenticationResult?> TryJwtAuthenticationAsync(
        HttpRequest request,
        CancellationToken cancellationToken)
    {
        if (!request.HasFormContentType)
            return null;

        var form = await request.ReadFormAsync(cancellationToken);

        var clientAssertion = form["client_assertion"].FirstOrDefault();
        var clientAssertionType = form["client_assertion_type"].FirstOrDefault();

        if (string.IsNullOrEmpty(clientAssertion) || string.IsNullOrEmpty(clientAssertionType))
            return null;

        if (clientAssertionType != ClientAssertionTypes.JwtBearer)
        {
            return new ClientAuthenticationResult
            {
                Error = OidcConstants.Errors.InvalidClient,
                ErrorDescription = "Unsupported client_assertion_type"
            };
        }

        // 1. Decode JWT without validation to extract claims
        var handler = new JwtSecurityTokenHandler();
        JwtSecurityToken jwt;
        try
        {
            jwt = handler.ReadJwtToken(clientAssertion);
        }
        catch (Exception)
        {
            return new ClientAuthenticationResult
            {
                Error = OidcConstants.Errors.InvalidClient,
                ErrorDescription = "Invalid JWT format"
            };
        }

        // 2. Get client_id from 'sub' or 'iss' claim (per RFC 7523, both should be client_id)
        var clientId = jwt.Subject ?? jwt.Issuer;
        if (string.IsNullOrEmpty(clientId))
        {
            return new ClientAuthenticationResult
            {
                Error = OidcConstants.Errors.InvalidClient,
                ErrorDescription = "JWT must contain 'sub' or 'iss' claim with client_id"
            };
        }

        // Verify iss and sub match (per RFC 7523 Section 3)
        if (!string.IsNullOrEmpty(jwt.Subject) && !string.IsNullOrEmpty(jwt.Issuer) && jwt.Subject != jwt.Issuer)
        {
            return new ClientAuthenticationResult
            {
                Error = OidcConstants.Errors.InvalidClient,
                ErrorDescription = "JWT 'sub' and 'iss' claims must match"
            };
        }

        // 3. Look up the client
        var client = await _clientStore.FindClientByIdAsync(clientId, cancellationToken);
        if (client == null)
        {
            return new ClientAuthenticationResult
            {
                Error = OidcConstants.Errors.InvalidClient,
                ErrorDescription = "Unknown client"
            };
        }

        if (!client.Enabled)
        {
            return new ClientAuthenticationResult
            {
                Error = OidcConstants.Errors.InvalidClient,
                ErrorDescription = "Client is disabled"
            };
        }

        // 4. Validate 'aud' claim - must contain token endpoint URL or issuer
        var issuer = await _issuerResolver.GetIssuerAsync(cancellationToken);
        var tokenEndpoint = $"{issuer}{_endpointConfig.TokenEndpoint}";
        var validAudiences = new[] { issuer, tokenEndpoint };

        var audClaims = jwt.Audiences?.ToList() ?? new List<string>();
        if (!audClaims.Any(a => validAudiences.Contains(a, StringComparer.OrdinalIgnoreCase)))
        {
            return new ClientAuthenticationResult
            {
                Error = OidcConstants.Errors.InvalidClient,
                ErrorDescription = $"JWT 'aud' must contain '{issuer}' or '{tokenEndpoint}'"
            };
        }

        // 5. Validate expiration
        if (jwt.ValidTo < DateTime.UtcNow)
        {
            return new ClientAuthenticationResult
            {
                Error = OidcConstants.Errors.InvalidClient,
                ErrorDescription = "JWT has expired"
            };
        }

        // 6. JTI replay protection is performed after signature validation (step 8b)
        //    per RFC 7523: only cache JTIs from cryptographically verified JWTs
        //    to prevent replay-cache poisoning attacks with forged tokens.
        var jti = jwt.Id;

        // 7. Get signing key and validate signature
        var algorithm = jwt.Header.Alg;

        SecurityKey? signingKey = null;
        var method = ClientAuthenticationMethod.PrivateKeyJwt; // Default, will be overwritten

        // Try to find a matching key from client secrets
        foreach (var secret in client.ClientSecrets)
        {
            // Check expiration
            if (secret.Expiration.HasValue && secret.Expiration.Value < DateTime.UtcNow)
                continue;

            if (secret.Type == "JsonWebKey")
            {
                // private_key_jwt: Client registered a public key (JWK format)
                try
                {
                    var jwk = new JsonWebKey(secret.Value);

                    // Ensure the JWK has the algorithm set for CryptoProviderFactory resolution.
                    // Per RFC 7517 Section 4.4, "alg" is optional on the JWK — when absent,
                    // infer it from the JWT header's "alg" claim so signature validation works.
                    if (string.IsNullOrEmpty(jwk.Alg) && !string.IsNullOrEmpty(algorithm))
                    {
                        jwk.Alg = algorithm;
                    }

                    var kid = jwt.Header.Kid;
                    if (string.IsNullOrEmpty(kid) || jwk.Kid == kid)
                    {
                        signingKey = jwk;
                        method = ClientAuthenticationMethod.PrivateKeyJwt;
                        break;
                    }
                }
                catch
                {
                    continue;
                }
            }
            else if (secret.Type == "X509CertificateBase64")
            {
                // private_key_jwt: Client registered a certificate
                try
                {
                    var certBytes = Convert.FromBase64String(secret.Value);
                    var cert = System.Security.Cryptography.X509Certificates.X509CertificateLoader.LoadCertificate(certBytes);
                    var certKey = new X509SecurityKey(cert);
                    var kid = jwt.Header.Kid;
                    if (string.IsNullOrEmpty(kid) || certKey.KeyId == kid)
                    {
                        signingKey = certKey;
                        method = ClientAuthenticationMethod.PrivateKeyJwt;
                        break;
                    }
                }
                catch
                {
                    continue;
                }
            }
            else if (secret.Type == "SharedSecret" && IsSymmetricAlgorithm(algorithm))
            {
                // client_secret_jwt: Use shared secret as HMAC key
                var keyBytes = Encoding.UTF8.GetBytes(secret.Value);
                if (keyBytes.Length < 32)
                {
                    // Pad to minimum 256 bits for HS256
                    keyBytes = Encoding.UTF8.GetBytes(secret.Value.PadRight(32, '\0'));
                }
                signingKey = new SymmetricSecurityKey(keyBytes);
                method = ClientAuthenticationMethod.ClientSecretJwt;
                break;
            }
        }

        if (signingKey == null)
        {
            return new ClientAuthenticationResult
            {
                Error = OidcConstants.Errors.InvalidClient,
                ErrorDescription = "No suitable key found for JWT validation"
            };
        }

        // 8. Validate the JWT signature
        var validationParameters = new TokenValidationParameters
        {
            ValidateIssuer = true,
            ValidIssuer = clientId,
            ValidateAudience = true,
            ValidAudiences = validAudiences,
            ValidateLifetime = true,
            ValidateIssuerSigningKey = true,
            IssuerSigningKey = signingKey,
            ClockSkew = TimeSpan.FromMinutes(1)
        };

        try
        {
            handler.ValidateToken(clientAssertion, validationParameters, out _);
        }
        catch (SecurityTokenException ex)
        {
            return new ClientAuthenticationResult
            {
                Error = OidcConstants.Errors.InvalidClient,
                ErrorDescription = $"JWT validation failed: {ex.Message}"
            };
        }

        // 8b. JTI replay protection — only after signature is cryptographically verified.
        //     Caching JTIs from unvalidated JWTs would allow replay-cache poisoning.
        if (!string.IsNullOrEmpty(jti))
        {
            var jtiCacheKey = $"{JtiCachePrefix}{clientId}:{jti}";
            var existing = await _cache.GetAsync(jtiCacheKey, cancellationToken);
            if (existing != null)
            {
                return new ClientAuthenticationResult
                {
                    Error = OidcConstants.Errors.InvalidClient,
                    ErrorDescription = "JWT has already been used (replay detected)"
                };
            }
            var cacheOptions = new DistributedCacheEntryOptions
            {
                AbsoluteExpirationRelativeToNow = JtiCacheDuration
            };
            await _cache.SetAsync(jtiCacheKey, new byte[] { 1 }, cacheOptions, cancellationToken);
        }

        return new ClientAuthenticationResult
        {
            Client = client,
            Method = method
        };
    }

    private static bool IsSymmetricAlgorithm(string algorithm)
    {
        return algorithm.StartsWith("HS", StringComparison.OrdinalIgnoreCase);
    }

    private async Task<ClientAuthenticationResult?> TryMtlsClientAuthAsync(
        HttpRequest request,
        CancellationToken cancellationToken)
    {
        if (!_mtlsOptions.Enabled)
            return null;

        var cert = _certExtractor.Extract(request.HttpContext);
        if (cert == null)
            return null;

        if (!request.HasFormContentType)
            return null;

        var form = await request.ReadFormAsync(cancellationToken);
        var clientId = form["client_id"].FirstOrDefault();
        if (string.IsNullOrEmpty(clientId))
            return null;

        var client = await _clientStore.FindClientByIdAsync(clientId, cancellationToken);
        if (client == null)
            return null;

        if (client.TokenEndpointAuthMethod is not ("tls_client_auth" or "self_signed_tls_client_auth"))
            return null;

        if (!client.Enabled)
        {
            return new ClientAuthenticationResult
            {
                Error = OidcConstants.Errors.InvalidClient,
                ErrorDescription = "Client is disabled"
            };
        }

        return client.TokenEndpointAuthMethod == "tls_client_auth"
            ? ValidatePkiCert(client, cert)
            : ValidateSelfSignedCert(client, cert);
    }

    private ClientAuthenticationResult ValidatePkiCert(Client client, X509Certificate2 cert)
    {
        bool matched;

        if (!string.IsNullOrEmpty(client.TlsClientAuthSubjectDn))
        {
            var presentedDn = new X500DistinguishedName(cert.SubjectName.RawData).Format(true);
            var expectedDn = new X500DistinguishedName(client.TlsClientAuthSubjectDn).Format(true);
            matched = string.Equals(presentedDn, expectedDn, StringComparison.OrdinalIgnoreCase);
        }
        else if (!string.IsNullOrEmpty(client.TlsClientAuthSanDns))
        {
            matched = CertHasSan(cert, client.TlsClientAuthSanDns, SanType.DnsName);
        }
        else if (!string.IsNullOrEmpty(client.TlsClientAuthSanUri))
        {
            matched = CertHasSan(cert, client.TlsClientAuthSanUri, SanType.UniformResourceIdentifier);
        }
        else if (!string.IsNullOrEmpty(client.TlsClientAuthSanEmail))
        {
            matched = CertHasSan(cert, client.TlsClientAuthSanEmail, SanType.Rfc822Name);
        }
        else if (!string.IsNullOrEmpty(client.TlsClientAuthSanIp))
        {
            matched = CertHasSan(cert, client.TlsClientAuthSanIp, SanType.IpAddress);
        }
        else
        {
            return new ClientAuthenticationResult
            {
                Error = OidcConstants.Errors.InvalidClient,
                ErrorDescription = "Client is configured for tls_client_auth but has no certificate identity registered"
            };
        }

        if (!matched)
        {
            _logger.LogWarning("mTLS certificate identity mismatch for client {ClientId}", client.ClientId);
            return new ClientAuthenticationResult
            {
                Error = OidcConstants.Errors.InvalidClient,
                ErrorDescription = "Client certificate does not match registered identity"
            };
        }

        return new ClientAuthenticationResult
        {
            Client = client,
            Method = ClientAuthenticationMethod.TlsClientAuth
        };
    }

    private ClientAuthenticationResult ValidateSelfSignedCert(Client client, X509Certificate2 cert)
    {
        var presentedThumbprint = ComputeSha256CertThumbprint(cert);

        foreach (var secret in client.ClientSecrets)
        {
            if (secret.Type != "X509CertificateBase64")
                continue;
            if (secret.Expiration.HasValue && secret.Expiration.Value < DateTime.UtcNow)
                continue;

            try
            {
                var storedCertBytes = Convert.FromBase64String(secret.Value);
                var storedCert = new X509Certificate2(storedCertBytes);
                if (ComputeSha256CertThumbprint(storedCert) == presentedThumbprint)
                {
                    return new ClientAuthenticationResult
                    {
                        Client = client,
                        Method = ClientAuthenticationMethod.SelfSignedTlsClientAuth
                    };
                }
            }
            catch
            {
                // skip malformed stored certs
            }
        }

        _logger.LogWarning("mTLS self-signed certificate thumbprint mismatch for client {ClientId}", client.ClientId);
        return new ClientAuthenticationResult
        {
            Error = OidcConstants.Errors.InvalidClient,
            ErrorDescription = "Client certificate does not match any registered certificate"
        };
    }

    internal static string ComputeSha256CertThumbprint(X509Certificate2 cert)
    {
        var hash = SHA256.HashData(cert.RawData);
        return Convert.ToBase64String(hash)
            .Replace('+', '-')
            .Replace('/', '_')
            .TrimEnd('=');
    }

    private enum SanType { DnsName, Rfc822Name, UniformResourceIdentifier, IpAddress }

    private static bool CertHasSan(X509Certificate2 cert, string expected, SanType type)
    {
        var sanExtension = cert.Extensions["2.5.29.17"];
        if (sanExtension == null) return false;

        var san = new X509SubjectAlternativeNameExtension(sanExtension.RawData, sanExtension.Critical);

        return type switch
        {
            SanType.DnsName => san.EnumerateDnsNames().Any(d =>
                string.Equals(d, expected, StringComparison.OrdinalIgnoreCase)),
            SanType.IpAddress => san.EnumerateIPAddresses().Any(ip =>
                string.Equals(ip.ToString(), expected, StringComparison.OrdinalIgnoreCase)),
            _ => EnumerateSanStrings(san, type).Any(v =>
                string.Equals(v, expected, StringComparison.OrdinalIgnoreCase))
        };
    }

    private static IEnumerable<string> EnumerateSanStrings(X509SubjectAlternativeNameExtension san, SanType type)
    {
        var formatted = san.Format(true);
        var prefix = type switch
        {
            SanType.Rfc822Name => "RFC822 Name=",
            SanType.UniformResourceIdentifier => "URL=",
            _ => null
        };
        if (prefix == null) yield break;

        foreach (var line in formatted.Split('\n', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries))
        {
            if (line.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
                yield return line[prefix.Length..];
        }
    }

    private async Task<ClientAuthenticationResult?> TryPublicClientAsync(
        HttpRequest request,
        CancellationToken cancellationToken)
    {
        string? clientId = null;

        // Check form
        if (request.HasFormContentType)
        {
            var form = await request.ReadFormAsync(cancellationToken);
            clientId = form["client_id"].FirstOrDefault();
        }

        // Check query (for authorize endpoint)
        if (string.IsNullOrEmpty(clientId))
        {
            clientId = request.Query["client_id"].FirstOrDefault();
        }

        if (string.IsNullOrEmpty(clientId))
            return null;

        return await ValidatePublicClientAsync(clientId, cancellationToken);
    }

    private async Task<ClientAuthenticationResult?> ValidatePublicClientAsync(
        string clientId,
        CancellationToken cancellationToken)
    {
        var client = await _clientStore.FindClientByIdAsync(clientId, cancellationToken);
        if (client == null)
        {
            return new ClientAuthenticationResult
            {
                Error = OidcConstants.Errors.InvalidClient,
                ErrorDescription = "Unknown client"
            };
        }

        // Public clients should not require a secret
        if (client.RequireClientSecret)
        {
            return new ClientAuthenticationResult
            {
                Error = OidcConstants.Errors.InvalidClient,
                ErrorDescription = "Client credentials required"
            };
        }

        return new ClientAuthenticationResult
        {
            Client = client,
            Method = ClientAuthenticationMethod.None
        };
    }

    private async Task<ClientAuthenticationResult> ValidateClientSecretAsync(
        string clientId,
        string clientSecret,
        ClientAuthenticationMethod method,
        CancellationToken cancellationToken)
    {
        var client = await _clientStore.FindClientByIdAsync(clientId, cancellationToken);

        if (client == null)
        {
            return new ClientAuthenticationResult
            {
                Error = OidcConstants.Errors.InvalidClient,
                ErrorDescription = "Unknown client"
            };
        }

        if (!client.Enabled)
        {
            return new ClientAuthenticationResult
            {
                Error = OidcConstants.Errors.InvalidClient,
                ErrorDescription = "Client is disabled"
            };
        }

        // Validate secret
        var secretValid = false;
        foreach (var secret in client.ClientSecrets)
        {
            // Check expiration
            if (secret.Expiration.HasValue && secret.Expiration.Value < DateTime.UtcNow)
                continue;

            // Validate based on secret type
            if (secret.Type == "SharedSecret")
            {
                // Hash and compare
                var hashedInput = HashSecret(clientSecret);
                if (SecureCompare(hashedInput, secret.Value))
                {
                    secretValid = true;
                    break;
                }

                // Also try direct comparison for non-hashed secrets (development)
                if (SecureCompare(clientSecret, secret.Value))
                {
                    secretValid = true;
                    break;
                }
            }
        }

        if (!secretValid)
        {
            return new ClientAuthenticationResult
            {
                Error = OidcConstants.Errors.InvalidClient,
                ErrorDescription = "Invalid client credentials"
            };
        }

        return new ClientAuthenticationResult
        {
            Client = client,
            Method = method
        };
    }

    private static string HashSecret(string secret)
    {
        using var sha256 = SHA256.Create();
        var bytes = Encoding.UTF8.GetBytes(secret);
        var hash = sha256.ComputeHash(bytes);
        return Convert.ToBase64String(hash);
    }

    private static bool SecureCompare(string a, string b)
    {
        if (a.Length != b.Length)
            return false;

        var result = 0;
        for (var i = 0; i < a.Length; i++)
        {
            result |= a[i] ^ b[i];
        }
        return result == 0;
    }
}
