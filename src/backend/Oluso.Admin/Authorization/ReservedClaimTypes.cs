namespace Oluso.Admin.Authorization;

/// <summary>
/// Security constants for reserved claim types and role names.
///
/// SECURITY MODEL:
/// - Permissions are system-defined constants stored in database roles
/// - Claims are identity data in tokens (profile, roles, org info)
/// - Authorization checks verify role membership against the database
/// - Token claims are NOT directly trusted for permission checks
/// </summary>
public static class ReservedClaimTypes
{
    /// <summary>
    /// Standard OIDC/JWT claim types that are system-managed.
    /// These claims are set by the token service and should not be
    /// overwritten by user claims or client claims.
    /// </summary>
    public static readonly HashSet<string> StandardClaims = new(StringComparer.OrdinalIgnoreCase)
    {
        // JWT registered claims (RFC 7519)
        "sub",                  // Subject (user ID)
        "iss",                  // Issuer
        "aud",                  // Audience
        "exp",                  // Expiration
        "nbf",                  // Not before
        "iat",                  // Issued at
        "jti",                  // JWT ID

        // OIDC authentication claims
        "auth_time",            // Authentication time
        "amr",                  // Authentication methods references
        "acr",                  // Authentication context class reference
        "azp",                  // Authorized party
        "nonce",                // Nonce
        "at_hash",              // Access token hash
        "c_hash",               // Code hash
        "s_hash",               // State hash
        "sid",                  // Session ID
    };

    /// <summary>
    /// System-level claim types that affect authorization.
    /// These are managed by the system and should not be user-settable.
    /// </summary>
    /// <remarks>
    /// Note: "permission" (singular) is NOT reserved - it's used for storing
    /// individual permissions in the RoleClaims table. Only "permissions" (plural)
    /// is reserved as it's the JWT token claim containing the aggregated array.
    /// </remarks>
    public static readonly HashSet<string> SystemClaims = new(StringComparer.OrdinalIgnoreCase)
    {
        // Tenant context
        "tenant_id",
        "tid",

        // Role claims (populated from database)
        "role",

        // Permissions claim in JWT token (aggregated from roles at token creation)
        // SECURITY: This must never be settable via client claims or API scopes
        // Note: "permission" (singular) is allowed for RoleClaims storage
        "permissions",

        // Organization context
        "org_ids",
        "org_role",
        "org_roles",
        "is_org_admin",
        "allowed_tenants",

        // Platform-wide privilege flag, computed at issuance from system roles.
        // TenantResolutionMiddleware grants every tenant on this claim alone, so
        // a stored claim carrying it is a complete cross-tenant escalation.
        "super_admin",

        // Per-user module seats — emitted from UserModuleAccess at token
        // issuance. A persisted user/role/client claim with this key would
        // let an attacker phantom-grant module access, bypassing the seats
        // table entirely.
        "module_seats",
    };

    /// <summary>
    /// Role names that are reserved for system use.
    /// These roles have special meaning in the authorization system.
    /// </summary>
    public static readonly HashSet<string> ReservedRoles = new(StringComparer.OrdinalIgnoreCase)
    {
        "SuperAdmin",
        "SystemAdmin",
        "GlobalAdmin",
        "PlatformAdmin",
        "System",
        "OrgAdmin",
        "TenantAdmin",
        "Admin",
    };

    /// <summary>
    /// Platform-level roles that can ONLY be assigned by SuperAdmin.
    /// These roles grant cross-tenant or platform-wide access.
    /// </summary>
    public static readonly HashSet<string> SuperAdminOnlyRoles = new(StringComparer.OrdinalIgnoreCase)
    {
        "SuperAdmin",
        "SystemAdmin",
        "GlobalAdmin",
        "PlatformAdmin",
        "System",
    };

    /// <summary>
    /// Organization-level roles that can only be assigned by OrgAdmins (org Owner or Admin)
    /// or SuperAdmin. These grant organization-wide access across tenants.
    /// </summary>
    public static readonly HashSet<string> OrgAdminAssignableRoles = new(StringComparer.OrdinalIgnoreCase)
    {
        "OrgAdmin",
    };

    /// <summary>
    /// Roles the system maintains from other state, never granted directly.
    /// <para>
    /// OrgAdmin is derived from organization membership by
    /// <c>OrganizationsController.SyncOrgAdminRoleAsync</c>. Assigning it by hand
    /// produces a holder with no membership behind it — admin-portal access and
    /// organization permissions while <c>org_roles</c> says they lead nothing —
    /// and the next membership change silently revokes it again.
    /// </para>
    /// </summary>
    public static readonly HashSet<string> DerivedRoles = new(StringComparer.OrdinalIgnoreCase)
    {
        "OrgAdmin",
    };

    /// <summary>
    /// Tenant-level admin roles that can be assigned by TenantAdmins, OrgAdmins, or SuperAdmin.
    /// These are self-delegatable within a tenant.
    /// </summary>
    public static readonly HashSet<string> TenantAdminAssignableRoles = new(StringComparer.OrdinalIgnoreCase)
    {
        "TenantAdmin",
        "Admin",
    };

    /// <summary>
    /// Roles that grant access to the Admin Dashboard.
    /// </summary>
    public static readonly HashSet<string> AdminDashboardRoles = new(StringComparer.OrdinalIgnoreCase)
    {
        "SuperAdmin",
        "SystemAdmin",
        "OrgAdmin",
        "TenantAdmin",
        "Admin",
    };

    /// <summary>
    /// Claim type that grants access to the Admin Dashboard.
    /// Can be added to custom roles for admin access.
    /// </summary>
    public const string AdminDashboardAccessClaim = "admin_dashboard_access";

    /// <summary>
    /// Checks if a claim type is a standard OIDC/JWT claim that should not be overwritten.
    /// </summary>
    public static bool IsStandardClaim(string claimType)
    {
        if (string.IsNullOrWhiteSpace(claimType))
            return false;

        return StandardClaims.Contains(claimType);
    }

    /// <summary>
    /// Checks if a claim type is a system-managed claim.
    /// </summary>
    public static bool IsSystemClaim(string claimType)
    {
        if (string.IsNullOrWhiteSpace(claimType))
            return false;

        return SystemClaims.Contains(claimType);
    }

    /// <summary>
    /// Checks if a claim type should not be settable via client claims.
    /// Client claims should not override standard or system claims.
    /// </summary>
    public static bool IsProtectedFromClientClaims(string claimType)
    {
        if (string.IsNullOrWhiteSpace(claimType))
            return false;

        return IsStandardClaim(claimType) || IsSystemClaim(claimType);
    }

    /// <summary>
    /// Checks if a claim type is reserved for system use.
    /// </summary>
    public static bool IsReservedClaimType(string claimType)
    {
        if (string.IsNullOrWhiteSpace(claimType))
            return false;

        return IsStandardClaim(claimType) || IsSystemClaim(claimType);
    }

    /// <summary>
    /// Checks if a claim type should not be manually set via the Claims API.
    /// This includes reserved claims plus "permission" which should be set via Permissions API.
    /// </summary>
    public static bool IsProtectedClaimType(string claimType)
    {
        if (string.IsNullOrWhiteSpace(claimType))
            return false;

        // "permission" should be set via the Permissions array, not Claims
        if (claimType.Equals("permission", StringComparison.OrdinalIgnoreCase))
            return true;

        return IsReservedClaimType(claimType);
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
    /// Validates that a list of claims doesn't contain any protected claim types.
    /// Protected claims include reserved claims plus "permission" (use Permissions API instead).
    /// Returns a list of invalid claim types found.
    /// </summary>
    public static IEnumerable<string> GetProtectedClaimTypes(IEnumerable<(string Type, string Value)> claims)
    {
        return claims
            .Where(c => IsProtectedClaimType(c.Type))
            .Select(c => c.Type)
            .Distinct();
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
    /// Checks if a role can only be assigned by SuperAdmin (platform-level roles).
    /// </summary>
    public static bool IsSuperAdminOnlyRole(string roleName)
    {
        if (string.IsNullOrWhiteSpace(roleName))
            return false;

        return SuperAdminOnlyRoles.Contains(roleName);
    }

    /// <summary>
    /// Checks if a reserved role can be assigned by an OrgAdmin (org Owner or Admin).
    /// This includes org-level roles (OrgAdmin) plus tenant-level roles (TenantAdmin, Admin).
    /// </summary>
    public static bool IsOrgAdminAssignableRole(string roleName)
    {
        if (string.IsNullOrWhiteSpace(roleName))
            return false;

        return OrgAdminAssignableRoles.Contains(roleName)
            || TenantAdminAssignableRoles.Contains(roleName);
    }

    /// <summary>
    /// Checks if a reserved role can be self-delegated by a TenantAdmin within their tenant.
    /// </summary>
    public static bool IsTenantAdminAssignableRole(string roleName)
    {
        if (string.IsNullOrWhiteSpace(roleName))
            return false;

        return TenantAdminAssignableRoles.Contains(roleName);
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

    /// <summary>
    /// Checks if a user has access to the Admin Dashboard based on their roles and claims.
    /// </summary>
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
}
