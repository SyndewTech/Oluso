namespace Oluso.Admin.Authorization;

/// <summary>
/// Security constants for reserved claim types and role names.
/// These cannot be created or modified by tenant administrators.
/// </summary>
public static class ReservedClaimTypes
{
    /// <summary>
    /// Claim types that are reserved for system use and cannot be created by tenant admins.
    /// These claims grant elevated privileges or affect system-level authorization.
    /// </summary>
    public static readonly HashSet<string> Reserved = new(StringComparer.OrdinalIgnoreCase)
    {
        // SuperAdmin/system-level claims
        "super_admin",
        "superadmin",
        "system_admin",
        "systemadmin",
        "global_admin",
        "globaladmin",
        "platform_admin",

        // Tenant bypass claims
        "tenant_bypass",
        "all_tenants",
        "cross_tenant",

        // Standard security claims that shouldn't be manually set
        "sub",                  // Subject (user ID)
        "iss",                  // Issuer
        "aud",                  // Audience
        "exp",                  // Expiration
        "nbf",                  // Not before
        "iat",                  // Issued at
        "jti",                  // JWT ID
        "auth_time",            // Authentication time
        "amr",                  // Authentication methods references
        "acr",                  // Authentication context class reference
        "azp",                  // Authorized party
        "nonce",                // Nonce
        "at_hash",              // Access token hash
        "c_hash",               // Code hash
        "s_hash",               // State hash

        // Internal tenant claims
        "tenant_id",
        "tid",
        "http://schemas.oluso.io/claims/tenant",
    };

    /// <summary>
    /// Role names that are reserved for system use and cannot be created at tenant level.
    /// These roles grant access to the Admin Dashboard and must be assigned by SuperAdmin only.
    /// </summary>
    public static readonly HashSet<string> ReservedRoles = new(StringComparer.OrdinalIgnoreCase)
    {
        "SuperAdmin",
        "SystemAdmin",
        "GlobalAdmin",
        "PlatformAdmin",
        "System",

        // Admin roles that grant dashboard access - these are system-managed
        "TenantAdmin",
        "Admin",
    };

    /// <summary>
    /// Roles that grant access to the Admin Dashboard.
    /// Users must have at least one of these roles to log in to the Admin UI.
    /// </summary>
    public static readonly HashSet<string> AdminDashboardRoles = new(StringComparer.OrdinalIgnoreCase)
    {
        "SuperAdmin",
        "SystemAdmin",
        "TenantAdmin",
        "Admin",
    };

    /// <summary>
    /// Claim type that grants access to the Admin Dashboard.
    /// This can be added to custom roles to allow admin access without using reserved role names.
    /// </summary>
    public const string AdminDashboardAccessClaim = "admin_dashboard_access";

    /// <summary>
    /// Checks if a claim type is reserved for system use.
    /// </summary>
    public static bool IsReservedClaimType(string claimType)
    {
        if (string.IsNullOrWhiteSpace(claimType))
            return false;

        // Direct match
        if (Reserved.Contains(claimType))
            return true;

        // Also check for variations with common prefixes/suffixes
        var normalized = claimType.Replace("-", "_").Replace(".", "_").ToLowerInvariant();

        // Block any claim that contains "super_admin" or similar patterns
        if (normalized.Contains("super_admin") ||
            normalized.Contains("superadmin") ||
            normalized.Contains("system_admin") ||
            normalized.Contains("systemadmin") ||
            normalized.Contains("global_admin") ||
            normalized.Contains("globaladmin") ||
            normalized.Contains("platform_admin"))
        {
            return true;
        }

        return false;
    }

    /// <summary>
    /// Checks if a role name is reserved for system use.
    /// </summary>
    public static bool IsReservedRoleName(string roleName)
    {
        if (string.IsNullOrWhiteSpace(roleName))
            return false;

        return ReservedRoles.Contains(roleName);
    }

    /// <summary>
    /// Validates that a list of claims doesn't contain any reserved claim types.
    /// Returns a list of invalid claim types found.
    /// </summary>
    public static IEnumerable<string> GetReservedClaimTypes(IEnumerable<(string Type, string Value)> claims)
    {
        return claims
            .Where(c => IsReservedClaimType(c.Type))
            .Select(c => c.Type)
            .Distinct();
    }

    /// <summary>
    /// Checks if a user has access to the Admin Dashboard based on their roles and claims.
    /// Access is granted if:
    /// 1. User has one of the AdminDashboardRoles (SuperAdmin, SystemAdmin, TenantAdmin, Admin), OR
    /// 2. User has a role with the admin_dashboard_access claim
    /// </summary>
    /// <param name="roles">User's roles</param>
    /// <param name="roleClaims">Claims from the user's roles (claim type, claim value pairs)</param>
    /// <returns>True if user has admin dashboard access</returns>
    public static bool HasAdminDashboardAccess(
        IEnumerable<string> roles,
        IEnumerable<(string Type, string Value)>? roleClaims = null)
    {
        // Check if user has any of the admin dashboard roles
        if (roles.Any(r => AdminDashboardRoles.Contains(r)))
        {
            return true;
        }

        // Check if user has the admin_dashboard_access claim from any role
        if (roleClaims != null &&
            roleClaims.Any(c =>
                c.Type.Equals(AdminDashboardAccessClaim, StringComparison.OrdinalIgnoreCase) &&
                c.Value.Equals("true", StringComparison.OrdinalIgnoreCase)))
        {
            return true;
        }

        return false;
    }

    /// <summary>
    /// Checks if a role grants admin dashboard access.
    /// </summary>
    public static bool IsAdminDashboardRole(string roleName)
    {
        if (string.IsNullOrWhiteSpace(roleName))
            return false;

        return AdminDashboardRoles.Contains(roleName);
    }
}
