using System.IdentityModel.Tokens.Jwt;
using System.Security.Cryptography;
using System.Text;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Caching.Distributed;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Options;
using Microsoft.IdentityModel.Tokens;
using Oluso.Core.Domain.Entities;
using Oluso.Core.Domain.Interfaces;
using Oluso.Core.Protocols;
using Oluso.Core.Protocols.Models;
using Oluso.Core.Protocols.Validation;

namespace Oluso.Protocols.Validation;

/// <summary>
/// Default implementation of client authenticator
/// </summary>
public class ClientAuthenticator : IClientAuthenticator
{
    private readonly IClientStore _clientStore;
    private readonly ITenantContext _tenantContext;
    private readonly IConfiguration _configuration;
    private readonly OidcEndpointConfiguration _endpointConfig;
    private readonly IDistributedCache _cache;
    private readonly IHttpContextAccessor _httpContextAccessor;

    // Cache for JTI replay protection (5 minute window)
    private const string JtiCachePrefix = "oluso:client:jwt:jti:";
    private static readonly TimeSpan JtiCacheDuration = TimeSpan.FromMinutes(5);

    public ClientAuthenticator(
        IClientStore clientStore,
        ITenantContext tenantContext,
        IConfiguration configuration,
        IOptions<OidcEndpointConfiguration> endpointConfig,
        IDistributedCache cache,
        IHttpContextAccessor httpContextAccessor)
    {
        _clientStore = clientStore;
        _tenantContext = tenantContext;
        _configuration = configuration;
        _endpointConfig = endpointConfig.Value;
        _cache = cache;
        _httpContextAccessor = httpContextAccessor;
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

        // 4. Try public client (client_id only, no secret)
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
        var issuer = GetIssuer();
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

        // 6. Validate 'jti' for replay protection
        var jti = jwt.Id;
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
            // Store JTI to prevent replay
            var options = new DistributedCacheEntryOptions
            {
                AbsoluteExpirationRelativeToNow = JtiCacheDuration
            };
            await _cache.SetAsync(jtiCacheKey, new byte[] { 1 }, options, cancellationToken);
        }

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
                    var cert = new System.Security.Cryptography.X509Certificates.X509Certificate2(certBytes);
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

        return new ClientAuthenticationResult
        {
            Client = client,
            Method = method
        };
    }

    private string GetIssuer()
    {
        var request = _httpContextAccessor.HttpContext?.Request;
        var baseUrl = _configuration["Oluso:IssuerUri"]
            ?? (request != null ? $"{request.Scheme}://{request.Host}" : "https://localhost");

        if (_tenantContext.HasTenant)
        {
            return $"{baseUrl.TrimEnd('/')}/{_tenantContext.Tenant!.Identifier}";
        }

        return baseUrl;
    }

    private static bool IsSymmetricAlgorithm(string algorithm)
    {
        return algorithm.StartsWith("HS", StringComparison.OrdinalIgnoreCase);
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
