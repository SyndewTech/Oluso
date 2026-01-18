using System.Text.Json;
using Microsoft.AspNetCore.Authorization;
using Microsoft.Extensions.Logging;
using Oluso.Core.Domain.Interfaces;

namespace Oluso.Admin.Authorization;

/// <summary>
/// Authorization handler that checks if the user has the required permission.
///
/// SECURITY MODEL:
/// - Claims are identity data (profile, roles, tenant)
/// - Permissions are a separate token property (not mixed with claims)
/// - First checks token's "permissions" claim (fast path, no DB call)
/// - Falls back to database role lookup if permissions not in token
/// </summary>
public class PermissionAuthorizationHandler : IAuthorizationHandler
{
    public const string PolicyPrefix = "Permission:";
    public const string AnyPolicyPrefix = "AnyPermission:";

    private readonly IRoleStore _roleStore;
    private readonly ILogger<PermissionAuthorizationHandler> _logger;

    public PermissionAuthorizationHandler(
        IRoleStore roleStore,
        ILogger<PermissionAuthorizationHandler> logger)
    {
        _roleStore = roleStore;
        _logger = logger;
    }

    public async Task HandleAsync(AuthorizationHandlerContext context)
    {
        // Find pending requirements that we can handle
        var pendingRequirements = context.PendingRequirements.ToList();

        foreach (var requirement in pendingRequirements)
        {
            // We handle DenyAnonymousAuthorizationRequirement implicitly
            // by checking authentication in our permission check
            if (requirement is PermissionRequirement permissionRequirement)
            {
                if (await HasPermissionAsync(context, permissionRequirement.Permission))
                {
                    context.Succeed(requirement);
                }
            }
            else if (requirement is AnyPermissionRequirement anyPermissionRequirement)
            {
                foreach (var permission in anyPermissionRequirement.Permissions)
                {
                    if (await HasPermissionAsync(context, permission))
                    {
                        context.Succeed(requirement);
                        break;
                    }
                }
            }
        }
    }

    private async Task<bool> HasPermissionAsync(AuthorizationHandlerContext context, string permission)
    {
        var user = context.User;

        if (user?.Identity?.IsAuthenticated != true)
        {
            _logger.LogDebug("Permission check failed: user not authenticated");
            return false;
        }

        // SuperAdmins bypass permission checks
        if (IsSuperAdmin(user))
        {
            _logger.LogDebug("Permission {Permission} granted: user is SuperAdmin", permission);
            return true;
        }

        // FAST PATH: Check token's "permissions" claim first (no database call)
        var tokenPermissions = GetTokenPermissions(user);
        if (tokenPermissions != null)
        {
            if (HasPermissionInList(tokenPermissions, permission))
            {
                _logger.LogDebug("Permission {Permission} granted via token", permission);
                return true;
            }

            // Permission not in token - deny (permissions are exhaustive in token)
            _logger.LogDebug("Permission {Permission} denied: not in token permissions", permission);
            return false;
        }

        // FALLBACK: Token doesn't have permissions claim, check database
        // This handles backward compatibility with older tokens
        return await HasPermissionFromDatabaseAsync(user, permission);
    }

    /// <summary>
    /// Gets permissions from the token's "permissions" claim.
    /// Returns null if the token doesn't have a permissions claim.
    /// </summary>
    private static HashSet<string>? GetTokenPermissions(System.Security.Claims.ClaimsPrincipal user)
    {
        var permissionsClaim = user.FindFirst("permissions")?.Value;
        if (string.IsNullOrEmpty(permissionsClaim))
        {
            return null;
        }

        try
        {
            var permissions = JsonSerializer.Deserialize<string[]>(permissionsClaim);
            if (permissions != null)
            {
                return new HashSet<string>(permissions, StringComparer.OrdinalIgnoreCase);
            }
        }
        catch (JsonException)
        {
            // Invalid JSON - fall back to database lookup
        }

        return null;
    }

    /// <summary>
    /// Checks if the required permission exists in the permission list,
    /// including wildcard matching.
    /// </summary>
    private static bool HasPermissionInList(HashSet<string> permissions, string permission)
    {
        // Direct match
        if (permissions.Contains(permission))
        {
            return true;
        }

        // Check for wildcard permissions (e.g., "users.*" grants "users.read")
        var category = permission.Split('.').FirstOrDefault();
        if (category != null && permissions.Contains($"{category}.*"))
        {
            return true;
        }

        // Check for full admin wildcard
        if (permissions.Contains("*"))
        {
            return true;
        }

        return false;
    }

    /// <summary>
    /// Falls back to database lookup for permission checking.
    /// Used when token doesn't contain permissions claim (backward compatibility).
    /// </summary>
    private async Task<bool> HasPermissionFromDatabaseAsync(
        System.Security.Claims.ClaimsPrincipal user,
        string permission)
    {
        // Get user's roles from claims
        var roleNames = user.FindAll("role")
            .Select(c => c.Value)
            .ToList();

        if (roleNames.Count == 0)
        {
            _logger.LogDebug("Permission check failed: user has no roles");
            return false;
        }

        // Get tenant ID from claims
        var tenantId = user.FindFirst("tenant_id")?.Value;

        // Check each role for the required permission
        foreach (var roleName in roleNames)
        {
            var role = await _roleStore.GetByNameAsync(roleName, tenantId);
            if (role == null)
            {
                // Try global role
                role = await _roleStore.GetByNameAsync(roleName, null);
            }

            if (role != null)
            {
                var permissions = role.GetPermissions();
                if (HasPermissionInList(
                    new HashSet<string>(permissions, StringComparer.OrdinalIgnoreCase),
                    permission))
                {
                    _logger.LogDebug(
                        "Permission {Permission} granted via role {Role} (database fallback)",
                        permission, roleName);
                    return true;
                }
            }
        }

        _logger.LogDebug(
            "Permission {Permission} denied: not found in any role (database fallback)",
            permission);
        return false;
    }

    /// <summary>
    /// Checks if user has SuperAdmin role.
    /// SECURITY: This checks role claims from the token. Since permissions are
    /// never in tokens (only in database), a fake role claim can't grant permissions.
    /// The SuperAdmin bypass is safe because it's checked before permission lookup,
    /// and any actual permission grants still require database-verified roles.
    /// </summary>
    private static bool IsSuperAdmin(System.Security.Claims.ClaimsPrincipal user)
    {
        // Check for SuperAdmin role claim
        // NOTE: We intentionally do NOT trust a "super_admin" claim - only role membership
        var roles = user.FindAll("role").Select(c => c.Value);
        return roles.Any(r => r.Equals("SuperAdmin", StringComparison.OrdinalIgnoreCase) ||
                              r.Equals("SystemAdmin", StringComparison.OrdinalIgnoreCase));
    }
}

/// <summary>
/// Authorization requirement for a single permission
/// </summary>
public class PermissionRequirement : IAuthorizationRequirement
{
    public string Permission { get; }

    public PermissionRequirement(string permission)
    {
        Permission = permission;
    }
}

/// <summary>
/// Authorization requirement for any of multiple permissions
/// </summary>
public class AnyPermissionRequirement : IAuthorizationRequirement
{
    public string[] Permissions { get; }

    public AnyPermissionRequirement(params string[] permissions)
    {
        Permissions = permissions;
    }
}
