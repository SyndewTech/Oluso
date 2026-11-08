using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Oluso.Core;
using Oluso.Core.Domain.Entities;
using Oluso.Core.Domain.Interfaces;

namespace Oluso.EntityFramework.Stores;

/// <summary>
/// Entity Framework implementation of IClientStore with tenant isolation
/// and platform client fallback support.
/// </summary>
public class ClientStore : IClientStore
{
    private readonly IOlusoDbContext _context;
    private readonly ITenantContext _tenantContext;
    private readonly ILogger<ClientStore>? _logger;

    public ClientStore(IOlusoDbContext context, ITenantContext tenantContext, ILogger<ClientStore>? logger = null)
    {
        _context = context;
        _tenantContext = tenantContext;
        _logger = logger;
    }

    public async Task<Client?> FindClientByIdAsync(string clientId, CancellationToken cancellationToken = default)
    {
        _logger?.LogInformation(
            "FindClientByIdAsync(clientId='{ClientId}'): HasTenant={HasTenant}, TenantId='{TenantId}'",
            clientId, _tenantContext.HasTenant, _tenantContext.TenantId ?? "(null)");

        // Tenant must already be resolved by middleware before client lookup
        var client = await _context.Clients
            .Include(c => c.ClientSecrets)
            .Include(c => c.AllowedGrantTypes)
            .Include(c => c.RedirectUris)
            .Include(c => c.PostLogoutRedirectUris)
            .Include(c => c.AllowedScopes)
            .Include(c => c.Claims)
            .Include(c => c.AllowedAuthorizeParameters)
            .Include(c => c.AllowedCorsOrigins)
            .Include(c => c.Properties)
            .Include(c => c.IdentityProviderRestrictions)
            .Include(c => c.AllowedRoles)
            .Include(c => c.AllowedUsers)
            .FirstOrDefaultAsync(c => c.ClientId == clientId, cancellationToken);

        _logger?.LogInformation(
            "FindClientByIdAsync first-query result for '{ClientId}': {Outcome} (TenantId on row: '{RowTenantId}')",
            clientId,
            client == null ? "NOT FOUND" : "FOUND",
            client?.TenantId ?? "(n/a)");

        if (client is not null || _tenantContext.TenantId == PlatformConstants.TenantId)
        {
            _logger?.LogInformation(
                "FindClientByIdAsync returning early for '{ClientId}' (client {NullOrNot}, ctx is platform? {IsPlatform})",
                clientId,
                client == null ? "is null" : "found",
                _tenantContext.TenantId == PlatformConstants.TenantId);
            return client;
        }

        // Platform clients (admin-portal, workspace-portal, account-portal) are
        // shared UIs the platform ships for every tenant. They live under the
        // platform tenant, so the global filter hides them on tenant routes
        // and the OIDC validator reports "Unknown client". Bypass the filter
        // and re-impose TenantId == platform so this branch can ONLY surface
        // platform clients — no cross-tenant leak from another regular tenant.
        return await _context.Clients
            .IgnoreQueryFilters()
            .Include(c => c.ClientSecrets)
            .Include(c => c.AllowedGrantTypes)
            .Include(c => c.RedirectUris)
            .Include(c => c.PostLogoutRedirectUris)
            .Include(c => c.AllowedScopes)
            .Include(c => c.Claims)
            .Include(c => c.AllowedAuthorizeParameters)
            .Include(c => c.AllowedCorsOrigins)
            .Include(c => c.Properties)
            .Include(c => c.IdentityProviderRestrictions)
            .Include(c => c.AllowedRoles)
            .Include(c => c.AllowedUsers)
            .FirstOrDefaultAsync(
                c => c.ClientId == clientId && c.TenantId == PlatformConstants.TenantId,
                cancellationToken);
    }

    public async Task<bool?> IsClientEnabledAsync(string clientId, CancellationToken cancellationToken = default)
    {
        var enabled = await _context.Clients
            .AsNoTracking()
            .Where(c => c.ClientId == clientId)
            .Select(c => (bool?)c.Enabled)
            .FirstOrDefaultAsync(cancellationToken);

        if (enabled is not null || _tenantContext.TenantId == PlatformConstants.TenantId)
            return enabled;

        // Platform-shared clients (admin-portal, etc.) live under the platform
        // tenant. Mirror FindClientByIdAsync's explicit platform fallback —
        // IgnoreQueryFilters narrowly constrained to TenantId == platform so
        // we can't surface another tenant's private client.
        return await _context.Clients
            .IgnoreQueryFilters()
            .AsNoTracking()
            .Where(c => c.ClientId == clientId && c.TenantId == PlatformConstants.TenantId)
            .Select(c => (bool?)c.Enabled)
            .FirstOrDefaultAsync(cancellationToken);
    }

    public async Task<Client?> GetByIdAsync(int id, CancellationToken cancellationToken = default)
    {
        var client = await _context.Clients
            .Include(c => c.ClientSecrets)
            .Include(c => c.AllowedGrantTypes)
            .Include(c => c.RedirectUris)
            .Include(c => c.PostLogoutRedirectUris)
            .Include(c => c.AllowedScopes)
            .Include(c => c.Claims)
            .Include(c => c.AllowedAuthorizeParameters)
            .Include(c => c.AllowedCorsOrigins)
            .Include(c => c.Properties)
            .Include(c => c.IdentityProviderRestrictions)
            .Include(c => c.AllowedRoles)
            .Include(c => c.AllowedUsers)
            .FirstOrDefaultAsync(c => c.Id == id, cancellationToken);

        // Guard: Don't return platform clients to tenant admin
        if (client?.TenantId == PlatformConstants.TenantId &&
            _tenantContext.TenantId != PlatformConstants.TenantId)
        {
            return null;
        }

        return client;
    }

    public async Task<IEnumerable<Client>> GetAllClientsAsync(CancellationToken cancellationToken = default)
    {
        // Regular clients only — platform clients have their own dedicated endpoint.
        // The explicit Where clause ensures platform clients are excluded even when
        // the current tenant context is _platform (e.g. superadmin sessions).
        return await _context.Clients
            .Where(c => c.TenantId != PlatformConstants.TenantId)
            .Include(c => c.AllowedGrantTypes)
            .Include(c => c.AllowedScopes)
            .ToListAsync(cancellationToken);
    }

    public Task<bool> IsRegisteredRedirectUriAsync(string redirectUri, CancellationToken cancellationToken = default) =>
        // Exact match on the indexed RedirectUri column; the tenant filters on both tables scope it to the tenant.
        _context.ClientRedirectUris
            .AnyAsync(r => r.RedirectUri == redirectUri && r.Client.Enabled, cancellationToken);

    public async Task<bool> RecordAccessAsync(string clientId, DateTime now, TimeSpan interval, CancellationToken cancellationToken = default)
    {
        // Matches only when the stored time is stale, so the usual case is one small read; no
        // related collections are loaded.
        var staleBefore = now - interval;
        var client = await _context.Clients
            .FirstOrDefaultAsync(c => c.ClientId == clientId && (c.LastAccessed == null || c.LastAccessed < staleBefore), cancellationToken);

        // Same rule as FindClientByIdAsync: a platform client used on a tenant host is found
        // under the platform tenant, and only there.
        if (client == null && _tenantContext.TenantId != PlatformConstants.TenantId)
        {
            client = await _context.Clients
                .IgnoreQueryFilters()
                .FirstOrDefaultAsync(c => c.ClientId == clientId && c.TenantId == PlatformConstants.TenantId
                    && (c.LastAccessed == null || c.LastAccessed < staleBefore), cancellationToken);
        }
        if (client == null) return false;

        client.LastAccessed = now;
        await _context.SaveChangesAsync(cancellationToken);
        return true;
    }

    public async Task<Client> AddClientAsync(Client client, CancellationToken cancellationToken = default)
    {
        // Always set TenantId from context to prevent bypass attacks
        client.TenantId = _tenantContext.TenantId;

        // Propagate TenantId to all child entities
        PropagateChildTenantIds(client);

        _context.Clients.Add(client);
        await _context.SaveChangesAsync(cancellationToken);
        return client;
    }

    public async Task<Client> UpdateClientAsync(Client client, CancellationToken cancellationToken = default)
    {
        // Guard: Don't allow updating platform clients from non-platform context
        if (client.TenantId == PlatformConstants.TenantId &&
            _tenantContext.TenantId != PlatformConstants.TenantId)
        {
            throw new InvalidOperationException("Cannot update platform client from tenant context");
        }

        client.Updated = DateTime.UtcNow;
        await _context.SaveChangesAsync(cancellationToken);
        return client;
    }

    public async Task DeleteByIdAsync(int id, CancellationToken cancellationToken = default)
    {
        var client = await _context.Clients.FirstOrDefaultAsync(c => c.Id == id, cancellationToken);
        if (client != null)
        {
            // Guard: Don't allow deleting platform clients from non-platform context
            if (client.TenantId == PlatformConstants.TenantId &&
                _tenantContext.TenantId != PlatformConstants.TenantId)
            {
                throw new InvalidOperationException("Cannot delete platform client from tenant context");
            }

            _context.Clients.Remove(client);
            await _context.SaveChangesAsync(cancellationToken);
        }
    }

    #region Platform Client Methods

    public async Task<IEnumerable<Client>> GetPlatformClientsAsync(CancellationToken cancellationToken = default)
    {
        return await _context.Clients
            .IgnoreQueryFilters()
            .Where(c => c.TenantId == PlatformConstants.TenantId)
            .Include(c => c.AllowedGrantTypes)
            .Include(c => c.AllowedScopes)
            .ToListAsync(cancellationToken);
    }

    public async Task<Client?> GetPlatformClientByIdAsync(int id, CancellationToken cancellationToken = default)
    {
        return await _context.Clients
            .IgnoreQueryFilters()
            .Where(c => c.TenantId == PlatformConstants.TenantId)
            .Include(c => c.ClientSecrets)
            .Include(c => c.AllowedGrantTypes)
            .Include(c => c.RedirectUris)
            .Include(c => c.PostLogoutRedirectUris)
            .Include(c => c.AllowedScopes)
            .Include(c => c.Claims)
            .Include(c => c.AllowedAuthorizeParameters)
            .Include(c => c.AllowedCorsOrigins)
            .Include(c => c.Properties)
            .Include(c => c.IdentityProviderRestrictions)
            .Include(c => c.AllowedRoles)
            .Include(c => c.AllowedUsers)
            .FirstOrDefaultAsync(c => c.Id == id, cancellationToken);
    }

    public async Task<Client> AddPlatformClientAsync(Client client, CancellationToken cancellationToken = default)
    {
        client.TenantId = PlatformConstants.TenantId; // Force platform tenant
        client.NonEditable = true; // Platform clients are non-editable by tenants

        // Propagate TenantId to all child entities
        PropagateChildTenantIds(client);

        _context.Clients.Add(client);
        await _context.SaveChangesAsync(cancellationToken);
        return client;
    }

    public async Task<Client> UpdatePlatformClientAsync(Client client, CancellationToken cancellationToken = default)
    {
        if (client.TenantId != PlatformConstants.TenantId)
            throw new InvalidOperationException("Not a platform client");

        client.Updated = DateTime.UtcNow;
        await _context.SaveChangesAsync(cancellationToken);
        return client;
    }

    public async Task DeletePlatformClientByIdAsync(int id, CancellationToken cancellationToken = default)
    {
        var client = await _context.Clients
            .IgnoreQueryFilters()
            .FirstOrDefaultAsync(c => c.Id == id && c.TenantId == PlatformConstants.TenantId, cancellationToken);

        if (client != null)
        {
            _context.Clients.Remove(client);
            await _context.SaveChangesAsync(cancellationToken);
        }
    }

    #endregion

    #region Private Helpers

    /// <summary>
    /// Propagate TenantId from the parent client to all child entities.
    /// This ensures child entities have consistent TenantId for global query filters.
    /// </summary>
    private static void PropagateChildTenantIds(Client client)
    {
        var tenantId = client.TenantId;

        foreach (var secret in client.ClientSecrets ?? [])
            secret.TenantId = tenantId;
        foreach (var grantType in client.AllowedGrantTypes ?? [])
            grantType.TenantId = tenantId;
        foreach (var redirectUri in client.RedirectUris ?? [])
            redirectUri.TenantId = tenantId;
        foreach (var postLogoutUri in client.PostLogoutRedirectUris ?? [])
            postLogoutUri.TenantId = tenantId;
        foreach (var scope in client.AllowedScopes ?? [])
            scope.TenantId = tenantId;
        foreach (var claim in client.Claims ?? [])
            claim.TenantId = tenantId;
        foreach (var authParam in client.AllowedAuthorizeParameters ?? [])
            authParam.TenantId = tenantId;
        foreach (var corsOrigin in client.AllowedCorsOrigins ?? [])
            corsOrigin.TenantId = tenantId;
        foreach (var property in client.Properties ?? [])
            property.TenantId = tenantId;
        foreach (var idpRestriction in client.IdentityProviderRestrictions ?? [])
            idpRestriction.TenantId = tenantId;
        foreach (var role in client.AllowedRoles ?? [])
            role.TenantId = tenantId;
        foreach (var user in client.AllowedUsers ?? [])
            user.TenantId = tenantId;
    }

    #endregion
}
