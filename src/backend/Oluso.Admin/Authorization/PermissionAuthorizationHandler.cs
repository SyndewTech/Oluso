using System.Text.Json;
using Microsoft.AspNetCore.Authorization;
using Microsoft.Extensions.Logging;
using Oluso.Core.Authorization;
using Oluso.Core.Domain.Entities;
using Oluso.Core.Domain.Interfaces;
using Oluso.Core.Services;

namespace Oluso.Admin.Authorization;

/// <summary>
/// Authorization handler that checks if the user has the required permission.
///
/// SECURITY MODEL:
/// - Claims are identity data (profile, roles, tenant)
/// - Permissions are a separate token property (not mixed with claims)
/// - First checks token's "permissions" claim (fast path, no DB call), but only
///   while the request is still in the tenant the token was minted for
/// - Falls back to database role lookup otherwise, resolved against the tenant
///   the request actually landed in
/// </summary>
public class PermissionAuthorizationHandler : IAuthorizationHandler
{
    private readonly IEffectiveRoleResolver _effectiveRoles;
    private readonly ITenantContext _tenantContext;
    private readonly ILogger<PermissionAuthorizationHandler> _logger;

    public PermissionAuthorizationHandler(
        IEffectiveRoleResolver effectiveRoles,
        ITenantContext tenantContext,
        ILogger<PermissionAuthorizationHandler> logger)
    {
        _effectiveRoles = effectiveRoles;
        _tenantContext = tenantContext;
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

        // NOTE: No SuperAdmin bypass here. All roles (including SuperAdmin) go through
        // the same permission evaluation. SuperAdmin gets "*" wildcard which grants
        // everything except RequiresExplicitGrant permissions. This ensures sensitive
        // operations (sensitive financial or data access, etc.) always require explicit grants.

        // FAST PATH: Check token's "permissions" claim first (no database call).
        //
        // Only valid while the request is still scoped to the tenant the token was
        // minted for. A client that switches tenant with X-Tenant-Id carries the
        // old tenant's permissions in its token until it re-mints, and those must
        // not be honoured in the new tenant — roles are per-tenant assignments, so
        // the same user can legitimately hold very different permissions either
        // side of a switch.
        var tokenPermissions = TokenMatchesCurrentTenant(user)
            ? GetTokenPermissions(user)
            : null;
        _logger.LogDebug("Token permissions count: {Count}", tokenPermissions?.Count ?? -1);

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
        _logger.LogDebug("No permissions in token, falling back to database lookup");
        return await HasPermissionFromDatabaseAsync(user, permission);
    }

    /// <summary>
    /// Gets permissions from the token's "permissions" claim.
    /// Handles two formats:
    /// 1. Single claim with JSON array value: {"permissions": ["*","users.read"]}
    /// 2. Multiple individual claims (JWT library splits JSON arrays): permissions=*, permissions=users.read
    /// Returns null if the token doesn't have a permissions claim.
    /// </summary>
    private HashSet<string>? GetTokenPermissions(System.Security.Claims.ClaimsPrincipal user)
    {
        var permissionsClaim = user.FindFirst("permissions")?.Value;
        if (string.IsNullOrEmpty(permissionsClaim))
        {
            _logger.LogDebug("No permissions claim found in token");
            return null;
        }

        // Try JSON array format first (single claim with serialized array)
        if (permissionsClaim.StartsWith('['))
        {
            try
            {
                var permissions = JsonSerializer.Deserialize<string[]>(permissionsClaim);
                if (permissions != null)
                {
                    _logger.LogDebug("Parsed {Count} permissions from token (JSON array)", permissions.Length);
                    return new HashSet<string>(permissions, StringComparer.OrdinalIgnoreCase);
                }
            }
            catch (JsonException ex)
            {
                _logger.LogWarning(ex, "Failed to parse permissions claim as JSON array");
            }
        }

        // JWT library split the JSON array into individual claims — collect them all
        var allClaims = user.FindAll("permissions").Select(c => c.Value).ToList();
        if (allClaims.Count > 0)
        {
            _logger.LogDebug("Collected {Count} permissions from individual token claims", allClaims.Count);
            return new HashSet<string>(allClaims, StringComparer.OrdinalIgnoreCase);
        }

        return null;
    }

    /// <summary>
    /// Checks if the required permission exists in the permission list,
    /// including wildcard matching at any depth.
    /// Sensitive permissions (RequiresExplicitGrant) cannot be matched by wildcards.
    /// Examples: "<module>.*" matches "<module>.read", but NOT "<module>.sensitive.view" (if sensitive)
    /// </summary>
    private static bool HasPermissionInList(HashSet<string> permissions, string permission)
    {
        // Direct match always works (even for sensitive permissions)
        if (permissions.Contains(permission))
        {
            return true;
        }

        // Sensitive permissions require explicit grant — no wildcard matching
        if (PermissionRegistry.Instance.RequiresExplicitGrant(permission))
        {
            return false;
        }

        // Check for full admin wildcard
        if (permissions.Contains("*"))
        {
            return true;
        }

        // Check for wildcard permissions at each level
        // For "<module>.a.b.c", check: "<module>.*", "<module>.a.*", "<module>.a.b.*"
        var segments = permission.Split('.');
        var prefix = "";
        for (int i = 0; i < segments.Length - 1; i++)
        {
            prefix = i == 0 ? segments[i] : $"{prefix}.{segments[i]}";
            if (permissions.Contains($"{prefix}.*"))
            {
                return true;
            }
        }

        return false;
    }

    /// <summary>
    /// Falls back to database lookup for permission checking.
    /// Used when token doesn't contain permissions claim (backward compatibility).
    /// </summary>
    /// <summary>
    /// True when the request is still scoped to the tenant the token was issued
    /// for, which is what makes the token's baked-in permissions trustworthy.
    /// A request with no resolved tenant (platform-level operations) keeps the
    /// fast path — there is no other tenant to be wrong about.
    /// </summary>
    private bool TokenMatchesCurrentTenant(System.Security.Claims.ClaimsPrincipal user)
    {
        var resolvedTenantId = _tenantContext.TenantId;
        if (string.IsNullOrEmpty(resolvedTenantId))
        {
            return true;
        }

        var tokenTenantId = user.FindFirst("tenant_id")?.Value;
        if (string.IsNullOrEmpty(tokenTenantId))
        {
            return true;
        }

        if (string.Equals(tokenTenantId, resolvedTenantId, StringComparison.OrdinalIgnoreCase))
        {
            return true;
        }

        _logger.LogDebug(
            "Token was minted for tenant {TokenTenant} but request resolved to {ResolvedTenant} — " +
            "ignoring token permissions and resolving from the database",
            tokenTenantId, resolvedTenantId);
        return false;
    }

    private async Task<bool> HasPermissionFromDatabaseAsync(
        System.Security.Claims.ClaimsPrincipal user,
        string permission)
    {
        var userId = user.FindFirst("sub")?.Value
            ?? user.FindFirst(System.Security.Claims.ClaimTypes.NameIdentifier)?.Value;

        if (string.IsNullOrEmpty(userId))
        {
            _logger.LogDebug("Permission check failed: no subject on the principal");
            return false;
        }

        // Resolve against the tenant the request actually landed in, and read role
        // ROWS rather than the token's role names. Names are ambiguous — every
        // tenant has its own "Admin" row — so resolving a name from a token minted
        // elsewhere would grant this tenant's Admin permissions to someone who was
        // only ever an Admin somewhere else.
        var tenantId = _tenantContext.TenantId ?? user.FindFirst("tenant_id")?.Value;

        // Roles held in this tenant plus the organization-admin grant derived
        // from organization membership.
        var permissions = await _effectiveRoles.GetEffectivePermissionsAsync(userId, tenantId);
        if (HasPermissionInList(new HashSet<string>(permissions, StringComparer.OrdinalIgnoreCase), permission))
        {
            _logger.LogDebug(
                "Permission {Permission} granted in tenant {TenantId} (database fallback)",
                permission, tenantId ?? "(none)");
            return true;
        }

        _logger.LogDebug(
            "Permission {Permission} denied: not found in any role (database fallback)",
            permission);
        return false;
    }

    // Role claim types to check (JWT middleware may remap claim types)
    private static readonly string[] RoleClaimTypes =
    {
        "role",
        "roles",
        System.Security.Claims.ClaimTypes.Role, // http://schemas.microsoft.com/ws/2008/06/identity/claims/role
    };

    /// <summary>
    /// Checks if user has SuperAdmin role.
    /// SECURITY: This checks role claims from the token. Since permissions are
    /// never in tokens (only in database), a fake role claim can't grant permissions.
    /// The SuperAdmin bypass is safe because it's checked before permission lookup,
    /// and any actual permission grants still require database-verified roles.
    /// </summary>
    private bool IsSuperAdmin(System.Security.Claims.ClaimsPrincipal user)
    {
        // First check for super_admin claim (most authoritative)
        var superAdminClaim = user.FindFirst("super_admin")?.Value;
        if (superAdminClaim is "true" or "1")
        {
            _logger.LogDebug("User is SuperAdmin via super_admin claim");
            return true;
        }

        // Check for SuperAdmin role claim across multiple claim types
        // (JWT middleware may remap "role" to Microsoft schema URI)
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

        _logger.LogDebug("Found roles: [{Roles}]", string.Join(", ", roles));

        return roles.Any(r => r.Equals("SuperAdmin", StringComparison.OrdinalIgnoreCase) ||
                              r.Equals("SystemAdmin", StringComparison.OrdinalIgnoreCase));
    }
}
