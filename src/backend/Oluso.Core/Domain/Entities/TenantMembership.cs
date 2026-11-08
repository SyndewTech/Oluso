namespace Oluso.Core.Domain.Entities;

/// <summary>
/// Represents a user's access to a tenant.
/// This is the single source of truth for tenant access control.
/// Every user who can access a tenant must have a TenantMembership row,
/// including for their home tenant (OlusoUser.TenantId).
/// </summary>
public class TenantMembership
{
    public string Id { get; set; } = Guid.NewGuid().ToString();
    public string TenantId { get; set; } = default!;
    public string UserId { get; set; } = default!;
    public DateTime JoinedAt { get; set; } = DateTime.UtcNow;

    /// <summary>
    /// Who granted this access (user ID or "system" for auto-created)
    /// </summary>
    public string? InvitedByUserId { get; set; }

    // Navigation properties
    public Tenant Tenant { get; set; } = default!;
}
