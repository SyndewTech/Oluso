using Oluso.Core.Domain.Entities;

namespace Oluso.Core.Domain.Interfaces;

/// <summary>
/// Persistence for <see cref="AccessTokenRevocation"/>: revoked self-contained access tokens, checked whenever one
/// is presented to this server (resource endpoints, userinfo, introspection).
/// </summary>
public interface IAccessTokenRevocationStore
{
    /// <summary>Revokes the client's access tokens of <paramref name="sessionId"/> issued up to now.</summary>
    Task RevokeSessionAsync(string? tenantId, string clientId, string sessionId, DateTime expiresAt,
        CancellationToken cancellationToken = default);

    /// <summary>Revokes the one access token whose <c>jti</c> is <paramref name="tokenId"/>.</summary>
    Task RevokeTokenAsync(string? tenantId, string clientId, string tokenId, DateTime expiresAt,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Whether a token of <paramref name="clientId"/>, with the given <c>sid</c>, <c>jti</c> and issue time, has
    /// been revoked.
    /// </summary>
    Task<bool> IsRevokedAsync(string? tenantId, string clientId, string? sessionId, string? tokenId, DateTime issuedAt,
        CancellationToken cancellationToken = default);

    /// <summary>Deletes revocations whose tokens have all expired. Returns the number removed.</summary>
    Task<int> RemoveExpiredAsync(DateTime nowUtc, CancellationToken cancellationToken = default);
}
