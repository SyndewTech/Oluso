using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Oluso.Core.Domain.Entities;
using Oluso.Core.Domain.Interfaces;

namespace Oluso.Core.Api;

/// <summary>
/// Base controller for Admin API endpoints.
/// Provides tenant context, organization context, and admin user information for administrative operations.
///
/// Tenant resolution for Admin API:
/// 1. X-Tenant-Id header (preferred for SPA clients)
/// 2. tenant_id claim in JWT token
/// 3. Query parameter (for debugging only)
///
/// Admin users can switch between tenants they have access to within their organization(s).
/// The TenantResolutionMiddleware validates organization membership and sets context claims.
/// </summary>
[ApiController]
[Authorize(Policy = "AdminApi")]
public abstract class AdminBaseController : ControllerBase
{
    private readonly ITenantContext _tenantContext;

    protected AdminBaseController(ITenantContext tenantContext)
    {
        _tenantContext = tenantContext;
    }

    /// <summary>
    /// Gets the current tenant context for data isolation
    /// </summary>
    protected ITenantContext TenantContext => _tenantContext;

    /// <summary>
    /// Gets the current tenant ID
    /// </summary>
    protected string? TenantId => _tenantContext.TenantId;

    /// <summary>
    /// Gets the admin user's ID from the token (sub or NameIdentifier claim).
    /// Returns null if no subject claim is present.
    /// </summary>
    protected string? AdminUserId =>
        User.FindFirst("sub")?.Value
        ?? User.FindFirst(System.Security.Claims.ClaimTypes.NameIdentifier)?.Value;

    /// <summary>
    /// Gets the admin user's ID, throwing if not authenticated.
    /// Use this when the user ID is required for the operation.
    /// </summary>
    protected string RequireAdminUserId =>
        AdminUserId ?? throw new UnauthorizedAccessException("No subject in token");

    /// <summary>
    /// Gets the admin user's display name from the token
    /// </summary>
    protected string? AdminUserName =>
        User.FindFirst("name")?.Value
        ?? User.FindFirst("preferred_username")?.Value
        ?? User.FindFirst("email")?.Value;

    /// <summary>
    /// Gets the admin user's email from the token
    /// </summary>
    protected string? AdminEmail => User.FindFirst("email")?.Value;

    /// <summary>
    /// Gets the client IP address
    /// </summary>
    protected string? ClientIp => HttpContext.Connection.RemoteIpAddress?.ToString();

    /// <summary>
    /// Gets the user agent string
    /// </summary>
    protected string? UserAgent => HttpContext.Request.Headers.UserAgent.FirstOrDefault();

    /// <summary>
    /// Checks if the admin user has a specific role
    /// </summary>
    protected bool HasRole(string role) => User.IsInRole(role);

    /// <summary>
    /// Checks if the admin user has any of the specified roles
    /// </summary>
    protected bool HasAnyRole(params string[] roles) =>
        roles.Any(role => User.IsInRole(role));

    /// <summary>
    /// Gets all roles for the current admin user
    /// </summary>
    protected IEnumerable<string> GetAdminRoles() =>
        User.FindAll("role").Select(c => c.Value);

    /// <summary>
    /// Checks if the current admin is a super admin (platform-wide access)
    /// </summary>
    protected bool IsSuperAdmin
    {
        get
        {
            // Check for super_admin claim
            var superAdminClaim = User.FindFirst("super_admin")?.Value;
            if (superAdminClaim is "true" or "1")
                return true;

            // Check for SuperAdmin or SystemAdmin role (with null tenant_id)
            var userTenantId = User.FindFirst("tenant_id")?.Value
                ?? User.FindFirst("tid")?.Value;

            // Only users with no tenant scope can be SuperAdmin
            if (!string.IsNullOrEmpty(userTenantId))
                return false;

            return HasRole("SuperAdmin") || HasRole("SystemAdmin") ||
                   HasRole("super_admin") || HasRole("platform_admin");
        }
    }

    #region Organization Context

    /// <summary>
    /// Gets the current organization ID for the tenant being accessed.
    /// This is set by TenantResolutionMiddleware after validating organization membership.
    /// Returns null if no organization context is available.
    /// </summary>
    protected string? CurrentOrganizationId =>
        User.FindFirst("current_org_id")?.Value
        ?? HttpContext.Items["CurrentOrgId"] as string;

    /// <summary>
    /// Gets the user's role in the current tenant's organization.
    /// This is the role they have in the organization that owns the current tenant.
    /// Returns null if no organization context is available.
    /// </summary>
    protected OrganizationRole? CurrentOrganizationRole
    {
        get
        {
            var roleStr = User.FindFirst("current_org_role")?.Value;
            if (string.IsNullOrEmpty(roleStr))
            {
                // Try from HttpContext.Items
                if (HttpContext.Items["CurrentOrgRole"] is OrganizationRole role)
                    return role;
                return null;
            }

            return Enum.TryParse<OrganizationRole>(roleStr, ignoreCase: true, out var parsedRole)
                ? parsedRole
                : null;
        }
    }

    /// <summary>
    /// Gets the organization membership for the current context.
    /// Returns null if not available (e.g., super admin or no org context).
    /// </summary>
    protected OrganizationMembership? CurrentOrganizationMembership =>
        HttpContext.Items["CurrentOrgMembership"] as OrganizationMembership;

    /// <summary>
    /// Checks if the user is an owner in the current tenant's organization.
    /// </summary>
    protected bool IsCurrentOrgOwner => CurrentOrganizationRole == OrganizationRole.Owner;

    /// <summary>
    /// Checks if the user is an admin (owner or admin) in the current tenant's organization.
    /// </summary>
    protected bool IsCurrentOrgAdmin =>
        CurrentOrganizationRole == OrganizationRole.Owner ||
        CurrentOrganizationRole == OrganizationRole.Admin;

    /// <summary>
    /// Checks if the user has at least the specified role in the current tenant's organization.
    /// Role hierarchy: Owner > Admin > Member
    /// </summary>
    protected bool HasCurrentOrgRole(OrganizationRole minimumRole)
    {
        var currentRole = CurrentOrganizationRole;
        if (currentRole == null)
            return false;

        // Lower enum value = higher privilege
        return currentRole <= minimumRole;
    }

    /// <summary>
    /// Gets all organization IDs the user is a member of (from JWT claims).
    /// This returns ALL organizations, not just the current one.
    /// </summary>
    protected IEnumerable<string> AllOrganizationIds =>
        User.FindAll("org_id").Select(c => c.Value);

    /// <summary>
    /// Gets the user's role in a specific organization (from JWT claims).
    /// Returns null if not a member of the organization.
    /// </summary>
    protected OrganizationRole? GetOrganizationRole(string organizationId)
    {
        // Look for org_role claim in format "org_id:role"
        var roleClaimValue = User.FindAll("org_role")
            .Select(c => c.Value)
            .FirstOrDefault(v => v.StartsWith($"{organizationId}:", StringComparison.OrdinalIgnoreCase));

        if (string.IsNullOrEmpty(roleClaimValue))
            return null;

        var rolePart = roleClaimValue.Substring(organizationId.Length + 1);
        return Enum.TryParse<OrganizationRole>(rolePart, ignoreCase: true, out var role)
            ? role
            : null;
    }

    /// <summary>
    /// Checks if the user is an admin (owner or admin) of the specified organization.
    /// Use this when you need to check permissions for a specific org, not the current context.
    /// </summary>
    protected bool IsOrgAdmin(string organizationId)
    {
        var role = GetOrganizationRole(organizationId);
        return role == OrganizationRole.Owner || role == OrganizationRole.Admin;
    }

    /// <summary>
    /// Checks if the user is an org admin in any organization.
    /// </summary>
    protected bool IsAnyOrgAdmin =>
        User.FindFirst("is_org_admin")?.Value == "true";

    #endregion
}
