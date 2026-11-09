namespace Oluso.Admin.Dto.Users;

public class UserDto
{
    /// <summary>On create with an invite: why the invite email could not be sent (the user was still created).</summary>
    public string? InviteError { get; set; }
    /// <summary>False until the user sets a password (e.g. invited and not yet accepted): the invite can be resent.</summary>
    public bool HasPassword { get; set; }
    /// <summary>Signs in through at least one external identity provider.</summary>
    public bool IsFederated { get; set; }
    public List<string> LinkedProviders { get; set; } = new();
    public string Id { get; set; } = null!;
    public string UserName { get; set; } = null!;
    public string Email { get; set; } = null!;
    public string? FirstName { get; set; }
    public string? LastName { get; set; }
    public string? DisplayName { get; set; }
    public string? Picture { get; set; }
    public bool IsActive { get; set; }
    public bool EmailVerified { get; set; }
    public string? PhoneNumber { get; set; }
    public bool PhoneNumberVerified { get; set; }
    public bool TwoFactorEnabled { get; set; }
    public bool LockoutEnabled { get; set; }
    public DateTimeOffset? LockoutEnd { get; set; }
    public List<string> Roles { get; set; } = new();
    public DateTime CreatedAt { get; set; }
    public DateTime? UpdatedAt { get; set; }
    public DateTime? LastLoginAt { get; set; }

    /// <summary>Tenant that owns this user record (their home tenant).</summary>
    public string? TenantId { get; set; }

    /// <summary>Owning tenant's name, set only when it isn't the current tenant.</summary>
    public string? OwnerTenantName { get; set; }

    /// <summary>
    /// True when the user reaches this tenant through a TenantMembership rather
    /// than living in it. Their profile, credentials and sessions are managed
    /// from their home tenant, so those controls belong disabled here.
    /// </summary>
    public bool IsExternalToTenant { get; set; }
}

public class UserDetailDto : UserDto
{
    public int AccessFailedCount { get; set; }
    public List<UserClaimDto> Claims { get; set; } = new();
    public Dictionary<string, string>? CustomProperties { get; set; }
    public string? ExternalId { get; set; }
    public string? ExternalProvider { get; set; }

    /// <summary>Tenant that owns this user record (their home tenant).</summary>
    public string? TenantId { get; set; }

    /// <summary>Owning tenant's name, set only when it isn't the current tenant.</summary>
    public string? OwnerTenantName { get; set; }

    /// <summary>
    /// True when the user reaches this tenant through a TenantMembership rather
    /// than living in it. Their profile, credentials and sessions are managed
    /// from their home tenant, so those controls belong disabled here.
    /// </summary>
    public bool IsExternalToTenant { get; set; }
}

public class UserClaimDto
{
    public string Type { get; set; } = null!;
    public string Value { get; set; } = null!;
}

public class CreateUserRequest
{
    public string Email { get; set; } = null!;
    public string? UserName { get; set; }
    public string? Password { get; set; }
    /// <summary>
    /// Email the user a link to set their own password instead of the admin choosing one; leave
    /// <see cref="Password"/> empty. The link is valid for the tenant's account-link lifetime.
    /// </summary>
    public bool SendInvite { get; set; }
    public string? FirstName { get; set; }
    public string? LastName { get; set; }
    public string? DisplayName { get; set; }
    public string? PhoneNumber { get; set; }
    public bool? IsActive { get; set; }
    public bool? EmailVerified { get; set; }
    /// <summary>
    /// Roles to assign on creation, by id. Validated before the user is
    /// written; every id must be a role of the current tenant or platform-wide.
    /// </summary>
    public List<string>? RoleIds { get; set; }
}

/// <summary>
/// Replaces the user's roles within the current tenant. Role ids, never names:
/// a tenant row and a platform-wide row may share a name.
/// </summary>
public class SetUserRolesRequest
{
    public List<string> RoleIds { get; set; } = new();
}

/// <summary>
/// A role the user holds in the current tenant, as returned by the user-roles endpoint.
/// </summary>
public class UserRoleDto
{
    public string Id { get; set; } = null!;
    public string Name { get; set; } = null!;
    public string DisplayName { get; set; } = null!;
    public bool IsGlobal { get; set; }
}

public class ResetPasswordRequest
{
    public string NewPassword { get; set; } = null!;
}

public class AdminMfaStatusDto
{
    public bool Enabled { get; set; }
    public bool TwoFactorEnabled { get; set; }
}

public class AdminExternalLoginDto
{
    public string LoginProvider { get; set; } = null!;
    public string ProviderKey { get; set; } = null!;
    public string ProviderDisplayName { get; set; } = null!;
}

public class UserSessionDto
{
    public string SessionId { get; set; } = null!;
    public string? ClientId { get; set; }
    public string? ClientName { get; set; }
    public string? IpAddress { get; set; }
    public string? UserAgent { get; set; }
    public DateTime Created { get; set; }
    public DateTime Renewed { get; set; }
    public DateTime? Expires { get; set; }
    public bool IsCurrent { get; set; }
}

public class LockUserRequest
{
    /// <summary>When the lockout ends; omit to lock until an admin unlocks.</summary>
    public DateTimeOffset? LockoutEnd { get; set; }
}
