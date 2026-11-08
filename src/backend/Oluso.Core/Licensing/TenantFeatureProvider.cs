using Microsoft.Extensions.Logging;
using Oluso.Core.Domain.Interfaces;

namespace Oluso.Core.Licensing;

/// <summary>
/// Feature availability provider that checks tenant-level feature entitlements.
/// Priority 125: between organization features (150) and billing/subscription (100).
///
/// Design: A feature is allowed only when the tenant has an explicit TenantFeature
/// row with Enabled=true. Missing rows deny — there is no inheritance from the org.
/// The org-level provider (priority 150) caps what the tenant can enable.
/// </summary>
public class TenantFeatureProvider : IFeatureAvailabilityProvider
{
    private readonly ITenantFeatureStore _featureStore;
    private readonly ILogger<TenantFeatureProvider> _logger;

    public int Priority => 125;

    public TenantFeatureProvider(
        ITenantFeatureStore featureStore,
        ILogger<TenantFeatureProvider> logger)
    {
        _featureStore = featureStore;
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

        var isEnabled = await _featureStore.IsFeatureEnabledAsync(tenantId, featureKey, cancellationToken);

        if (!isEnabled)
        {
            _logger.LogDebug(
                "Feature '{Feature}' denied for tenant {TenantId} by tenant-level feature gate",
                featureKey, tenantId);
        }

        return isEnabled;
    }

    public Task<IDictionary<string, object>> GetFeatureClaimsAsync(
        string? tenantId,
        string? userId = null,
        CancellationToken cancellationToken = default)
    {
        // No additional claims from tenant features
        return Task.FromResult<IDictionary<string, object>>(new Dictionary<string, object>());
    }
}
