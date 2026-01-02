using Microsoft.AspNetCore.Identity;

namespace Oluso.Core.Domain.Entities;

/// <summary>
/// Oluso user entity extending ASP.NET Core Identity
/// </summary>
public class OlusoUser : IdentityUser
{
    /// <summary>
    /// Tenant this user belongs to. Null for system-level users (SuperAdmin).
    /// </summary>
    public string? TenantId { get; set; }

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
    /// Custom claims stored as JSON
    /// </summary>
    public string? CustomClaims { get; set; }

    /// <summary>
    /// When the user accepted terms of service
    /// </summary>
    public DateTime? TermsAcceptedAt { get; set; }

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
    public string? TenantId { get; set; }
    public string? DisplayName { get; set; }
    public string? Description { get; set; }
    public bool IsSystemRole { get; set; }
    public string? Permissions { get; set; }
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public DateTime? UpdatedAt { get; set; }

    // Navigation properties
    public virtual ICollection<OlusoUserRole> UserRoles { get; set; } = new List<OlusoUserRole>();
    public virtual ICollection<OlusoRoleClaim> RoleClaims { get; set; } = new List<OlusoRoleClaim>();

    /// <summary>
    /// Gets permissions as a list
    /// </summary>
    public IEnumerable<string> GetPermissions()
    {
        if (string.IsNullOrEmpty(Permissions))
            return Enumerable.Empty<string>();

        return Permissions.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
    }

    /// <summary>
    /// Sets permissions from a list
    /// </summary>
    public void SetPermissions(IEnumerable<string> permissions)
    {
        Permissions = string.Join(",", permissions);
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
