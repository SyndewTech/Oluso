using Microsoft.AspNetCore.Identity;

namespace Oluso.Core.Domain.Entities;

/// <summary>
/// Oluso user entity extending ASP.NET Core Identity
/// </summary>
public class OlusoUser : IdentityUser
{
    /// <summary>
    /// Tenant this user belongs to. SuperAdmin's home tenant is the platform
    /// tenant (PlatformConstants.TenantId); OrgAdmin's is the first tenant of
    /// the org they manage. Every user has a home tenant.
    /// </summary>
    public string TenantId { get; set; } = string.Empty;

    public string? FirstName { get; set; }
    public string? LastName { get; set; }
    public string? DisplayName { get; set; }
    public string? ProfilePictureUrl { get; set; }
    public bool IsActive { get; set; } = true;
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public DateTime? UpdatedAt { get; set; }
    public DateTime? LastLoginAt { get; set; }
    public string? Locale { get; set; }
    public string? TimeZone { get; set; }

    /// <summary>
    /// Custom claims stored as JSON (legacy field; superseded by
    /// <see cref="CustomPropertiesJson"/> for arbitrary user metadata).
    /// </summary>
    public string? CustomClaims { get; set; }

    /// <summary>
    /// User-level custom-properties bag, JSON-serialised
    /// <c>Dictionary&lt;string, string&gt;</c>. Written by journey step
    /// handlers (sign-up <c>claimMappings</c>, terms acceptance, etc.)
    /// and editable from the admin UI's Custom Properties tab. Distinct
    /// from <see cref="Claims"/> which carries identity assertions
    /// surfaced in tokens.
    /// </summary>
    public string? CustomPropertiesJson { get; set; }

    /// <summary>
    /// When the user accepted terms of service
    /// </summary>
    public DateTime? TermsAcceptedAt { get; set; }

    /// <summary>
    /// When the local password was last set (created, changed, or reset). Drives the tenant's
    /// password expiry; null for accounts whose password predates tracking.
    /// </summary>
    public DateTime? PasswordChangedAt { get; set; }

    // Navigation properties for ASP.NET Core Identity
    public virtual ICollection<OlusoUserClaim> Claims { get; set; } = new List<OlusoUserClaim>();
    public virtual ICollection<OlusoUserRole> UserRoles { get; set; } = new List<OlusoUserRole>();
    public virtual ICollection<OlusoUserLogin> Logins { get; set; } = new List<OlusoUserLogin>();
    public virtual ICollection<OlusoUserToken> Tokens { get; set; } = new List<OlusoUserToken>();
}

/// <summary>
/// Oluso role entity extending ASP.NET Core Identity
/// </summary>
public class OlusoRole : IdentityRole
{
    /// <summary>
    /// Claim type used for storing permissions in RoleClaims table.
    /// Each permission is stored as a separate claim with this type.
    /// </summary>
    public const string PermissionClaimType = "permission";

    /// <summary>
    /// Claim type used for storing a tenant's own application entitlements on a
    /// role, one claim per entitlement. Distinct from
    /// <see cref="PermissionClaimType"/>: permissions are Oluso's vocabulary and
    /// gate the platform; entitlements are the tenant's, mean nothing here, and
    /// are emitted for the tenant's applications to enforce.
    ///
    /// Named after the SCIM "entitlements" attribute that RFC 9068 §2.2.3.1
    /// nominates for exactly this, so the claim key is one consumers recognise.
    /// </summary>
    public const string EntitlementClaimType = "entitlements";

    public string? TenantId { get; set; }
    public string? DisplayName { get; set; }
    public string? Description { get; set; }
    public bool IsSystemRole { get; set; }
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public DateTime? UpdatedAt { get; set; }

    /// <summary>
    /// Category for delegated administration (e.g., "system", "tenant", "finance")
    /// </summary>
    public string? Category { get; set; }

    /// <summary>
    /// The role that can manage this role (for delegated administration).
    /// E.g., "HRAdmin" can manage "PayrollAdmin", "HRManager" etc.
    /// </summary>
    public string? ManagedByRole { get; set; }

    // Navigation properties
    public virtual ICollection<OlusoUserRole> UserRoles { get; set; } = new List<OlusoUserRole>();
    public virtual ICollection<OlusoRoleClaim> RoleClaims { get; set; } = new List<OlusoRoleClaim>();

    /// <summary>
    /// Gets permissions from RoleClaims where ClaimType == "permission".
    /// Requires RoleClaims to be loaded (Include or eager loading).
    /// </summary>
    public IEnumerable<string> GetPermissions()
    {
        return RoleClaims
            .Where(c => c.ClaimType == PermissionClaimType)
            .Select(c => c.ClaimValue)
            .Where(v => !string.IsNullOrEmpty(v))!;
    }
}

/// <summary>
/// External login for an Oluso user, extending ASP.NET Core Identity.
/// Tracks when external logins (Google, Microsoft, SAML, OIDC, etc.) were linked.
/// </summary>
public class OlusoUserLogin : IdentityUserLogin<string>
{
    // ProviderDisplayName is inherited from IdentityUserLogin<string>

    /// <summary>
    /// When this external login was linked
    /// </summary>
    public DateTime LinkedAt { get; set; } = DateTime.UtcNow;

    /// <summary>
    /// Last time this external login was used
    /// </summary>
    public DateTime? LastUsedAt { get; set; }

    /// <summary>
    /// Navigation property to the user
    /// </summary>
    public virtual OlusoUser? User { get; set; }
}

/// <summary>
/// User claim entity for Oluso, extending ASP.NET Core Identity.
/// </summary>
public class OlusoUserClaim : IdentityUserClaim<string>
{
    /// <summary>
    /// When this claim was added
    /// </summary>
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

    /// <summary>
    /// Source of the claim (e.g., "admin", "ldap", "oidc")
    /// </summary>
    public string? Source { get; set; }

    /// <summary>
    /// Optional description for the claim
    /// </summary>
    public string? Description { get; set; }

    /// <summary>
    /// Navigation property to the user
    /// </summary>
    public virtual OlusoUser? User { get; set; }
}

/// <summary>
/// User-role mapping entity for Oluso, extending ASP.NET Core Identity.
/// </summary>
public class OlusoUserRole : IdentityUserRole<string>
{
    /// <summary>
    /// The tenant this assignment applies in. Part of the key, so a user can
    /// hold the same platform-wide role in some tenants and not others. For a
    /// tenant-owned role it always equals the role's TenantId. SuperAdmin and
    /// SystemAdmin apply everywhere regardless (see GlobalRoles).
    /// </summary>
    public string TenantId { get; set; } = default!;

    /// <summary>
    /// When this role was assigned
    /// </summary>
    public DateTime AssignedAt { get; set; } = DateTime.UtcNow;

    /// <summary>
    /// Who assigned this role (user ID or "system")
    /// </summary>
    public string? AssignedBy { get; set; }

    /// <summary>
    /// Navigation property to the user
    /// </summary>
    public virtual OlusoUser? User { get; set; }

    /// <summary>
    /// Navigation property to the role
    /// </summary>
    public virtual OlusoRole? Role { get; set; }
}

/// <summary>
/// Role claim entity for Oluso, extending ASP.NET Core Identity.
/// </summary>
public class OlusoRoleClaim : IdentityRoleClaim<string>
{
    /// <summary>
    /// When this claim was added
    /// </summary>
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

    /// <summary>
    /// Optional description for the claim
    /// </summary>
    public string? Description { get; set; }

    /// <summary>
    /// Navigation property to the role
    /// </summary>
    public virtual OlusoRole? Role { get; set; }
}

/// <summary>
/// User token entity for Oluso, extending ASP.NET Core Identity.
/// Used for storing authentication tokens (e.g., refresh tokens, external provider tokens).
/// </summary>
public class OlusoUserToken : IdentityUserToken<string>
{
    /// <summary>
    /// When this token was created
    /// </summary>
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

    /// <summary>
    /// When this token expires (if applicable)
    /// </summary>
    public DateTime? ExpiresAt { get; set; }

    /// <summary>
    /// Navigation property to the user
    /// </summary>
    public virtual OlusoUser? User { get; set; }
}
