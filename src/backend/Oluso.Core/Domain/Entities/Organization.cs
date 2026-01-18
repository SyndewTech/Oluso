namespace Oluso.Core.Domain.Entities;

/// <summary>
/// Represents an organization that can own multiple tenants.
/// Organizations are the billing and ownership unit, while tenants provide data isolation.
/// </summary>
public class Organization
{
    public string Id { get; set; } = default!;

    /// <summary>
    /// Display name of the organization
    /// </summary>
    public string Name { get; set; } = default!;

    /// <summary>
    /// Unique URL-safe identifier (slug) for the organization
    /// </summary>
    public string Slug { get; set; } = default!;

    /// <summary>
    /// Optional description of the organization
    /// </summary>
    public string? Description { get; set; }

    /// <summary>
    /// Whether the organization is enabled
    /// </summary>
    public bool Enabled { get; set; } = true;

    /// <summary>
    /// Organization logo URL
    /// </summary>
    public string? LogoUrl { get; set; }

    /// <summary>
    /// Organization website URL
    /// </summary>
    public string? WebsiteUrl { get; set; }

    // Billing and subscription

    /// <summary>
    /// Stripe or payment provider customer ID
    /// </summary>
    public string? BillingCustomerId { get; set; }

    /// <summary>
    /// Current subscription plan ID
    /// </summary>
    public string? PlanId { get; set; }

    /// <summary>
    /// When the current plan expires
    /// </summary>
    public DateTime? PlanExpiresAt { get; set; }

    // Limits

    /// <summary>
    /// Maximum number of members allowed (0 = unlimited)
    /// </summary>
    public int MaxMembers { get; set; } = 0;

    /// <summary>
    /// Maximum number of tenants/environments allowed
    /// </summary>
    public int MaxTenants { get; set; } = 5;

    // Metadata

    /// <summary>
    /// JSON metadata for custom organization data
    /// </summary>
    public string? Metadata { get; set; }

    public DateTime Created { get; set; } = DateTime.UtcNow;
    public DateTime? Updated { get; set; }

    // Navigation properties
    public ICollection<Tenant> Tenants { get; set; } = new List<Tenant>();
    public ICollection<OrganizationMembership> Memberships { get; set; } = new List<OrganizationMembership>();
    public ICollection<OrganizationInvitation> Invitations { get; set; } = new List<OrganizationInvitation>();
}

/// <summary>
/// Role within an organization
/// </summary>
public enum OrganizationRole
{
    /// <summary>
    /// Full control: billing, delete org, manage all settings
    /// </summary>
    Owner = 0,

    /// <summary>
    /// Manage members, tenants, and settings (no billing/delete)
    /// </summary>
    Admin = 1,

    /// <summary>
    /// Access allowed tenants only
    /// </summary>
    Member = 2
}

/// <summary>
/// Status of an organization invitation
/// </summary>
public enum InvitationStatus
{
    Pending = 0,
    Accepted = 1,
    Declined = 2,
    Expired = 3,
    Revoked = 4
}

/// <summary>
/// Environment type for a tenant (dev, staging, production, etc.)
/// </summary>
public enum TenantEnvironment
{
    /// <summary>
    /// Development environment for testing
    /// </summary>
    Development = 0,

    /// <summary>
    /// Quality assurance / testing environment
    /// </summary>
    QA = 1,

    /// <summary>
    /// Staging environment (pre-production)
    /// </summary>
    Staging = 2,

    /// <summary>
    /// User acceptance testing environment
    /// </summary>
    UAT = 3,

    /// <summary>
    /// Production environment
    /// </summary>
    Production = 4
}
