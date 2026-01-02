using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Oluso.Core.Domain.Interfaces;

namespace Oluso.Core.Api;

/// <summary>
/// Base controller for Admin API endpoints.
/// Provides tenant context and admin user information for administrative operations.
///
/// Tenant resolution for Admin API:
/// 1. X-Tenant-Id header (preferred for SPA clients)
/// 2. tenant_id claim in JWT token
/// 3. Query parameter (for debugging only)
///
/// Admin users can switch between tenants they have access to.
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
}
