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

        // Case-insensitive — Postgres's default collation is case-sensitive
        // and "==" would translate to a case-sensitive SQL "=". A tenant
        // stored as "Summitech" wouldn't match an incoming "summitech" from
        // a subdomain or SSO landing form.
        var normalized = identifier.ToLowerInvariant();
        return await _context.Tenants
            .FirstOrDefaultAsync(t => t.Identifier.ToLower() == normalized, cancellationToken);
    }

    public async Task<Tenant?> GetByCustomDomainAsync(string domain, CancellationToken cancellationToken = default)
    {
        if (_context.Tenants == null)
            return null;

        // DNS is case-insensitive — match accordingly so a Host header
        // arriving as "Acme.Example.com" matches a CustomDomain stored
        // as "acme.example.com".
        var normalized = domain.ToLowerInvariant();
        return await _context.Tenants
            .FirstOrDefaultAsync(t => t.CustomDomain!.ToLower() == normalized && t.Enabled, cancellationToken);
    }

    public async Task<IEnumerable<Tenant>> GetAllAsync(CancellationToken cancellationToken = default)
    {
        if (_context.Tenants == null)
            return Enumerable.Empty<Tenant>();

        return await _context.Tenants.ToListAsync(cancellationToken);
    }

    public async Task<IEnumerable<Tenant>> GetByOrganizationAsync(string organizationId, CancellationToken cancellationToken = default)
    {
        if (_context.Tenants == null)
            return Enumerable.Empty<Tenant>();

        return await _context.Tenants
            .Where(t => t.OrganizationId == organizationId)
            .OrderBy(t => t.Environment)
            .ThenBy(t => t.Name)
            .ToListAsync(cancellationToken);
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

    public async Task<(IReadOnlyList<Tenant> Items, int TotalCount)> SearchAsync(TenantSearchQuery query, CancellationToken cancellationToken = default)
    {
        if (_context.Tenants == null || query.OrganizationIds is { Count: 0 })
            return (Array.Empty<Tenant>(), 0);

        var tenants = _context.Tenants.AsQueryable();

        if (query.OrganizationIds != null)
            tenants = tenants.Where(t => query.OrganizationIds.Contains(t.OrganizationId));
        if (query.Enabled is { } enabled)
            tenants = tenants.Where(t => t.Enabled == enabled);
        if (!string.IsNullOrWhiteSpace(query.Search))
        {
            var term = query.Search.Trim().ToLower();
            tenants = tenants.Where(t =>
                t.Name.ToLower().Contains(term)
                || (t.DisplayName != null && t.DisplayName.ToLower().Contains(term))
                || t.Identifier.ToLower().Contains(term)
                || (t.Description != null && t.Description.ToLower().Contains(term)));
        }

        var total = await tenants.CountAsync(cancellationToken);
        var items = await tenants
            .OrderBy(t => t.Name)
            .ThenBy(t => t.Identifier)
            .Skip(Math.Max(0, query.Skip))
            .Take(Math.Max(1, query.Take))
            .ToListAsync(cancellationToken);
        return (items, total);
    }
}
