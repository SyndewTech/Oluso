using Microsoft.Extensions.Logging;
using Oluso.Core.Domain.Interfaces;

namespace Oluso.Core.Licensing;

/// <summary>
/// Feature availability provider that checks per-user module seat access.
/// Priority 110: between tenant features (125) and billing/subscription (100).
///
/// Design: Only enforces per-user checks when the tenant has seat-based access configured
/// (MaxSeats is set on the TenantFeature). When MaxSeats is null, all tenant users can access
/// the module (backwards compatible with simple feature toggles).
///
/// When seat-based: user must have a UserModuleAccess row for the feature.
/// </summary>
public class UserModuleAccessFeatureProvider : IFeatureAvailabilityProvider
{
    private readonly IUserModuleAccessStore _moduleAccessStore;
    private readonly ITenantFeatureStore _tenantFeatureStore;
    private readonly ILogger<UserModuleAccessFeatureProvider> _logger;

    public int Priority => 110;

    public UserModuleAccessFeatureProvider(
        IUserModuleAccessStore moduleAccessStore,
        ITenantFeatureStore tenantFeatureStore,
        ILogger<UserModuleAccessFeatureProvider> logger)
    {
        _moduleAccessStore = moduleAccessStore;
        _tenantFeatureStore = tenantFeatureStore;
        _logger = logger;
    }

    public async Task<bool> IsFeatureAvailableAsync(
        string featureKey,
        string? tenantId,
        string? userId = null,
        CancellationToken cancellationToken = default)
    {
        // No tenant or user context = allow (e.g., super admin, background jobs)
        if (string.IsNullOrEmpty(tenantId) || string.IsNullOrEmpty(userId))
            return true;

        // Check if tenant uses seat-based access for this feature
        var features = await _tenantFeatureStore.GetFeaturesAsync(tenantId, cancellationToken);
        var feature = features.FirstOrDefault(f =>
            f.FeatureKey.Equals(featureKey, StringComparison.OrdinalIgnoreCase));

        // Feature not configured or MaxSeats is null = no per-user restriction
        if (feature == null || feature.MaxSeats == null)
            return true;

        // Seat-based: check if user has a seat
        var hasAccess = await _moduleAccessStore.HasAccessAsync(tenantId, userId, featureKey, cancellationToken);

        if (!hasAccess)
        {
            _logger.LogDebug(
                "Feature '{Feature}' denied for user {UserId} in tenant {TenantId} — no module seat assigned",
                featureKey, userId, tenantId);
        }

        return hasAccess;
    }

    public Task<IDictionary<string, object>> GetFeatureClaimsAsync(
        string? tenantId,
        string? userId = null,
        CancellationToken cancellationToken = default)
    {
        return Task.FromResult<IDictionary<string, object>>(new Dictionary<string, object>());
    }
}
