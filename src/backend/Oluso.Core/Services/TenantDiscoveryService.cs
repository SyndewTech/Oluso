using Oluso.Core.Domain.Entities;
using Oluso.Core.Domain.Interfaces;

namespace Oluso.Core.Services;

/// <summary>
/// A tenant found by public discovery, with the issuer a browser or client
/// must use to reach it.
/// </summary>
public sealed record DiscoveredTenant(Tenant Tenant, string IssuerUri);

/// <summary>
/// Resolves a tenant from the public signals an anonymous caller can supply:
/// its identifier, or a host (tenant subdomain or custom domain). Shared by the
/// <c>/api/tenant/resolve</c> endpoint and the platform's "Sign in with your
/// organisation" form so both answer the same way. Disabled and unknown tenants
/// both come back as null, so callers cannot tell them apart.
/// </summary>
public interface ITenantDiscoveryService
{
    /// <summary>False when multi-tenancy is not enabled (no tenant store registered).</summary>
    bool IsAvailable { get; }

    Task<DiscoveredTenant?> FindByIdentifierAsync(string identifier, CancellationToken cancellationToken = default);

    Task<DiscoveredTenant?> FindByHostAsync(string host, CancellationToken cancellationToken = default);
}

public sealed class TenantDiscoveryService : ITenantDiscoveryService
{
    private readonly ITenantStore? _tenantStore;
    private readonly IIssuerResolver _issuerResolver;

    public TenantDiscoveryService(IIssuerResolver issuerResolver, ITenantStore? tenantStore = null)
    {
        _issuerResolver = issuerResolver;
        _tenantStore = tenantStore;
    }

    public bool IsAvailable => _tenantStore != null;

    public async Task<DiscoveredTenant?> FindByIdentifierAsync(string identifier, CancellationToken cancellationToken = default)
    {
        if (_tenantStore == null || string.IsNullOrWhiteSpace(identifier)) return null;

        var tenant = await _tenantStore.GetByIdentifierAsync(identifier.Trim(), cancellationToken);
        return await ToDiscoveredAsync(tenant, cancellationToken);
    }

    public async Task<DiscoveredTenant?> FindByHostAsync(string host, CancellationToken cancellationToken = default)
    {
        if (_tenantStore == null || string.IsNullOrWhiteSpace(host)) return null;

        Tenant? tenant = null;
        if (!System.Net.IPAddress.TryParse(host, out _))
        {
            var parts = host.Split('.');
            if (parts.Length >= 3)
            {
                tenant = await _tenantStore.GetByIdentifierAsync(parts[0], cancellationToken);
            }
        }
        tenant ??= await _tenantStore.GetByCustomDomainAsync(host, cancellationToken);

        return await ToDiscoveredAsync(tenant, cancellationToken);
    }

    private async Task<DiscoveredTenant?> ToDiscoveredAsync(Tenant? tenant, CancellationToken cancellationToken)
    {
        if (tenant is null || !tenant.Enabled) return null;
        var issuer = await _issuerResolver.GetIssuerForTenantAsync(tenant, cancellationToken);
        return new DiscoveredTenant(tenant, issuer);
    }
}
