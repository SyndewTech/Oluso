namespace Oluso.Core.Services;

/// <summary>
/// Syncs subscription plan features to OrganizationFeature rows.
/// When a tenant's subscription changes, this service ensures the owning
/// organization's feature entitlements reflect the current plan.
/// Only manages features with Source = "subscription" — manual features are untouched.
/// </summary>
public interface IOrgFeatureSyncService
{
    /// <summary>
    /// Sync features for the organization that owns the given tenant,
    /// based on the tenant's current subscription plan.
    /// </summary>
    Task SyncSubscriptionFeaturesAsync(string tenantId, CancellationToken cancellationToken = default);

    /// <summary>
    /// Remove all subscription-sourced features for the organization that owns the given tenant.
    /// Called when a subscription is canceled immediately.
    /// </summary>
    Task RemoveSubscriptionFeaturesAsync(string tenantId, CancellationToken cancellationToken = default);
}
