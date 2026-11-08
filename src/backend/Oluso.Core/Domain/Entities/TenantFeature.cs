namespace Oluso.Core.Domain.Entities;

/// <summary>
/// Represents a feature entitlement for a tenant.
/// Controls which ERP modules and capabilities a tenant has access to,
/// within the bounds of what the parent organization allows.
/// </summary>
public class TenantFeature
{
    public string Id { get; set; } = Guid.NewGuid().ToString();
    public string TenantId { get; set; } = null!;
    public string FeatureKey { get; set; } = null!;
    public bool Enabled { get; set; } = true;
    public int? MaxSeats { get; set; }
    public DateTime GrantedAt { get; set; } = DateTime.UtcNow;
    public string? GrantedByUserId { get; set; }

    // Navigation
    public Tenant Tenant { get; set; } = null!;
}
