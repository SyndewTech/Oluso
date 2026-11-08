namespace Oluso.Core.Domain.Entities;

/// <summary>
/// Tracks per-user access (seat) for a module within a tenant.
/// Used for per-seat billing and filtering module-scoped user searches.
/// </summary>
public class UserModuleAccess
{
    public string Id { get; set; } = Guid.NewGuid().ToString();
    public string TenantId { get; set; } = null!;
    public string UserId { get; set; } = null!;
    public string FeatureKey { get; set; } = null!;
    public DateTime GrantedAt { get; set; } = DateTime.UtcNow;
    public string? GrantedByUserId { get; set; }

    // Navigation
    public Tenant Tenant { get; set; } = null!;
}
