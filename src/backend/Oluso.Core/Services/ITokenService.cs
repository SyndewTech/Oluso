using Oluso.Core.Protocols;
using Oluso.Core.Protocols.Models;

namespace Oluso.Core.Services;

/// <summary>
/// Service for creating tokens (access tokens, ID tokens, refresh tokens)
/// </summary>
public interface ITokenService
{
    /// <summary>
    /// Creates a token response from a grant result
    /// </summary>
    Task<TokenResponse> CreateTokenResponseAsync(
        GrantResult grant,
        TokenRequest request,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Creates an access token
    /// </summary>
    Task<string> CreateAccessTokenAsync(
        TokenCreationRequest request,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Creates an ID token
    /// </summary>
    Task<string> CreateIdTokenAsync(
        TokenCreationRequest request,
        string? accessTokenHash = null,
        string? codeHash = null,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Creates a refresh token
    /// </summary>
    Task<string> CreateRefreshTokenAsync(
        TokenCreationRequest request,
        CancellationToken cancellationToken = default);
}

/// <summary>
/// Request for token creation
/// </summary>
public class TokenCreationRequest
{
    public string? SubjectId { get; set; }
    public string ClientId { get; set; } = default!;
    public string? ClientName { get; set; }
    public ICollection<string> Scopes { get; set; } = new List<string>();
    public IDictionary<string, object> Claims { get; set; } = new Dictionary<string, object>();

    /// <summary>
    /// Access token lifetime in seconds
    /// </summary>
    public int Lifetime { get; set; }

    /// <summary>
    /// Identity token lifetime in seconds (defaults to 300 if not set)
    /// </summary>
    public int IdentityTokenLifetime { get; set; }

    public string? SessionId { get; set; }
    public string? Nonce { get; set; }
    public DateTime? AuthTime { get; set; }
    public ICollection<string>? Amr { get; set; }
    public string? Acr { get; set; }
    public ICollection<string>? Audiences { get; set; }
    public bool IsReference { get; set; }

    /// <summary>
    /// Whether to include JWT ID (jti) claim
    /// </summary>
    public bool IncludeJwtId { get; set; } = true;

    // DPoP
    public string? DPoPKeyThumbprint { get; set; }

    /// <summary>
    /// Salt for computing pairwise subject identifiers.
    /// When set, the subject ID will be hashed with this salt to create a unique
    /// identifier per client (RFC 7519 Section 8, OpenID Connect Core Section 8).
    /// </summary>
    public string? PairWiseSubjectSalt { get; set; }
}
