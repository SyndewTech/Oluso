namespace Oluso.Core.Domain.Entities;

/// <summary>
/// Represents a user's membership in an organization.
/// Users can be members of multiple organizations.
/// </summary>
public class OrganizationMembership
{
    public string Id { get; set; } = default!;

    /// <summary>
    /// The organization this membership belongs to
    /// </summary>
    public string OrganizationId { get; set; } = default!;

    /// <summary>
    /// The user who is a member
    /// </summary>
    public string UserId { get; set; } = default!;

    /// <summary>
    /// Role within the organization
    /// </summary>
    public OrganizationRole Role { get; set; } = OrganizationRole.Member;

    /// <summary>
    /// Specific tenant IDs this member can access (JSON array).
    /// Null means all tenants in the organization.
    /// Only applicable for Member role.
    /// </summary>
    public string? AllowedTenantIdsJson { get; set; }

    /// <summary>
    /// When the user joined the organization
    /// </summary>
    public DateTime JoinedAt { get; set; } = DateTime.UtcNow;

    /// <summary>
    /// Who invited this user (null if owner/founder)
    /// </summary>
    public string? InvitedByUserId { get; set; }

    /// <summary>
    /// The invitation that was accepted (if applicable)
    /// </summary>
    public string? InvitationId { get; set; }

    // Navigation properties
    public Organization Organization { get; set; } = default!;

    // Note: Navigation to OlusoUser would create circular dependency
    // Use UserId to look up user details when needed
}
