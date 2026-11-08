using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Oluso.Core.Authorization;
using Oluso.Core.Domain.Interfaces;
using System.Text.Json;

namespace Oluso.Core.Api;

/// <summary>
/// Base controller for Account API endpoints (end-user self-service).
/// Tenant isolation is enforced through the authenticated user's token.
///
/// Unlike Admin API, users CANNOT arbitrarily switch tenants.
/// For multi-tenant users, tenant switching is validated against allowed tenants.
///
/// Tenant resolution for Account API:
/// 1. X-Tenant-Id header (validated against user's allowed tenants)
/// 2. Primary tenant_id claim in JWT token
/// </summary>
[ApiController]
[Authorize(Policy = "AccountApi")]
[ApiExplorerSettings(GroupName = "account")]
public abstract class AccountBaseController : ControllerBase
{
    private readonly ITenantContext _tenantContext;

    protected AccountBaseController(ITenantContext tenantContext)
    {
        _tenantContext = tenantContext;
    }

    /// <summary>
    /// Gets the current user's ID from the token (sub claim)
    /// </summary>
    protected string UserId =>
        User.FindFirst("sub")?.Value
        ?? throw new UnauthorizedAccessException("No subject in token");

    /// <summary>
    /// Gets the user's email from the token
    /// </summary>
    protected string? UserEmail => User.FindFirst("email")?.Value;

    /// <summary>
    /// Gets the user's name from the token
    /// </summary>
    protected string? UserName =>
        User.FindFirst("name")?.Value
        ?? User.FindFirst("preferred_username")?.Value;

    /// <summary>
    /// Gets the tenant ID from the authenticated user's token.
    /// For multi-tenant users, this comes from the active tenant selected during auth
    /// or from the X-Tenant-Id header (validated against user's allowed tenants).
    /// </summary>
    protected string? TenantId
    {
        get
        {
            // First check if TenantResolutionMiddleware has set a tenant
            if (_tenantContext.HasTenant)
            {
                // Validate user belongs to this tenant
                var userTenants = GetUserTenantIds();
                if (userTenants.Contains(_tenantContext.TenantId!))
                {
                    return _tenantContext.TenantId;
                }
            }

            // Fall back to primary tenant from token
            return User.FindFirst("tenant_id")?.Value;
        }
    }

    /// <summary>
    /// Gets the tenant context
    /// </summary>
    protected ITenantContext TenantContext => _tenantContext;

    /// <summary>
    /// Gets all tenant IDs the user belongs to (for multi-tenant users)
    /// </summary>
    protected IReadOnlyList<string> GetUserTenantIds() => User.GetAllowedTenantIds();

    /// <summary>
    /// Whether the current user belongs to multiple tenants
    /// </summary>
    protected bool IsMultiTenantUser => GetUserTenantIds().Count > 1;

    /// <summary>
    /// Gets the client IP address
    /// </summary>
    protected string? ClientIp => HttpContext.Connection.RemoteIpAddress?.ToString();

    /// <summary>
    /// Gets the user agent string
    /// </summary>
    protected string? UserAgent => HttpContext.Request.Headers.UserAgent.FirstOrDefault();

    /// <summary>
    /// Gets the current session ID from the token
    /// </summary>
    protected string? SessionId => User.FindFirst("sid")?.Value;

    /// <summary>
    /// Validates that the requested resource belongs to the current user
    /// </summary>
    protected bool IsOwnResource(string resourceUserId) =>
        string.Equals(UserId, resourceUserId, StringComparison.OrdinalIgnoreCase);
}
