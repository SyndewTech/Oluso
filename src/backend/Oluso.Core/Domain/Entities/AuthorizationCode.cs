namespace Oluso.Core.Domain.Entities;

/// <summary>
/// Represents an OAuth 2.0 authorization code
/// </summary>
public class AuthorizationCode : TenantEntity
{
    public string Code { get; set; } = null!;
    public string ClientId { get; set; } = null!;
    public string SubjectId { get; set; } = null!;
    public string RedirectUri { get; set; } = null!;
    public List<string> Scopes { get; set; } = new();
    public string? CodeChallenge { get; set; }
    public string? CodeChallengeMethod { get; set; }
    public string? Nonce { get; set; }
    public string? SessionId { get; set; }
    public DateTime CreationTime { get; set; }
    public DateTime Expiration { get; set; }
    public bool IsConsumed { get; set; }
    public DateTime? ConsumedTime { get; set; }

    /// <summary>
    /// Additional claims to include in tokens
    /// </summary>
    public Dictionary<string, string>? Claims { get; set; }

    /// <summary>
    /// Properties for OIDC extensions
    /// </summary>
    public Dictionary<string, string>? Properties { get; set; }
}
