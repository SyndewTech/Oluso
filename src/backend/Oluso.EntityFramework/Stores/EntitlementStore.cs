using Microsoft.EntityFrameworkCore;
using Oluso.Core.Domain.Entities;
using Oluso.Core.Domain.Interfaces;

namespace Oluso.EntityFramework.Stores;

public class EntitlementStore : IEntitlementStore
{
    private readonly IOlusoDbContext _context;

    public EntitlementStore(IOlusoDbContext context)
    {
        _context = context;
    }

    public Task<Entitlement?> GetAsync(string tenantId, string id, CancellationToken cancellationToken = default)
    {
        return _context.Entitlements
            .FirstOrDefaultAsync(e => e.TenantId == tenantId && e.Id == id, cancellationToken);
    }

    public async Task<IReadOnlyList<Entitlement>> ListAsync(string tenantId, CancellationToken cancellationToken = default)
    {
        return await _context.Entitlements
            .Where(e => e.TenantId == tenantId)
            .OrderBy(e => e.Group)
            .ThenBy(e => e.DisplayName)
            .ToListAsync(cancellationToken);
    }

    public async Task<IReadOnlyList<Entitlement>> FindByNamesAsync(
        string? tenantId,
        IEnumerable<string> names,
        CancellationToken cancellationToken = default)
    {
        var wanted = names.Distinct(StringComparer.Ordinal).ToList();
        if (wanted.Count == 0)
        {
            return Array.Empty<Entitlement>();
        }

        return await _context.Entitlements
            .Where(e => e.TenantId == tenantId && wanted.Contains(e.Name))
            .ToListAsync(cancellationToken);
    }

    public async Task CreateAsync(Entitlement entitlement, CancellationToken cancellationToken = default)
    {
        entitlement.CreatedAt = DateTime.UtcNow;
        _context.Entitlements.Add(entitlement);
        await _context.SaveChangesAsync(cancellationToken);
    }

    public async Task UpdateAsync(Entitlement entitlement, CancellationToken cancellationToken = default)
    {
        entitlement.UpdatedAt = DateTime.UtcNow;
        _context.Entitlements.Update(entitlement);
        await _context.SaveChangesAsync(cancellationToken);
    }

    public async Task DeleteAsync(string tenantId, string id, CancellationToken cancellationToken = default)
    {
        var existing = await _context.Entitlements
            .FirstOrDefaultAsync(e => e.TenantId == tenantId && e.Id == id, cancellationToken);

        if (existing == null)
        {
            return;
        }

        _context.Entitlements.Remove(existing);
        await _context.SaveChangesAsync(cancellationToken);
    }
}
