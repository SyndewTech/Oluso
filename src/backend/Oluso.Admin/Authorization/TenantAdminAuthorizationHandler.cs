using Microsoft.AspNetCore.Authorization;
using Microsoft.Extensions.Logging;
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

    public TenantAdminRequirement(bool requireSuperAdmin = false)
    {
        RequireSuperAdmin = requireSuperAdmin;
    }
}

/// <summary>
/// Authorization handler for tenant admin access.
/// Validates that users have appropriate admin roles and tenant context.
///
/// Access levels:
/// - SuperAdmin (TenantId = null): Can access any tenant
/// - TenantAdmin/Admin (TenantId = specific): Can only access their own tenant
/// </summary>
public class TenantAdminAuthorizationHandler : AuthorizationHandler<TenantAdminRequirement>
{
    // System-level admin roles (reserved, cannot be created at tenant level)
    private static readonly string[] SystemAdminRoles = { "SuperAdmin", "SystemAdmin" };

    // Tenant-level admin roles
    private static readonly string[] TenantAdminRoles = { "TenantAdmin", "Admin" };

    // All admin roles combined
    private static readonly string[] AllAdminRoles = SystemAdminRoles.Concat(TenantAdminRoles).ToArray();

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

        var userId = user.FindFirst("sub")?.Value ?? user.FindFirst("http://schemas.xmlsoap.org/ws/2005/05/identity/claims/nameidentifier")?.Value;

        // Check for super_admin claim first (most authoritative)
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

        // Check if user is a system-level admin (TenantId = null and has system admin role)
        var isSystemAdmin = string.IsNullOrEmpty(userTenantId) &&
            SystemAdminRoles.Any(role => user.IsInRole(role));

        if (requirement.RequireSuperAdmin)
        {
            // SuperAdmin policy: Only system-level admins can pass
            if (isSystemAdmin)
            {
                _logger.LogDebug("User {UserId} authorized as SuperAdmin (system-level)", userId);
                context.Succeed(requirement);
            }
            else
            {
                _logger.LogWarning("User {UserId} denied SuperAdmin access - not a system-level admin", userId);
            }
            return Task.CompletedTask;
        }

        // TenantAdmin/AdminApi policy: Allow system admins or tenant-scoped admins
        if (isSystemAdmin)
        {
            // System admins can access any tenant
            _logger.LogDebug("User {UserId} (SuperAdmin) accessing tenant {TenantId}", userId, _tenantContext.TenantId);
            context.Succeed(requirement);
            return Task.CompletedTask;
        }

        // Check for tenant-level admin roles
        var hasTenantAdminRole = AllAdminRoles.Any(role => user.IsInRole(role));
        if (!hasTenantAdminRole)
        {
            _logger.LogDebug("User {UserId} has no admin role", userId);
            return Task.CompletedTask;
        }

        // Validate tenant context matches
        var requestTenantId = _tenantContext.TenantId;
        if (string.IsNullOrEmpty(requestTenantId))
        {
            // No tenant context - only system admins allowed
            _logger.LogWarning("User {UserId} denied - no tenant context and not a system admin", userId);
            return Task.CompletedTask;
        }

        if (!string.Equals(userTenantId, requestTenantId, StringComparison.OrdinalIgnoreCase))
        {
            // Tenant mismatch - user trying to access different tenant
            _logger.LogWarning(
                "User {UserId} from tenant {UserTenant} attempted to access tenant {RequestTenant}",
                userId, userTenantId, requestTenantId);
            return Task.CompletedTask;
        }

        // User is tenant admin accessing their own tenant
        _logger.LogDebug("User {UserId} authorized as TenantAdmin for tenant {TenantId}", userId, requestTenantId);
        context.Succeed(requirement);
        return Task.CompletedTask;
    }
}
