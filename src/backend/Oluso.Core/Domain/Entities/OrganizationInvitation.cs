namespace Oluso.Core.Domain.Entities;

/// <summary>
/// Represents an invitation to join an organization
/// </summary>
public class OrganizationInvitation
{
    public string Id { get; set; } = default!;

    /// <summary>
    /// The organization being invited to
    /// </summary>
    public string OrganizationId { get; set; } = default!;

    /// <summary>
    /// Email address of the invitee
    /// </summary>
    public string Email { get; set; } = default!;

    /// <summary>
    /// Role the invitee will have upon accepting
    /// </summary>
    public OrganizationRole Role { get; set; } = OrganizationRole.Member;

    /// <summary>
    /// Specific tenant IDs the invitee will have access to (JSON array).
    /// Null means all tenants.
    /// </summary>
    public string? AllowedTenantIdsJson { get; set; }

    /// <summary>
    /// Current status of the invitation
    /// </summary>
    public InvitationStatus Status { get; set; } = InvitationStatus.Pending;

    /// <summary>
    /// Secure token for the invitation link
    /// </summary>
    public string Token { get; set; } = default!;

    /// <summary>
    /// When the invitation expires
    /// </summary>
    public DateTime ExpiresAt { get; set; }

    /// <summary>
    /// Who sent the invitation
    /// </summary>
    public string InvitedByUserId { get; set; } = default!;

    /// <summary>
    /// When the invitation was created
    /// </summary>
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

    /// <summary>
    /// When the invitation was accepted (if applicable)
    /// </summary>
    public DateTime? AcceptedAt { get; set; }

    /// <summary>
    /// User ID of who accepted (may differ from email if user already existed)
    /// </summary>
    public string? AcceptedByUserId { get; set; }

    /// <summary>
    /// Optional personal message to include in the invitation
    /// </summary>
    public string? Message { get; set; }

    // Navigation properties
    public Organization Organization { get; set; } = default!;
}
