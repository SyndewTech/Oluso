using Microsoft.EntityFrameworkCore;
using Oluso.Core.Domain.Entities;
using Oluso.Core.Domain.Interfaces;

namespace Oluso.EntityFramework.Stores;

/// <summary>
/// Entity Framework implementation of IResourceStore with tenant isolation
/// </summary>
public class ResourceStore : IResourceStore
{
    private readonly IOlusoDbContext _context;
    private readonly ITenantContext _tenantContext;

    public ResourceStore(IOlusoDbContext context, ITenantContext tenantContext)
    {
        _context = context;
        _tenantContext = tenantContext;
    }

    // Identity Resources
    public async Task<IEnumerable<IdentityResource>> GetAllIdentityResourcesAsync(CancellationToken cancellationToken = default)
    {
        return await _context.IdentityResources
            .Include(r => r.UserClaims)
            .ToListAsync(cancellationToken);
    }

    public async Task<IEnumerable<IdentityResource>> FindIdentityResourcesByScopeNameAsync(
        IEnumerable<string> scopeNames,
        CancellationToken cancellationToken = default)
    {
        var names = scopeNames.ToList();
        return await _context.IdentityResources
            .Where(r => names.Contains(r.Name))
            .Include(r => r.UserClaims)
            .ToListAsync(cancellationToken);
    }

    public async Task<IdentityResource?> GetIdentityResourceByIdAsync(int id, CancellationToken cancellationToken = default)
    {
        return await _context.IdentityResources
            .Include(r => r.UserClaims)
            .Include(r => r.Properties)
            .FirstOrDefaultAsync(r => r.Id == id, cancellationToken);
    }

    public async Task<IdentityResource> AddIdentityResourceAsync(IdentityResource resource, CancellationToken cancellationToken = default)
    {
        // Always set TenantId from context to prevent bypass attacks
        resource.TenantId = _tenantContext.TenantId;
        _context.IdentityResources.Add(resource);
        await _context.SaveChangesAsync(cancellationToken);
        return resource;
    }

    public async Task<IdentityResource> UpdateIdentityResourceAsync(IdentityResource resource, CancellationToken cancellationToken = default)
    {
        resource.Updated = DateTime.UtcNow;

        // Ensure the entity and its collections are properly tracked
        var entry = ((DbContext)_context).Entry(resource);
        if (entry.State == EntityState.Detached)
        {
            _context.IdentityResources.Update(resource);
        }

        await _context.SaveChangesAsync(cancellationToken);
        return resource;
    }

    public async Task DeleteIdentityResourceAsync(int id, CancellationToken cancellationToken = default)
    {
        var resource = await _context.IdentityResources.FindAsync(new object[] { id }, cancellationToken);
        if (resource != null)
        {
            _context.IdentityResources.Remove(resource);
            await _context.SaveChangesAsync(cancellationToken);
        }
    }

    // Resources (RFC 8707)
    public async Task<IEnumerable<Resource>> GetAllResourcesAsync(CancellationToken cancellationToken = default)
    {
        return await _context.Resources
            .Include(r => r.AllowedScopes)
            .Include(r => r.UserClaims)
            .ToListAsync(cancellationToken);
    }

    public async Task<Resource?> GetResourceByIdAsync(int id, CancellationToken cancellationToken = default)
    {
        return await _context.Resources
            .Include(r => r.AllowedScopes)
            .Include(r => r.UserClaims)
            .FirstOrDefaultAsync(r => r.Id == id, cancellationToken);
    }

    public async Task<Resource?> FindResourceByUriAsync(string uri, CancellationToken cancellationToken = default)
    {
        return await _context.Resources
            .Include(r => r.AllowedScopes)
            .Include(r => r.UserClaims)
            .FirstOrDefaultAsync(r => r.Uri == uri, cancellationToken);
    }

    public async Task<IEnumerable<Resource>> FindResourcesByUrisAsync(
        IEnumerable<string> uris,
        CancellationToken cancellationToken = default)
    {
        var uriList = uris.ToList();
        return await _context.Resources
            .Where(r => uriList.Contains(r.Uri))
            .Include(r => r.AllowedScopes)
            .Include(r => r.UserClaims)
            .ToListAsync(cancellationToken);
    }

    public async Task<Resource> AddResourceAsync(Resource resource, CancellationToken cancellationToken = default)
    {
        // Always set TenantId from context to prevent bypass attacks
        resource.TenantId = _tenantContext.TenantId;
        _context.Resources.Add(resource);
        await _context.SaveChangesAsync(cancellationToken);
        return resource;
    }

    public async Task<Resource> UpdateResourceAsync(Resource resource, CancellationToken cancellationToken = default)
    {
        resource.Updated = DateTime.UtcNow;

        // Ensure the entity and its collections are properly tracked
        var entry = ((DbContext)_context).Entry(resource);
        if (entry.State == EntityState.Detached)
        {
            _context.Resources.Update(resource);
        }

        await _context.SaveChangesAsync(cancellationToken);
        return resource;
    }

    public async Task DeleteResourceAsync(int id, CancellationToken cancellationToken = default)
    {
        var resource = await _context.Resources.FindAsync(new object[] { id }, cancellationToken);
        if (resource != null)
        {
            _context.Resources.Remove(resource);
            await _context.SaveChangesAsync(cancellationToken);
        }
    }

    // API Scopes
    public async Task<IEnumerable<ApiScope>> GetAllApiScopesAsync(CancellationToken cancellationToken = default)
    {
        return await _context.ApiScopes
            .Include(s => s.UserClaims)
            .ToListAsync(cancellationToken);
    }

    public async Task<IEnumerable<ApiScope>> FindApiScopesByNameAsync(
        IEnumerable<string> scopeNames,
        CancellationToken cancellationToken = default)
    {
        var names = scopeNames.ToList();
        return await _context.ApiScopes
            .Where(s => names.Contains(s.Name))
            .Include(s => s.UserClaims)
            .ToListAsync(cancellationToken);
    }

    public async Task<ApiScope?> GetApiScopeByIdAsync(int id, CancellationToken cancellationToken = default)
    {
        return await _context.ApiScopes
            .Include(s => s.UserClaims)
            .Include(s => s.Properties)
            .FirstOrDefaultAsync(s => s.Id == id, cancellationToken);
    }

    public async Task<ApiScope> AddApiScopeAsync(ApiScope scope, CancellationToken cancellationToken = default)
    {
        // Always set TenantId from context to prevent bypass attacks
        scope.TenantId = _tenantContext.TenantId;
        _context.ApiScopes.Add(scope);
        await _context.SaveChangesAsync(cancellationToken);
        return scope;
    }

    public async Task<ApiScope> UpdateApiScopeAsync(ApiScope scope, CancellationToken cancellationToken = default)
    {
        scope.Updated = DateTime.UtcNow;

        // Ensure the entity and its collections are properly tracked
        var entry = ((DbContext)_context).Entry(scope);
        if (entry.State == EntityState.Detached)
        {
            _context.ApiScopes.Update(scope);
        }

        await _context.SaveChangesAsync(cancellationToken);
        return scope;
    }

    public async Task DeleteApiScopeAsync(int id, CancellationToken cancellationToken = default)
    {
        var scope = await _context.ApiScopes.FindAsync(new object[] { id }, cancellationToken);
        if (scope != null)
        {
            _context.ApiScopes.Remove(scope);
            await _context.SaveChangesAsync(cancellationToken);
        }
    }
}
