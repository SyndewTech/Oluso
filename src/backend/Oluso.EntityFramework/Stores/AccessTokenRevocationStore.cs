using Microsoft.EntityFrameworkCore;
using Oluso.Core.Domain.Entities;
using Oluso.Core.Domain.Interfaces;

namespace Oluso.EntityFramework.Stores;

public class AccessTokenRevocationStore : IAccessTokenRevocationStore
{
    private readonly IOlusoDbContext _context;

    public AccessTokenRevocationStore(IOlusoDbContext context)
    {
        _context = context;
    }

    public Task RevokeSessionAsync(string? tenantId, string clientId, string sessionId, DateTime expiresAt,
        CancellationToken cancellationToken = default) =>
        AddAsync(new AccessTokenRevocation
        {
            TenantId = tenantId,
            ClientId = clientId,
            SessionId = sessionId,
            RevokedAt = DateTime.UtcNow,
            ExpiresAt = expiresAt
        }, cancellationToken);

    public Task RevokeTokenAsync(string? tenantId, string clientId, string tokenId, DateTime expiresAt,
        CancellationToken cancellationToken = default) =>
        AddAsync(new AccessTokenRevocation
        {
            TenantId = tenantId,
            ClientId = clientId,
            TokenId = tokenId,
            RevokedAt = DateTime.UtcNow,
            ExpiresAt = expiresAt
        }, cancellationToken);

    public async Task<bool> IsRevokedAsync(string? tenantId, string clientId, string? sessionId, string? tokenId,
        DateTime issuedAt, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrEmpty(sessionId) && string.IsNullOrEmpty(tokenId))
            return false;

        // The token's tenant comes from the token itself, not the request's tenant context.
        return await _context.AccessTokenRevocations
            .IgnoreQueryFilters()
            .AnyAsync(r => r.ClientId == clientId && r.TenantId == tenantId &&
                ((sessionId != null && r.SessionId == sessionId && issuedAt <= r.RevokedAt) ||
                 (tokenId != null && r.TokenId == tokenId)), cancellationToken);
    }

    public async Task<int> RemoveExpiredAsync(DateTime nowUtc, CancellationToken cancellationToken = default) =>
        await _context.AccessTokenRevocations
            .IgnoreQueryFilters()
            .Where(r => r.ExpiresAt < nowUtc)
            .ExecuteDeleteAsync(cancellationToken);

    private async Task AddAsync(AccessTokenRevocation revocation, CancellationToken cancellationToken)
    {
        _context.AccessTokenRevocations.Add(revocation);
        await _context.SaveChangesAsync(cancellationToken);
    }
}
