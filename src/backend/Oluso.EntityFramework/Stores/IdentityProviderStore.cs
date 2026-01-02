using Microsoft.EntityFrameworkCore;
using Oluso.Core.Domain.Entities;
using Oluso.Core.Domain.Interfaces;

namespace Oluso.EntityFramework.Stores;

/// <summary>
/// Entity Framework implementation of IIdentityProviderStore
/// </summary>
public class IdentityProviderStore : IIdentityProviderStore
{
    private readonly IOlusoDbContext _context;
    private readonly ITenantContext _tenantContext;

    public IdentityProviderStore(IOlusoDbContext context, ITenantContext tenantContext)
    {
        _context = context;
        _tenantContext = tenantContext;
    }

    public async Task<IdentityProvider?> GetByIdAsync(int id, CancellationToken cancellationToken = default)
    {
        return await GetProvidersQuery()
            .FirstOrDefaultAsync(p => p.Id == id, cancellationToken);
    }

    public async Task<IdentityProvider?> GetBySchemeAsync(string scheme, CancellationToken cancellationToken = default)
    {
        return await GetProvidersQuery()
            .FirstOrDefaultAsync(p => p.Scheme == scheme, cancellationToken);
    }

    public async Task<IEnumerable<IdentityProvider>> GetAllAsync(CancellationToken cancellationToken = default)
    {
        return await GetProvidersQuery().ToListAsync(cancellationToken);
    }

    public async Task<IEnumerable<IdentityProvider>> GetByProviderTypeAsync(
        ExternalProviderType providerType,
        CancellationToken cancellationToken = default)
    {
        return await GetProvidersQuery()
            .Where(p => p.ProviderType == providerType)
            .ToListAsync(cancellationToken);
    }

    public async Task<bool> SchemeExistsGloballyAsync(string scheme, CancellationToken cancellationToken = default)
    {
        return await _context.IdentityProviders
            .AnyAsync(p => p.Scheme == scheme, cancellationToken);
    }

    public async Task<IdentityProvider> AddAsync(
        IdentityProvider provider,
        CancellationToken cancellationToken = default)
    {
        // Always set TenantId from context to prevent bypass attacks
        provider.TenantId = _tenantContext.TenantId;
        provider.Created = DateTime.UtcNow;

        _context.IdentityProviders.Add(provider);
        await _context.SaveChangesAsync(cancellationToken);
        return provider;
    }

    public async Task<IdentityProvider> UpdateAsync(
        IdentityProvider provider,
        CancellationToken cancellationToken = default)
    {
        provider.Updated = DateTime.UtcNow;
        await _context.SaveChangesAsync(cancellationToken);
        return provider;
    }

    public async Task DeleteAsync(int id, CancellationToken cancellationToken = default)
    {
        var provider = await GetProvidersQuery()
            .FirstOrDefaultAsync(p => p.Id == id, cancellationToken);

        if (provider != null)
        {
            _context.IdentityProviders.Remove(provider);
            await _context.SaveChangesAsync(cancellationToken);
        }
    }

    private IQueryable<IdentityProvider> GetProvidersQuery()
    {
        return _context.IdentityProviders
            .Where(p => p.TenantId == _tenantContext.TenantId || p.TenantId == null);
    }
}
