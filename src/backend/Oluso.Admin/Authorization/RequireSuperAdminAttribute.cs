using Microsoft.AspNetCore.Authorization;

namespace Oluso.Admin.Authorization;

/// <summary>
/// Restricts access to SuperAdmin or SystemAdmin role only.
/// Checks the "role" claim in the token — orthogonal to permission checks.
///
/// Use this alongside [RequirePermission] for SuperAdmin-only endpoints:
///   [RequireSuperAdmin]
///   [RequirePermission(AdminPermissions.TenantsRead)]
///
/// The permission check ensures the user has the right grant.
/// The role check ensures only SuperAdmins can reach the endpoint.
/// </summary>
[AttributeUsage(AttributeTargets.Method | AttributeTargets.Class)]
public class RequireSuperAdminAttribute : AuthorizeAttribute
{
    public RequireSuperAdminAttribute()
    {
        Roles = "SuperAdmin,SystemAdmin";
    }
}
