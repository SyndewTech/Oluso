using Microsoft.AspNetCore.Authorization;
using Microsoft.Extensions.Logging;
using Oluso.Core.Domain.Entities;
using Oluso.Core.Domain.Interfaces;

namespace Oluso.Admin.Authorization;

/// <summary>
/// Authorization requirement for tenant admin access.
/// Can require either tenant-scoped admin or system-wide SuperAdmin access.
/// </summary>
public class TenantAdminRequirement : IAuthorizationRequirement
{
    /// <summary>
    /// If true, requires SuperAdmin role with TenantId = null.
    /// If false, allows any admin role with matching tenant context.
    /// </summary>
    public bool RequireSuperAdmin { get; }

    /// <summary>
    /// If true, also allows OrgAdmin role (in addition to SuperAdmin when RequireSuperAdmin is true).
    /// </summary>
    public bool AllowOrgAdmin { get; }

    public TenantAdminRequirement(bool requireSuperAdmin = false, bool allowOrgAdmin = false)
    {
        RequireSuperAdmin = requireSuperAdmin;
        AllowOrgAdmin = allowOrgAdmin;
    }
}

/// <summary>
/// Authorization handler for tenant admin access.
/// Validates that users have appropriate admin roles, organization membership, or tenant context.
///
/// Access levels (in order of precedence):
/// 1. SuperAdmin (super_admin claim or SystemAdmin role with no tenant): Can access any tenant
/// 2. Organization Admin (is_org_admin claim + current_org_role set by middleware): Can access org tenants
/// 3. TenantAdmin/Admin (tenant_id claim matching request): Can only access their own tenant
/// </summary>
public class TenantAdminAuthorizationHandler : AuthorizationHandler<TenantAdminRequirement>
{
    // System-level admin roles (reserved, cannot be created at tenant level)
    private static readonly string[] SystemAdminRoles = { "SuperAdmin", "SystemAdmin" };

    // Organization-level admin roles
    private static readonly string[] OrgAdminRoles = { "OrgAdmin" };

    // Tenant-level admin roles
    private static readonly string[] TenantAdminRoles = { "TenantAdmin", "Admin" };

    // All admin roles combined
    private static readonly string[] AllAdminRoles = SystemAdminRoles.Concat(OrgAdminRoles).Concat(TenantAdminRoles).ToArray();

    // Role claim types to check (JWT may use different claim types)
    private static readonly string[] RoleClaimTypes =
    {
        "role",
        "roles",
        System.Security.Claims.ClaimTypes.Role, // http://schemas.microsoft.com/ws/2008/06/identity/claims/role
        "http://schemas.oluso.io/claims/role"
    };

    private readonly ITenantContext _tenantContext;
    private readonly ILogger<TenantAdminAuthorizationHandler> _logger;

    public TenantAdminAuthorizationHandler(
        ITenantContext tenantContext,
        ILogger<TenantAdminAuthorizationHandler> logger)
    {
        _tenantContext = tenantContext;
        _logger = logger;
    }

    protected override Task HandleRequirementAsync(
        AuthorizationHandlerContext context,
        TenantAdminRequirement requirement)
    {
        var user = context.User;

        if (!user.Identity?.IsAuthenticated ?? true)
        {
            _logger.LogDebug("User not authenticated");
            return Task.CompletedTask;
        }

        var userId = user.FindFirst("sub")?.Value
            ?? user.FindFirst("http://schemas.xmlsoap.org/ws/2005/05/identity/claims/nameidentifier")?.Value;

        // 1. Check for super_admin claim first (most authoritative)
        var superAdminClaim = user.FindFirst("super_admin")?.Value;
        if (superAdminClaim is "true" or "1")
        {
            _logger.LogDebug("User {UserId} authorized via super_admin claim", userId);
            context.Succeed(requirement);
            return Task.CompletedTask;
        }

        // Get user's tenant ID from claims
        var userTenantId = user.FindFirst("tenant_id")?.Value
            ?? user.FindFirst("tid")?.Value
            ?? user.FindFirst("http://schemas.oluso.io/claims/tenant")?.Value;

        // Get user's roles from claims (check multiple claim types)
        var userRoles = GetUserRoles(user);

        // 2. Check if user is a system-level admin (TenantId = null and has system admin role)
        var isSystemAdmin = string.IsNullOrEmpty(userTenantId) &&
            SystemAdminRoles.Any(role => userRoles.Contains(role, StringComparer.OrdinalIgnoreCase));

        // Check if user is an organization-level admin (TenantId = null and has OrgAdmin role)
        var isOrgLevelAdmin = string.IsNullOrEmpty(userTenantId) &&
            OrgAdminRoles.Any(role => userRoles.Contains(role, StringComparer.OrdinalIgnoreCase));

        if (requirement.RequireSuperAdmin)
        {
            // SuperAdmin policy: System-level admins can pass
            if (isSystemAdmin)
            {
                _logger.LogDebug("User {UserId} authorized as SuperAdmin (system-level)", userId);
                context.Succeed(requirement);
                return Task.CompletedTask;
            }

            // If AllowOrgAdmin is set, org-level admins can also pass
            if (requirement.AllowOrgAdmin && isOrgLevelAdmin)
            {
                _logger.LogDebug("User {UserId} authorized as OrgAdmin for SuperAdmin-or-OrgAdmin policy", userId);
                context.Succeed(requirement);
                return Task.CompletedTask;
            }

            _logger.LogWarning("User {UserId} denied SuperAdmin access - not a system-level admin or org admin", userId);
            return Task.CompletedTask;
        }

        // TenantAdmin/AdminApi policy: Allow system admins, org admins, or tenant-scoped admins
        if (isSystemAdmin)
        {
            // System admins can access any tenant (or no tenant for org management)
            _logger.LogDebug("User {UserId} (SuperAdmin) accessing tenant {TenantId}", userId, _tenantContext.TenantId ?? "(none)");
            context.Succeed(requirement);
            return Task.CompletedTask;
        }

        // Organization-level admins (OrgAdmin role with no tenant)
        if (isOrgLevelAdmin)
        {
            // OrgAdmins can access any endpoint - they manage organizations and their tenants
            // Access to specific tenant data will be filtered at the data layer based on org membership
            _logger.LogDebug("User {UserId} (OrgAdmin) authorized for endpoint (tenant: {TenantId})",
                userId, _tenantContext.TenantId ?? "(none)");
            context.Succeed(requirement);
            return Task.CompletedTask;
        }

        // 3. Check for organization-based admin access
        // The TenantResolutionMiddleware sets current_org_role when user has valid org membership
        var currentOrgRole = user.FindFirst("current_org_role")?.Value;
        if (!string.IsNullOrEmpty(currentOrgRole))
        {
            // User's access was validated by TenantResolutionMiddleware via organization membership
            // They have a valid role in the organization that owns the current tenant
            if (Enum.TryParse<OrganizationRole>(currentOrgRole, ignoreCase: true, out var orgRole))
            {
                _logger.LogDebug(
                    "User {UserId} authorized via organization membership with role {OrgRole} for tenant {TenantId}",
                    userId, orgRole, _tenantContext.TenantId);
                context.Succeed(requirement);
                return Task.CompletedTask;
            }
        }

        // 4. Check for is_org_admin claim (user is admin in at least one org)
        // This allows access to organization management endpoints without tenant context
        var isOrgAdmin = user.FindFirst("is_org_admin")?.Value;
        if (isOrgAdmin is "true" or "1")
        {
            // Org admins can access endpoints that don't require tenant context
            // (e.g., /api/admin/organizations, /api/admin/context)
            var requestTenantId = _tenantContext.TenantId;
            if (string.IsNullOrEmpty(requestTenantId))
            {
                _logger.LogDebug("User {UserId} authorized as OrgAdmin for non-tenant-scoped request", userId);
                context.Succeed(requirement);
                return Task.CompletedTask;
            }
            // If there's a tenant context but no current_org_role, the middleware rejected access
            // Don't succeed here - fall through to tenant role check
        }

        // 5. Legacy: Check for tenant-level admin roles
        var hasTenantAdminRole = AllAdminRoles.Any(role => userRoles.Contains(role, StringComparer.OrdinalIgnoreCase));
        if (!hasTenantAdminRole)
        {
            _logger.LogDebug("User {UserId} has no admin role or org membership", userId);
            return Task.CompletedTask;
        }

        // Validate tenant context matches
        var requestTenant = _tenantContext.TenantId;
        if (string.IsNullOrEmpty(requestTenant))
        {
            // No tenant context - only system admins or org admins allowed (already checked above)
            _logger.LogWarning("User {UserId} denied - no tenant context and not a system/org admin", userId);
            return Task.CompletedTask;
        }

        if (!string.Equals(userTenantId, requestTenant, StringComparison.OrdinalIgnoreCase))
        {
            // Tenant mismatch - user trying to access different tenant
            _logger.LogWarning(
                "User {UserId} from tenant {UserTenant} attempted to access tenant {RequestTenant}",
                userId, userTenantId, requestTenant);
            return Task.CompletedTask;
        }

        // User is tenant admin accessing their own tenant
        _logger.LogDebug("User {UserId} authorized as TenantAdmin for tenant {TenantId}", userId, requestTenant);
        context.Succeed(requirement);
        return Task.CompletedTask;
    }

    /// <summary>
    /// Gets user roles from claims, checking multiple claim types that may be used.
    /// </summary>
    private static HashSet<string> GetUserRoles(System.Security.Claims.ClaimsPrincipal user)
    {
        var roles = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var claimType in RoleClaimTypes)
        {
            foreach (var claim in user.FindAll(claimType))
            {
                if (!string.IsNullOrWhiteSpace(claim.Value))
                {
                    roles.Add(claim.Value);
                }
            }
        }
        return roles;
    }
}
