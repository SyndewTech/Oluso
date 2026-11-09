using Microsoft.EntityFrameworkCore;
using Oluso.Core.Domain.Entities;
using Oluso.Core.Domain.Interfaces;

namespace Oluso.EntityFramework.Stores;

public class ActionTokenStore : IActionTokenStore
{
    private readonly IOlusoDbContext _context;

    public ActionTokenStore(IOlusoDbContext context)
    {
        _context = context;
    }

    public async Task<ActionToken> CreateAsync(ActionToken token, CancellationToken cancellationToken = default)
    {
        _context.ActionTokens.Add(token);
        await _context.SaveChangesAsync(cancellationToken);
        return token;
    }

    public async Task<ActionToken?> GetByHashAsync(string tokenHash, CancellationToken cancellationToken = default)
    {
        // IgnoreQueryFilters because anonymous (no tenant context) lookups
        // need to find the token before its tenant is even known.
        // Hash uniqueness still means at most one row.
        return await _context.ActionTokens
            .IgnoreQueryFilters()
            .FirstOrDefaultAsync(t => t.TokenHash == tokenHash, cancellationToken);
    }

    public async Task<bool> TryConsumeAsync(string id, DateTime consumedAtUtc, CancellationToken cancellationToken = default)
    {
        // Single UPDATE with a "still null" guard so two parallel redeems
        // of the same raw token can never both succeed — the second one's
        // affected-rows count is zero.
        var affected = await _context.ActionTokens
            .IgnoreQueryFilters()
            .Where(t => t.Id == id && t.ConsumedAt == null)
            .ExecuteUpdateAsync(
                s => s.SetProperty(t => t.ConsumedAt, consumedAtUtc),
                cancellationToken);
        return affected > 0;
    }

    public async Task ReleaseAsync(string id, CancellationToken cancellationToken = default)
    {
        await _context.ActionTokens
            .IgnoreQueryFilters()
            .Where(t => t.Id == id && t.ConsumedAt != null)
            .ExecuteUpdateAsync(s => s.SetProperty(t => t.ConsumedAt, (DateTime?)null), cancellationToken);
    }

    public async Task<int> RemoveExpiredAsync(DateTime nowUtc, CancellationToken cancellationToken = default)
    {
        return await _context.ActionTokens
            .IgnoreQueryFilters()
            .Where(t => t.ExpiresAt < nowUtc)
            .ExecuteDeleteAsync(cancellationToken);
    }
}
