using Microsoft.EntityFrameworkCore;
using Oluso.Core.Domain.Entities;
using Oluso.Core.Domain.Interfaces;

namespace Oluso.EntityFramework.Stores;

public class CustomDomainVerificationStore : ICustomDomainVerificationStore
{
    private readonly IOlusoDbContext _context;

    public CustomDomainVerificationStore(IOlusoDbContext context)
    {
        _context = context;
    }

    public async Task<CustomDomainVerification?> GetByIdAsync(string id, CancellationToken ct = default)
        => await _context.CustomDomainVerifications
            .Include(d => d.Tenant)
            .FirstOrDefaultAsync(d => d.Id == id, ct);

    public async Task<IEnumerable<CustomDomainVerification>> GetByTenantIdAsync(string tenantId, CancellationToken ct = default)
        => await _context.CustomDomainVerifications
            .Where(d => d.TenantId == tenantId && d.Status != CustomDomainStatus.Removed)
            .OrderByDescending(d => d.CreatedAt)
            .ToListAsync(ct);

    public async Task<CustomDomainVerification?> GetByDomainAsync(string domain, CancellationToken ct = default)
        => await _context.CustomDomainVerifications
            .FirstOrDefaultAsync(d => d.Domain == domain && d.Status != CustomDomainStatus.Removed, ct);

    public async Task<IEnumerable<CustomDomainVerification>> GetPendingVerificationsAsync(CancellationToken ct = default)
        => await _context.CustomDomainVerifications
            .Where(d => d.Status == CustomDomainStatus.Pending || d.Status == CustomDomainStatus.Verified)
            .OrderBy(d => d.CreatedAt)
            .ToListAsync(ct);

    public async Task<IEnumerable<CustomDomainVerification>> GetServableDomainsAsync(CancellationToken ct = default)
        => await _context.CustomDomainVerifications
            .Where(d => d.Status == CustomDomainStatus.Verified || d.Status == CustomDomainStatus.Active)
            .ToListAsync(ct);

    public async Task<IEnumerable<CustomDomainVerification>> GetActiveDomainsForRevalidationAsync(DateTime revalidateBefore, CancellationToken ct = default)
        => await _context.CustomDomainVerifications
            .Where(d => d.Status == CustomDomainStatus.Active
                && (d.LastRevalidatedAt == null || d.LastRevalidatedAt < revalidateBefore))
            .OrderBy(d => d.LastRevalidatedAt)
            .ToListAsync(ct);

    public async Task<CustomDomainVerification> CreateAsync(CustomDomainVerification verification, CancellationToken ct = default)
    {
        _context.CustomDomainVerifications.Add(verification);
        await ((DbContext)_context).SaveChangesAsync(ct);
        return verification;
    }

    public async Task<CustomDomainVerification> UpdateAsync(CustomDomainVerification verification, CancellationToken ct = default)
    {
        ((DbContext)_context).Entry(verification).State = EntityState.Modified;
        await ((DbContext)_context).SaveChangesAsync(ct);
        return verification;
    }

    public async Task<bool> DeleteAsync(string id, CancellationToken ct = default)
    {
        var entity = await _context.CustomDomainVerifications.FirstOrDefaultAsync(d => d.Id == id, ct);
        if (entity == null) return false;
        entity.Status = CustomDomainStatus.Removed;
        await ((DbContext)_context).SaveChangesAsync(ct);
        return true;
    }
}
