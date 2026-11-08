using Microsoft.EntityFrameworkCore;
using Oluso.Core.Domain.Entities;
using Oluso.Core.Domain.Interfaces;

namespace Oluso.EntityFramework.Stores;

public class TenantEmailDomainStore : ITenantEmailDomainStore
{
    private readonly IOlusoDbContext _context;

    public TenantEmailDomainStore(IOlusoDbContext context)
    {
        _context = context;
    }

    public Task<TenantEmailDomain?> GetByIdAsync(string id, CancellationToken ct = default)
        => _context.TenantEmailDomains.FirstOrDefaultAsync(d => d.Id == id, ct);

    public async Task<IReadOnlyList<TenantEmailDomain>> GetByTenantIdAsync(string tenantId, CancellationToken ct = default)
        => await _context.TenantEmailDomains
            .Where(d => d.TenantId == tenantId && d.Status != TenantEmailDomainStatus.Removed)
            .OrderByDescending(d => d.CreatedAt)
            .ToListAsync(ct);

    public Task<TenantEmailDomain?> GetByTenantAndDomainAsync(string tenantId, string domain, CancellationToken ct = default)
        => _context.TenantEmailDomains.FirstOrDefaultAsync(d => d.TenantId == tenantId && d.Domain == domain, ct);

    public Task<TenantEmailDomain?> GetVerifiedByDomainAsync(string domain, CancellationToken ct = default)
        => _context.TenantEmailDomains.FirstOrDefaultAsync(
            d => d.Domain == domain && d.Status == TenantEmailDomainStatus.Verified, ct);

    public async Task<bool> IsVerifiedForTenantAsync(string tenantId, string? email, CancellationToken ct = default)
    {
        var covering = TenantEmailDomain.CoveringDomains(email);
        if (covering.Count == 0 || string.IsNullOrEmpty(tenantId))
        {
            return false;
        }

        return await _context.TenantEmailDomains.AnyAsync(d =>
            d.TenantId == tenantId
            && d.Status == TenantEmailDomainStatus.Verified
            && covering.Contains(d.Domain), ct);
    }

    public async Task<IReadOnlyList<TenantEmailDomain>> GetPendingAsync(CancellationToken ct = default)
        => await _context.TenantEmailDomains
            .Where(d => d.Status == TenantEmailDomainStatus.Pending)
            .OrderBy(d => d.CreatedAt)
            .ToListAsync(ct);

    public async Task<IReadOnlyList<TenantEmailDomain>> GetVerifiedForRevalidationAsync(DateTime revalidateBefore, CancellationToken ct = default)
        => await _context.TenantEmailDomains
            .Where(d => d.Status == TenantEmailDomainStatus.Verified
                && (d.LastRevalidatedAt == null || d.LastRevalidatedAt < revalidateBefore))
            .OrderBy(d => d.LastRevalidatedAt)
            .ToListAsync(ct);

    public async Task<TenantEmailDomain> CreateAsync(TenantEmailDomain domain, CancellationToken ct = default)
    {
        _context.TenantEmailDomains.Add(domain);
        await ((DbContext)_context).SaveChangesAsync(ct);
        return domain;
    }

    public async Task<TenantEmailDomain> UpdateAsync(TenantEmailDomain domain, CancellationToken ct = default)
    {
        ((DbContext)_context).Entry(domain).State = EntityState.Modified;
        await ((DbContext)_context).SaveChangesAsync(ct);
        return domain;
    }
}
