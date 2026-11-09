using Microsoft.EntityFrameworkCore;
using Oluso.Core.Domain.Entities;
using Oluso.Core.Domain.Interfaces;

namespace Oluso.EntityFramework.Stores;

/// <summary>
/// Entity Framework implementation of ITenantMembershipStore
/// </summary>
public class TenantMembershipStore : ITenantMembershipStore
{
    private readonly IOlusoDbContext _context;

    public TenantMembershipStore(IOlusoDbContext context)
    {
        _context = context;
    }

    public async Task<IEnumerable<TenantMembership>> GetByTenantAsync(string tenantId, CancellationToken cancellationToken = default)
    {
        return await _context.TenantMemberships
            .Where(tm => tm.TenantId == tenantId)
            .ToListAsync(cancellationToken);
    }

    public async Task<IEnumerable<TenantMembership>> GetByUserAsync(string userId, CancellationToken cancellationToken = default)
    {
        return await _context.TenantMemberships
            .Include(tm => tm.Tenant)
            .Where(tm => tm.UserId == userId)
            .ToListAsync(cancellationToken);
    }

    public async Task<TenantMembership?> GetByTenantAndUserAsync(string tenantId, string userId, CancellationToken cancellationToken = default)
    {
        return await _context.TenantMemberships
            .FirstOrDefaultAsync(tm => tm.TenantId == tenantId && tm.UserId == userId, cancellationToken);
    }

    public async Task<TenantMembership> AddAsync(TenantMembership membership, CancellationToken cancellationToken = default)
    {
        // Idempotent: TenantMembershipInterceptor already creates the membership
        // for a new user's home tenant and for a tenant role grant, so callers
        // that also add one explicitly would otherwise hit the (TenantId, UserId)
        // unique index.
        var existing = await _context.TenantMemberships.FirstOrDefaultAsync(
            tm => tm.TenantId == membership.TenantId && tm.UserId == membership.UserId,
            cancellationToken);

        if (existing != null)
        {
            return existing;
        }

        _context.TenantMemberships.Add(membership);
        await _context.SaveChangesAsync(cancellationToken);
        return membership;
    }

    public async Task RemoveAsync(string tenantId, string userId, CancellationToken cancellationToken = default)
    {
        var membership = await _context.TenantMemberships
            .FirstOrDefaultAsync(tm => tm.TenantId == tenantId && tm.UserId == userId, cancellationToken);

        if (membership != null)
        {
            _context.TenantMemberships.Remove(membership);
            await _context.SaveChangesAsync(cancellationToken);
        }
    }
}
