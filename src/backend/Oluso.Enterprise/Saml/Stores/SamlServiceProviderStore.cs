using Microsoft.EntityFrameworkCore;
using Oluso.Core.Domain.Interfaces;
using Oluso.Enterprise.Saml.Entities;
using Oluso.Enterprise.Saml.EntityFramework;

namespace Oluso.Enterprise.Saml.Stores;

/// <summary>
/// Entity Framework implementation of ISamlServiceProviderStore using SamlDbContext.
/// Relies on EF Core global query filters for tenant isolation (configured via TenantEntity base class).
/// </summary>
public class SamlServiceProviderStore : ISamlServiceProviderStore
{
    private readonly SamlDbContext _context;
    private readonly ITenantContext _tenantContext;

    public SamlServiceProviderStore(SamlDbContext context, ITenantContext tenantContext)
    {
        _context = context;
        _tenantContext = tenantContext;
    }

    public async Task<SamlServiceProvider?> GetByEntityIdAsync(
        string entityId,
        CancellationToken cancellationToken = default)
    {
        // Query filter applied automatically via TenantEntity
        return await _context.SamlServiceProviders
            .FirstOrDefaultAsync(sp => sp.EntityId == entityId, cancellationToken);
    }

    public async Task<SamlServiceProvider?> GetByIdAsync(
        int id,
        CancellationToken cancellationToken = default)
    {
        // Query filter applied automatically via TenantEntity
        return await _context.SamlServiceProviders
            .FirstOrDefaultAsync(sp => sp.Id == id, cancellationToken);
    }

    public async Task<IReadOnlyList<SamlServiceProvider>> GetAllAsync(
        bool includeDisabled = false,
        CancellationToken cancellationToken = default)
    {
        // Query filter applied automatically via TenantEntity
        var query = _context.SamlServiceProviders.AsQueryable();

        if (!includeDisabled)
        {
            query = query.Where(sp => sp.Enabled);
        }

        return await query
            .OrderBy(sp => sp.DisplayName ?? sp.EntityId)
            .ToListAsync(cancellationToken);
    }

    public async Task<SamlServiceProvider> AddAsync(
        SamlServiceProvider entity,
        CancellationToken cancellationToken = default)
    {
        // Always set TenantId from context to prevent bypass attacks
        entity.TenantId = _tenantContext.TenantId;
        entity.Created = DateTime.UtcNow;

        _context.SamlServiceProviders.Add(entity);
        await _context.SaveChangesAsync(cancellationToken);

        return entity;
    }

    public async Task<SamlServiceProvider> UpdateAsync(
        SamlServiceProvider entity,
        CancellationToken cancellationToken = default)
    {
        entity.Updated = DateTime.UtcNow;
        await _context.SaveChangesAsync(cancellationToken);

        return entity;
    }

    public async Task<bool> DeleteAsync(
        int id,
        CancellationToken cancellationToken = default)
    {
        // Query filter applied automatically via TenantEntity
        var entity = await _context.SamlServiceProviders
            .FirstOrDefaultAsync(sp => sp.Id == id, cancellationToken);

        if (entity == null)
        {
            return false;
        }

        _context.SamlServiceProviders.Remove(entity);
        await _context.SaveChangesAsync(cancellationToken);

        return true;
    }

    public async Task<bool> ExistsAsync(
        string entityId,
        int? excludeId = null,
        CancellationToken cancellationToken = default)
    {
        // Query filter applied automatically via TenantEntity
        var query = _context.SamlServiceProviders
            .Where(sp => sp.EntityId == entityId);

        if (excludeId.HasValue)
        {
            query = query.Where(sp => sp.Id != excludeId.Value);
        }

        return await query.AnyAsync(cancellationToken);
    }

    public async Task UpdateLastAccessedAsync(
        string entityId,
        CancellationToken cancellationToken = default)
    {
        // Query filter applied automatically via TenantEntity
        var entity = await _context.SamlServiceProviders
            .FirstOrDefaultAsync(sp => sp.EntityId == entityId, cancellationToken);

        if (entity != null)
        {
            entity.LastAccessed = DateTime.UtcNow;
            await _context.SaveChangesAsync(cancellationToken);
        }
    }
}
