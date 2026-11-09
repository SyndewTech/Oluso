using Oluso.Core.Domain.Entities;

namespace Oluso.Admin.Dto.Organizations;

public class OrganizationDto
{
    public string Id { get; set; } = null!;
    public string Name { get; set; } = null!;
    public string Slug { get; set; } = null!;
    public string? Description { get; set; }
    public bool Enabled { get; set; }
    public string? LogoUrl { get; set; }
    public string? WebsiteUrl { get; set; }
    public string? BillingCustomerId { get; set; }
    public string? PlanId { get; set; }
    public DateTime? PlanExpiresAt { get; set; }
    public int MaxMembers { get; set; }
    public int MaxTenants { get; set; }
    public int TenantCount { get; set; }
    public DateTime Created { get; set; }
    public DateTime? Updated { get; set; }
}

public class CreateOrganizationRequest
{
    public string Name { get; set; } = null!;
    public string Slug { get; set; } = null!;
    public string? Description { get; set; }
    public string? LogoUrl { get; set; }
    public string? WebsiteUrl { get; set; }
    public int? MaxMembers { get; set; }
    public int? MaxTenants { get; set; }
    public string? OwnerUserId { get; set; }
}

public class UpdateOrganizationRequest
{
    public string? Name { get; set; }
    public string? Slug { get; set; }
    public string? Description { get; set; }
    public string? LogoUrl { get; set; }
    public string? WebsiteUrl { get; set; }
    public bool? Enabled { get; set; }
    public int? MaxMembers { get; set; }
    public int? MaxTenants { get; set; }
    public string? Metadata { get; set; }
}

public class OrganizationMemberDto
{
    public string Id { get; set; } = null!;
    public string OrganizationId { get; set; } = null!;
    public string UserId { get; set; } = null!;
    public string? UserName { get; set; }
    public string? Email { get; set; }
    public string? DisplayName { get; set; }
    public OrganizationRole Role { get; set; }
    public List<string>? AllowedTenantIds { get; set; }
    public DateTime JoinedAt { get; set; }
    public string? InvitedByUserId { get; set; }
    public string? InvitationId { get; set; }
}

public class AddOrganizationMemberRequest
{
    public string UserId { get; set; } = null!;
    public OrganizationRole? Role { get; set; }
    public List<string>? AllowedTenantIds { get; set; }
}

public class UpdateOrganizationMemberRequest
{
    public OrganizationRole? Role { get; set; }
    public List<string>? AllowedTenantIds { get; set; }
}

public class OrganizationInvitationDto
{
    public string Id { get; set; } = null!;
    public string OrganizationId { get; set; } = null!;
    public string? OrganizationName { get; set; }
    public string Email { get; set; } = null!;
    public OrganizationRole Role { get; set; }
    public InvitationStatus Status { get; set; }
    public List<string>? AllowedTenantIds { get; set; }
    public string InvitedByUserId { get; set; } = null!;
    public DateTime CreatedAt { get; set; }
    public DateTime ExpiresAt { get; set; }
    public DateTime? AcceptedAt { get; set; }
}

/// <summary>
/// A user found by the organization-scoped picker, with the tenant they belong
/// to so two people with similar names can be told apart.
/// </summary>
public class OrganizationUserDto
{
    public string Id { get; set; } = null!;
    public string? UserName { get; set; }
    public string? Email { get; set; }
    public string? DisplayName { get; set; }
    public string? TenantId { get; set; }
    public string? TenantName { get; set; }
}

public class CreateOrganizationInvitationRequest
{
    public string Email { get; set; } = null!;
    public OrganizationRole? Role { get; set; }
    public List<string>? AllowedTenantIds { get; set; }
    public int? ExpirationDays { get; set; }

    /// <summary>Optional note from the inviter, included in the invitation email.</summary>
    public string? Message { get; set; }
}

public class OrganizationTenantDto
{
    public string Id { get; set; } = null!;
    public string Name { get; set; } = null!;
    public string? DisplayName { get; set; }
    public string Identifier { get; set; } = null!;
    public string? Description { get; set; }
    public TenantEnvironment Environment { get; set; }
    public bool Enabled { get; set; }
    public DateTime Created { get; set; }
    public DateTime? Updated { get; set; }
}

public class CreateOrganizationTenantRequest
{
    public string Name { get; set; } = null!;
    public string? DisplayName { get; set; }
    public string Identifier { get; set; } = null!;
    public string? Description { get; set; }
    public TenantEnvironment? Environment { get; set; }
}

public class OrganizationFeaturesResponse
{
    public string OrganizationId { get; set; } = null!;
    public bool HasFeaturesConfigured { get; set; }
    public List<OrganizationFeatureDto> Features { get; set; } = new();
}

public class OrganizationFeatureDto
{
    public string Key { get; set; } = null!;
    public string DisplayName { get; set; } = null!;
    public string Description { get; set; } = null!;
    public bool Enabled { get; set; }
    public DateTime? GrantedAt { get; set; }
    public DateTime? ExpiresAt { get; set; }
    public int? MaxSeats { get; set; }
    public string Source { get; set; } = "manual";
    public string? SourceSubscriptionId { get; set; }
}

public class SetOrganizationFeaturesRequest
{
    public List<FeatureUpdateDto> Features { get; set; } = new();
}

public class FeatureUpdateDto
{
    public string FeatureKey { get; set; } = null!;
    public bool Enabled { get; set; }
    public DateTime? ExpiresAt { get; set; }
    public int? MaxSeats { get; set; }
}

public class EnableFeatureRequest
{
    public DateTime? ExpiresAt { get; set; }
}

public class ProvisionOrganizationRequest
{
    public string Name { get; set; } = null!;
    public string Slug { get; set; } = null!;
    public string? Description { get; set; }
    public string? LogoUrl { get; set; }
    public string? WebsiteUrl { get; set; }
    public int? MaxMembers { get; set; }
    public int? MaxTenants { get; set; }
    public string? TenantName { get; set; }
    public string? TenantDisplayName { get; set; }
    public string TenantIdentifier { get; set; } = null!;
    public string? TenantDescription { get; set; }
    public TenantEnvironment? TenantEnvironment { get; set; }
    public string AdminEmail { get; set; } = null!;
    public string? AdminFirstName { get; set; }
    public string? AdminLastName { get; set; }
    public string? BaseUrl { get; set; }
}

public class ProvisionOrganizationResponse
{
    public string OrganizationId { get; set; } = null!;
    public string OrganizationName { get; set; } = null!;
    public string TenantId { get; set; } = null!;
    public string TenantIdentifier { get; set; } = null!;
    public string AdminUserId { get; set; } = null!;
    public string AdminEmail { get; set; } = null!;
    public bool IsNewUser { get; set; }
    public bool WelcomeEmailSent { get; set; }
}

public class MemberTenantAccessDto
{
    public string TenantId { get; set; } = null!;
    public string TenantName { get; set; } = null!;
    public string TenantIdentifier { get; set; } = null!;
    public TenantEnvironment Environment { get; set; }
    public bool HasAccess { get; set; }
    public bool IsHomeTenant { get; set; }
    public List<string> Roles { get; set; } = new();
    public DateTime? JoinedAt { get; set; }
}

public class UpdateMemberTenantRoleRequest
{
    public string RoleName { get; set; } = null!;
}
