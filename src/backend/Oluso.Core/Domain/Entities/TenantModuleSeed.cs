namespace Oluso.Core.Domain.Entities;

/// <summary>
/// Tracks which modules have been seeded for a tenant.
/// When a feature is disabled at tenant creation, seeding is deferred.
/// When the feature is later enabled, this table is checked to run any missing seeds.
/// </summary>
public class TenantModuleSeed
{
    public string Id { get; set; } = Guid.NewGuid().ToString();
    public string TenantId { get; set; } = null!;
    public string FeatureKey { get; set; } = null!;
    public DateTime SeededAt { get; set; } = DateTime.UtcNow;

    // Navigation
    public Tenant Tenant { get; set; } = null!;
}
