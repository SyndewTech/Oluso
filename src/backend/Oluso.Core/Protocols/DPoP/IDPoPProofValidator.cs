using Microsoft.IdentityModel.Tokens;

namespace Oluso.Core.Protocols.DPoP;

/// <summary>
/// DPoP proof validation result
/// </summary>
public class DPoPValidationResult
{
    public bool IsValid { get; set; }
    public string? Error { get; set; }
    public string? ErrorDescription { get; set; }

    /// <summary>
    /// The JWK thumbprint (jkt) of the DPoP key
    /// </summary>
    public string? JwkThumbprint { get; set; }

    /// <summary>
    /// The JsonWebKey from the DPoP proof
    /// </summary>
    public JsonWebKey? JsonWebKey { get; set; }

    /// <summary>
    /// Server nonce that should be used in response (if nonce was missing/invalid)
    /// </summary>
    public string? ServerNonce { get; set; }

    /// <summary>
    /// Whether the client should retry with a new nonce
    /// </summary>
    public bool RequiresNonce { get; set; }

    public static DPoPValidationResult Success(string jkt, JsonWebKey jwk) => new()
    {
        IsValid = true,
        JwkThumbprint = jkt,
        JsonWebKey = jwk
    };

    public static DPoPValidationResult Failure(string error, string? description = null) => new()
    {
        IsValid = false,
        Error = error,
        ErrorDescription = description
    };

    public static DPoPValidationResult NonceRequired(string nonce) => new()
    {
        IsValid = false,
        Error = "use_dpop_nonce",
        ErrorDescription = "DPoP nonce is required",
        RequiresNonce = true,
        ServerNonce = nonce
    };
}

/// <summary>
/// Context for DPoP validation
/// </summary>
public class DPoPValidationContext
{
    /// <summary>
    /// The raw DPoP proof JWT from the DPoP header
    /// </summary>
    public required string Proof { get; init; }

    /// <summary>
    /// The HTTP method of the request (htm claim)
    /// </summary>
    public required string HttpMethod { get; init; }

    /// <summary>
    /// The HTTP URI of the request (htu claim)
    /// </summary>
    public required string HttpUri { get; init; }

    /// <summary>
    /// Expected access token hash (ath claim) - for resource server validation
    /// </summary>
    public string? ExpectedAccessTokenHash { get; init; }

    /// <summary>
    /// Expected JWK thumbprint - for validating proof matches previously bound key
    /// </summary>
    public string? ExpectedJwkThumbprint { get; init; }

    /// <summary>
    /// Whether to require server nonce
    /// </summary>
    public bool RequireNonce { get; init; }

    /// <summary>
    /// Client ID for nonce validation scope
    /// </summary>
    public string? ClientId { get; init; }
}

/// <summary>
/// DPoP proof validator per RFC 9449
/// </summary>
public interface IDPoPProofValidator
{
    /// <summary>
    /// Validates a DPoP proof JWT
    /// </summary>
    Task<DPoPValidationResult> ValidateAsync(DPoPValidationContext context, CancellationToken cancellationToken = default);

    /// <summary>
    /// Computes the access token hash (ath) for a given access token
    /// </summary>
    string ComputeAccessTokenHash(string accessToken);
}

/// <summary>
/// Store for DPoP nonces and JTI replay protection
/// </summary>
public interface IDPoPNonceStore
{
    /// <summary>
    /// Whether server nonces are required
    /// </summary>
    bool IsNonceRequired { get; }

    /// <summary>
    /// Generates a new nonce for the client
    /// </summary>
    Task<string> GenerateNonceAsync(string? clientId, CancellationToken cancellationToken = default);

    /// <summary>
    /// Validates a nonce
    /// </summary>
    Task<bool> ValidateNonceAsync(string nonce, string? clientId, CancellationToken cancellationToken = default);

    /// <summary>
    /// Validates JTI for replay protection
    /// </summary>
    Task<bool> ValidateJtiAsync(string jti, TimeSpan lifetime, CancellationToken cancellationToken = default);
}
