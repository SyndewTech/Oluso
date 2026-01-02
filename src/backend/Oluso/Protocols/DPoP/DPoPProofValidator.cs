using System.IdentityModel.Tokens.Jwt;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using Microsoft.IdentityModel.Tokens;
using Oluso.Core.Protocols.DPoP;

namespace Oluso.Protocols.DPoP;

/// <summary>
/// Default implementation of DPoP proof validator per RFC 9449
/// </summary>
public class DPoPProofValidator : IDPoPProofValidator
{
    private readonly IDPoPNonceStore _nonceStore;
    private readonly ILogger<DPoPProofValidator> _logger;
    private readonly TimeSpan _proofLifetime;
    private readonly TimeSpan _clockSkew;

    // Supported algorithms for DPoP
    private static readonly HashSet<string> SupportedAlgorithms = new(StringComparer.OrdinalIgnoreCase)
    {
        SecurityAlgorithms.RsaSha256,
        SecurityAlgorithms.RsaSha384,
        SecurityAlgorithms.RsaSha512,
        SecurityAlgorithms.EcdsaSha256,
        SecurityAlgorithms.EcdsaSha384,
        SecurityAlgorithms.EcdsaSha512,
        SecurityAlgorithms.RsaSsaPssSha256,
        SecurityAlgorithms.RsaSsaPssSha384,
        SecurityAlgorithms.RsaSsaPssSha512
    };

    public DPoPProofValidator(
        IDPoPNonceStore nonceStore,
        IConfiguration configuration,
        ILogger<DPoPProofValidator> logger)
    {
        _nonceStore = nonceStore;
        _logger = logger;
        _proofLifetime = TimeSpan.FromSeconds(
            configuration.GetValue("Oluso:DPoP:ProofLifetimeSeconds",
                configuration.GetValue("IdentityServer:DPoP:ProofLifetimeSeconds", 60)));
        _clockSkew = TimeSpan.FromSeconds(
            configuration.GetValue("Oluso:DPoP:ClockSkewSeconds",
                configuration.GetValue("IdentityServer:DPoP:ClockSkewSeconds", 5)));
    }

    public async Task<DPoPValidationResult> ValidateAsync(
        DPoPValidationContext context,
        CancellationToken cancellationToken = default)
    {
        try
        {
            // Parse the JWT without validation first to get the header
            var handler = new JwtSecurityTokenHandler();
            if (!handler.CanReadToken(context.Proof))
            {
                return DPoPValidationResult.Failure("invalid_dpop_proof", "Invalid JWT format");
            }

            var jwt = handler.ReadJwtToken(context.Proof);

            // 1. Validate typ header
            if (!jwt.Header.TryGetValue("typ", out var typValue) ||
                typValue?.ToString()?.Equals("dpop+jwt", StringComparison.OrdinalIgnoreCase) != true)
            {
                return DPoPValidationResult.Failure("invalid_dpop_proof", "typ header must be 'dpop+jwt'");
            }

            // 2. Validate alg header (must be asymmetric)
            var alg = jwt.Header.Alg;
            if (string.IsNullOrEmpty(alg) || !SupportedAlgorithms.Contains(alg))
            {
                return DPoPValidationResult.Failure("invalid_dpop_proof",
                    $"Unsupported algorithm: {alg}. Must be an asymmetric algorithm.");
            }

            // 3. Extract and validate jwk header
            if (!jwt.Header.TryGetValue("jwk", out var jwkValue) || jwkValue == null)
            {
                return DPoPValidationResult.Failure("invalid_dpop_proof", "jwk header is required");
            }

            JsonWebKey jwk;
            try
            {
                var jwkJson = jwkValue is string jwkString
                    ? jwkString
                    : JsonSerializer.Serialize(jwkValue);
                jwk = new JsonWebKey(jwkJson);
            }
            catch (Exception ex)
            {
                _logger.LogDebug(ex, "Failed to parse JWK from DPoP proof");
                return DPoPValidationResult.Failure("invalid_dpop_proof", "Invalid jwk in header");
            }

            // 4. Validate jwk is a public key (no private key material)
            if (jwk.HasPrivateKey)
            {
                return DPoPValidationResult.Failure("invalid_dpop_proof",
                    "jwk must not contain private key material");
            }

            // 5. Validate signature
            var validationParameters = new TokenValidationParameters
            {
                ValidateIssuer = false,
                ValidateAudience = false,
                ValidateLifetime = false, // We'll validate iat manually
                ValidateIssuerSigningKey = true,
                IssuerSigningKey = jwk,
                ValidAlgorithms = new[] { alg }
            };

            try
            {
                handler.ValidateToken(context.Proof, validationParameters, out _);
            }
            catch (SecurityTokenException ex)
            {
                _logger.LogDebug(ex, "DPoP proof signature validation failed");
                return DPoPValidationResult.Failure("invalid_dpop_proof", "Signature validation failed");
            }

            // 6. Validate required claims
            // jti - unique identifier
            var jti = jwt.Payload.Jti;
            if (string.IsNullOrEmpty(jti))
            {
                return DPoPValidationResult.Failure("invalid_dpop_proof", "jti claim is required");
            }

            // htm - HTTP method
            if (!jwt.Payload.TryGetValue("htm", out var htmValue) ||
                htmValue?.ToString()?.Equals(context.HttpMethod, StringComparison.OrdinalIgnoreCase) != true)
            {
                return DPoPValidationResult.Failure("invalid_dpop_proof",
                    $"htm claim must match request method '{context.HttpMethod}'");
            }

            // htu - HTTP URI
            if (!jwt.Payload.TryGetValue("htu", out var htuValue))
            {
                return DPoPValidationResult.Failure("invalid_dpop_proof", "htu claim is required");
            }

            if (!ValidateHtu(htuValue?.ToString(), context.HttpUri))
            {
                return DPoPValidationResult.Failure("invalid_dpop_proof",
                    "htu claim does not match request URI");
            }

            // iat - issued at time
            var issuedAt = jwt.Payload.IssuedAt;
            if (issuedAt == default)
            {
                return DPoPValidationResult.Failure("invalid_dpop_proof", "iat claim is required");
            }

            var now = DateTime.UtcNow;

            if (issuedAt > now.Add(_clockSkew))
            {
                return DPoPValidationResult.Failure("invalid_dpop_proof", "Proof is issued in the future");
            }

            if (issuedAt < now.Subtract(_proofLifetime).Subtract(_clockSkew))
            {
                return DPoPValidationResult.Failure("invalid_dpop_proof", "Proof has expired");
            }

            // 7. Validate nonce if required
            if (context.RequireNonce || _nonceStore.IsNonceRequired)
            {
                jwt.Payload.TryGetValue("nonce", out var nonceValue);
                var nonce = nonceValue?.ToString();

                if (string.IsNullOrEmpty(nonce))
                {
                    var newNonce = await _nonceStore.GenerateNonceAsync(context.ClientId, cancellationToken);
                    return DPoPValidationResult.NonceRequired(newNonce);
                }

                var nonceValid = await _nonceStore.ValidateNonceAsync(nonce, context.ClientId, cancellationToken);
                if (!nonceValid)
                {
                    var newNonce = await _nonceStore.GenerateNonceAsync(context.ClientId, cancellationToken);
                    return DPoPValidationResult.NonceRequired(newNonce);
                }
            }

            // 8. Validate jti uniqueness (replay protection)
            var jtiValid = await _nonceStore.ValidateJtiAsync(jti, _proofLifetime, cancellationToken);
            if (!jtiValid)
            {
                return DPoPValidationResult.Failure("invalid_dpop_proof", "Proof has already been used (jti)");
            }

            // 9. Validate access token hash (ath) if provided
            if (!string.IsNullOrEmpty(context.ExpectedAccessTokenHash))
            {
                if (!jwt.Payload.TryGetValue("ath", out var athValue) ||
                    athValue?.ToString() != context.ExpectedAccessTokenHash)
                {
                    return DPoPValidationResult.Failure("invalid_dpop_proof",
                        "ath claim does not match access token");
                }
            }

            // 10. Compute JWK thumbprint
            var jkt = ComputeJwkThumbprint(jwk);

            // 11. Validate thumbprint matches expected (if binding to previous key)
            if (!string.IsNullOrEmpty(context.ExpectedJwkThumbprint) &&
                context.ExpectedJwkThumbprint != jkt)
            {
                return DPoPValidationResult.Failure("invalid_dpop_proof",
                    "DPoP key does not match bound key");
            }

            _logger.LogDebug("DPoP proof validated successfully, jkt: {Jkt}", jkt);
            return DPoPValidationResult.Success(jkt, jwk);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Unexpected error validating DPoP proof");
            return DPoPValidationResult.Failure("invalid_dpop_proof", "Failed to validate DPoP proof");
        }
    }

    public string ComputeAccessTokenHash(string accessToken)
    {
        using var sha256 = SHA256.Create();
        var hash = sha256.ComputeHash(Encoding.ASCII.GetBytes(accessToken));
        return Base64UrlEncoder.Encode(hash);
    }

    private static string ComputeJwkThumbprint(JsonWebKey jwk)
    {
        // Per RFC 7638: JWK Thumbprint
        // For RSA: {"e":"...","kty":"RSA","n":"..."}
        // For EC: {"crv":"...","kty":"EC","x":"...","y":"..."}
        // Members must be in lexicographic order

        string thumbprintInput;

        if (jwk.Kty == JsonWebAlgorithmsKeyTypes.RSA)
        {
            thumbprintInput = $"{{\"e\":\"{jwk.E}\",\"kty\":\"RSA\",\"n\":\"{jwk.N}\"}}";
        }
        else if (jwk.Kty == JsonWebAlgorithmsKeyTypes.EllipticCurve)
        {
            thumbprintInput = $"{{\"crv\":\"{jwk.Crv}\",\"kty\":\"EC\",\"x\":\"{jwk.X}\",\"y\":\"{jwk.Y}\"}}";
        }
        else
        {
            throw new NotSupportedException($"Key type {jwk.Kty} not supported for thumbprint");
        }

        using var sha256 = SHA256.Create();
        var hash = sha256.ComputeHash(Encoding.UTF8.GetBytes(thumbprintInput));
        return Base64UrlEncoder.Encode(hash);
    }

    private static bool ValidateHtu(string? htu, string requestUri)
    {
        if (string.IsNullOrEmpty(htu))
            return false;

        try
        {
            var htuUri = new Uri(htu);
            var reqUri = new Uri(requestUri);

            // Per RFC 9449: Compare scheme, host, port, and path (ignore query and fragment)
            return htuUri.Scheme.Equals(reqUri.Scheme, StringComparison.OrdinalIgnoreCase) &&
                   htuUri.Host.Equals(reqUri.Host, StringComparison.OrdinalIgnoreCase) &&
                   htuUri.Port == reqUri.Port &&
                   htuUri.AbsolutePath.Equals(reqUri.AbsolutePath, StringComparison.OrdinalIgnoreCase);
        }
        catch
        {
            return false;
        }
    }
}
