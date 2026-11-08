namespace Oluso.Core.Domain.Entities;

/// <summary>
/// Represents a feature entitlement for an organization.
/// Controls which ERP modules and capabilities an organization has access to.
/// </summary>
public class OrganizationFeature
{
    public string Id { get; set; } = Guid.NewGuid().ToString();
    public string OrganizationId { get; set; } = null!;
    public string FeatureKey { get; set; } = null!;
    public bool Enabled { get; set; } = true;
    public int? MaxSeats { get; set; }
    public DateTime GrantedAt { get; set; } = DateTime.UtcNow;
    public string? GrantedByUserId { get; set; }
    public DateTime? ExpiresAt { get; set; }

    /// <summary>
    /// How this feature was granted: "manual" (admin toggle) or "subscription" (billing sync).
    /// Subscription-sourced features are managed by OrgFeatureSyncService and should not be toggled manually.
    /// </summary>
    public string Source { get; set; } = "manual";

    /// <summary>
    /// Links back to the PlatformSubscription that granted this feature.
    /// Null for manually-granted features.
    /// </summary>
    public string? SourceSubscriptionId { get; set; }

    // Navigation
    public Organization Organization { get; set; } = null!;
}
