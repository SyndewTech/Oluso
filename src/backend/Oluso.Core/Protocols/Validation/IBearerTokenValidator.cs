using Oluso.Core.Common;

namespace Oluso.Core.Protocols.Validation;

/// <summary>
/// Result of bearer token validation
/// </summary>
public class BearerTokenValidationResult : ValidationResult
{
    /// <summary>
    /// The subject ID from the token (if present)
    /// </summary>
    public string? SubjectId { get; set; }

    /// <summary>
    /// The client ID that the token was issued to
    /// </summary>
    public string? ClientId { get; set; }

    /// <summary>
    /// The scopes granted in the token
    /// </summary>
    public ICollection<string> Scopes { get; set; } = new List<string>();

    /// <summary>
    /// All claims from the token
    /// </summary>
    public IDictionary<string, object> Claims { get; set; } = new Dictionary<string, object>();

    /// <summary>
    /// The token type (jwt or reference)
    /// </summary>
    public string? TokenType { get; set; }

    /// <summary>
    /// DPoP key thumbprint if token is DPoP-bound
    /// </summary>
    public string? DPoPKeyThumbprint { get; set; }

    /// <summary>
    /// When the token expires
    /// </summary>
    public DateTime? Expiration { get; set; }

    /// <summary>
    /// Session ID if present
    /// </summary>
    public string? SessionId { get; set; }

    public static BearerTokenValidationResult Success(
        string? subjectId,
        string? clientId,
        ICollection<string> scopes,
        IDictionary<string, object>? claims = null)
    {
        return new BearerTokenValidationResult
        {
            SubjectId = subjectId,
            ClientId = clientId,
            Scopes = scopes,
            Claims = claims ?? new Dictionary<string, object>()
        };
    }

    public new static BearerTokenValidationResult Failure(string error, string? errorDescription = null)
    {
        return new BearerTokenValidationResult
        {
            Error = error,
            ErrorDescription = errorDescription
        };
    }
}

/// <summary>
/// Context for bearer token validation
/// </summary>
public class BearerTokenValidationContext
{
    /// <summary>
    /// The bearer token to validate
    /// </summary>
    public string Token { get; set; } = default!;

    /// <summary>
    /// Required scopes (any of these must be present)
    /// </summary>
    public ICollection<string>? RequiredScopes { get; set; }

    /// <summary>
    /// If true, all required scopes must be present. If false, at least one must be present.
    /// </summary>
    public bool RequireAllScopes { get; set; } = false;

    /// <summary>
    /// DPoP proof if provided (for sender-constrained tokens)
    /// </summary>
    public string? DPoPProof { get; set; }

    /// <summary>
    /// HTTP method for DPoP validation
    /// </summary>
    public string? HttpMethod { get; set; }

    /// <summary>
    /// HTTP URI for DPoP validation
    /// </summary>
    public string? HttpUri { get; set; }

    /// <summary>
    /// Expected audience (optional, defaults to issuer validation only)
    /// </summary>
    public string? ExpectedAudience { get; set; }
}

/// <summary>
/// Validates bearer tokens (both JWT and reference tokens) with support for DPoP.
/// This provides a unified interface for token validation across the application.
/// </summary>
public interface IBearerTokenValidator
{
    /// <summary>
    /// Validates a bearer token
    /// </summary>
    /// <param name="context">The validation context containing the token and requirements</param>
    /// <param name="cancellationToken">Cancellation token</param>
    /// <returns>Validation result with token claims if successful</returns>
    Task<BearerTokenValidationResult> ValidateAsync(
        BearerTokenValidationContext context,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Simple validation for a token string without additional context
    /// </summary>
    Task<BearerTokenValidationResult> ValidateAsync(
        string token,
        CancellationToken cancellationToken = default);
}
