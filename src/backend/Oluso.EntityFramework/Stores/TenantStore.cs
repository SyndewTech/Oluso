using Microsoft.EntityFrameworkCore;
using Oluso.Core.Domain.Entities;
using Oluso.Core.Domain.Interfaces;

namespace Oluso.EntityFramework.Stores;

/// <summary>
/// Entity Framework implementation of ITenantStore
/// </summary>
public class TenantStore : ITenantStore
{
    private readonly IOlusoDbContext _context;

    public TenantStore(IOlusoDbContext context)
    {
        _context = context;
    }

    public async Task<Tenant?> GetByIdAsync(string tenantId, CancellationToken cancellationToken = default)
    {
        if (_context.Tenants == null)
            return null;

        return await _context.Tenants.FindAsync(new object[] { tenantId }, cancellationToken);
    }

    public async Task<Tenant?> GetByIdentifierAsync(string identifier, CancellationToken cancellationToken = default)
    {
        if (_context.Tenants == null)
            return null;

        return await _context.Tenants
            .FirstOrDefaultAsync(t => t.Identifier == identifier, cancellationToken);
    }

    public async Task<IEnumerable<Tenant>> GetAllAsync(CancellationToken cancellationToken = default)
    {
        if (_context.Tenants == null)
            return Enumerable.Empty<Tenant>();

        return await _context.Tenants.ToListAsync(cancellationToken);
    }

    public async Task<Tenant> CreateAsync(Tenant tenant, CancellationToken cancellationToken = default)
    {
        if (_context.Tenants == null)
            throw new InvalidOperationException("Tenants DbSet is not available. Ensure multi-tenancy is enabled.");

        _context.Tenants.Add(tenant);
        await _context.SaveChangesAsync(cancellationToken);
        return tenant;
    }

    public async Task<Tenant> UpdateAsync(Tenant tenant, CancellationToken cancellationToken = default)
    {
        tenant.Updated = DateTime.UtcNow;
        await _context.SaveChangesAsync(cancellationToken);
        return tenant;
    }

    public async Task DeleteAsync(string tenantId, CancellationToken cancellationToken = default)
    {
        if (_context.Tenants == null)
            return;

        var tenant = await _context.Tenants.FindAsync(new object[] { tenantId }, cancellationToken);
        if (tenant != null)
        {
            _context.Tenants.Remove(tenant);
            await _context.SaveChangesAsync(cancellationToken);
        }
    }
}
