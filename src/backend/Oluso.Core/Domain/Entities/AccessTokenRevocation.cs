namespace Oluso.Core.Domain.Entities;

/// <summary>
/// A revocation of self-contained (JWT) access tokens, which the server does not store and so cannot delete. Either
/// every access token of a client's session issued up to <see cref="RevokedAt"/> (<see cref="SessionId"/>; RFC 6749
/// section 4.1.2 for a replayed authorization code, RFC 7009 for a revoked token of the session), or one token
/// (<see cref="TokenId"/>, its <c>jti</c>). A row is only needed until the last token it covers has expired.
/// </summary>
public class AccessTokenRevocation : TenantEntity
{
    public string Id { get; set; } = Guid.NewGuid().ToString();

    public string ClientId { get; set; } = default!;

    /// <summary>The session (<c>sid</c>) whose tokens issued up to <see cref="RevokedAt"/> are revoked.</summary>
    public string? SessionId { get; set; }

    /// <summary>The <c>jti</c> of the one token revoked.</summary>
    public string? TokenId { get; set; }

    public DateTime RevokedAt { get; set; }

    /// <summary>When the last token this revocation covers expires; the row can be removed after it.</summary>
    public DateTime ExpiresAt { get; set; }
}
