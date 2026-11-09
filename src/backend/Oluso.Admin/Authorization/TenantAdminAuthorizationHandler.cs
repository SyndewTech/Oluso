using System.Text.Json;
using Microsoft.AspNetCore.Authorization;
using Microsoft.Extensions.Logging;
using Oluso.Core;
using Oluso.Core.Domain.Interfaces;

namespace Oluso.Admin.Authorization;

/// <summary>
/// Requires a system-level admin (SuperAdmin), or optionally an organization admin. Tenant admin access is the
/// admin.access permission (the AdminApi policy), not this requirement.
/// </summary>
public class TenantAdminRequirement : IAuthorizationRequirement
{
    /// <summary>
    /// If true, organization admins (is_org_admin) also pass.
    /// </summary>
    public bool AllowOrgAdmin { get; }

    public TenantAdminRequirement(bool allowOrgAdmin = false)
    {
        AllowOrgAdmin = allowOrgAdmin;
    }
}

/// <summary>
/// Authorization handler for the SuperAdmin and SuperAdminOrOrgAdmin policies.
/// A SuperAdmin is a super_admin claim, or a SuperAdmin/SystemAdmin role with no tenant. An organization admin is the
/// is_org_admin claim, emitted for an Owner/Admin organization membership.
/// </summary>
public class TenantAdminAuthorizationHandler : AuthorizationHandler<TenantAdminRequirement>
{
    // System-level admin roles (reserved, cannot be created at tenant level)
    private static readonly string[] SystemAdminRoles = { "SuperAdmin", "SystemAdmin" };

    // Role claim types to check (JWT may use different claim types)
    private static readonly string[] RoleClaimTypes =
    {
        "role",
        "roles",
        System.Security.Claims.ClaimTypes.Role,
        "http://schemas.microsoft.com/ws/2008/06/identity/claims/role"
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

        if (IsTruthyClaim(user.FindFirst("super_admin")?.Value))
        {
            context.Succeed(requirement);
            return Task.CompletedTask;
        }

        var userTenantId = user.FindFirst("tenant_id")?.Value
            ?? user.FindFirst("tid")?.Value;
        var userRoles = GetUserRoles(user);

        // System admins have no tenant_id in their token. Also accept "_platform" in case the token was issued
        // before the profile-based tenant_id fix.
        var isSystemAdmin = (string.IsNullOrEmpty(userTenantId) || userTenantId == PlatformConstants.TenantId) &&
            SystemAdminRoles.Any(role => userRoles.Contains(role, StringComparer.OrdinalIgnoreCase));
        if (isSystemAdmin)
        {
            context.Succeed(requirement);
            return Task.CompletedTask;
        }

        if (requirement.AllowOrgAdmin && IsTruthyClaim(user.FindFirst("is_org_admin")?.Value))
        {
            _logger.LogDebug("User {UserId} authorized as OrgAdmin for SuperAdmin-or-OrgAdmin policy", userId);
            context.Succeed(requirement);
            return Task.CompletedTask;
        }

        _logger.LogWarning("User {UserId} denied SuperAdmin access - not a system-level admin or org admin (tenant {TenantId})",
            userId, _tenantContext.TenantId ?? "(none)");
        return Task.CompletedTask;
    }

    /// <summary>
    /// Gets user roles from claims, checking multiple claim types that may be used.
    /// Handles both individual role claims and JSON array role claims from OIDC tokens.
    /// </summary>
    private static HashSet<string> GetUserRoles(System.Security.Claims.ClaimsPrincipal user)
    {
        var roles = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var claimType in RoleClaimTypes)
        {
            foreach (var claim in user.FindAll(claimType))
            {
                if (string.IsNullOrWhiteSpace(claim.Value))
                    continue;

                // OIDC tokens may emit roles as a JSON array (e.g. ["SuperAdmin","TenantAdmin"])
                // The JWT handler may return this as a single claim value instead of decomposing it
                if (claim.Value.StartsWith('['))
                {
                    try
                    {
                        var parsed = JsonSerializer.Deserialize<string[]>(claim.Value);
                        if (parsed != null)
                        {
                            foreach (var role in parsed)
                            {
                                if (!string.IsNullOrWhiteSpace(role))
                                    roles.Add(role);
                            }
                            continue;
                        }
                    }
                    catch
                    {
                        // Not valid JSON array — treat as literal value
                    }
                }

                roles.Add(claim.Value);
            }
        }
        return roles;
    }

    /// <summary>
    /// Case-insensitive check for boolean claim values.
    /// Handles "true", "True", "TRUE", and "1" (all forms that can appear from JSON bool roundtripping).
    /// </summary>
    private static bool IsTruthyClaim(string? value) =>
        string.Equals(value, "true", StringComparison.OrdinalIgnoreCase) || value == "1";
}
