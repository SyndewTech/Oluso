using Microsoft.Extensions.Logging;
using Oluso.Core.Domain.Interfaces;

namespace Oluso.Core.Licensing;

/// <summary>
/// Feature availability provider that checks organization-level feature entitlements.
/// Priority 150: between platform license (200) and billing/subscription (100).
///
/// Design: If an org has NO features configured, all module features are denied.
/// A super admin must explicitly enable features for an org before its tenants can use them.
/// </summary>
public class OrganizationFeatureProvider : IFeatureAvailabilityProvider
{
    private readonly IOrganizationFeatureStore _featureStore;
    private readonly ITenantStore _tenantStore;
    private readonly ILogger<OrganizationFeatureProvider> _logger;

    public int Priority => 150;

    public OrganizationFeatureProvider(
        IOrganizationFeatureStore featureStore,
        ITenantStore tenantStore,
        ILogger<OrganizationFeatureProvider> logger)
    {
        _featureStore = featureStore;
        _tenantStore = tenantStore;
        _logger = logger;
    }

    public async Task<bool> IsFeatureAvailableAsync(
        string featureKey,
        string? tenantId,
        string? userId = null,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrEmpty(tenantId))
            return true; // No tenant context (e.g. super admin) = allow

        // Resolve tenant → organization
        var tenant = await _tenantStore.GetByIdAsync(tenantId, cancellationToken);
        if (tenant == null || string.IsNullOrEmpty(tenant.OrganizationId))
            return true; // No org association = allow

        // Check if org has any features configured
        var hasConfig = await _featureStore.HasAnyFeaturesConfiguredAsync(tenant.OrganizationId, cancellationToken);
        if (!hasConfig)
            return false; // No features configured = deny all until org features are set up

        // Org has features configured — check if this specific feature is enabled
        var isEnabled = await _featureStore.IsFeatureEnabledAsync(tenant.OrganizationId, featureKey, cancellationToken);

        if (!isEnabled)
        {
            _logger.LogDebug(
                "Feature '{Feature}' denied for tenant {TenantId} (org {OrgId})",
                featureKey, tenantId, tenant.OrganizationId);
        }

        return isEnabled;
    }

    public Task<IDictionary<string, object>> GetFeatureClaimsAsync(
        string? tenantId,
        string? userId = null,
        CancellationToken cancellationToken = default)
    {
        // No additional claims from org features
        return Task.FromResult<IDictionary<string, object>>(new Dictionary<string, object>());
    }
}
