using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Oluso.Admin.Authorization;
using Oluso.Core.Authorization;
using Oluso.Core.Api;
using Oluso.Core.Domain.Entities;
using Oluso.Core.Domain.Interfaces;
using Oluso.Core.Services;
using Oluso.Core.Licensing;
using Oluso.Admin.Dto.Permissions;

namespace Oluso.Admin.Controllers;

/// <summary>
/// API for retrieving available permissions.
/// This endpoint allows the frontend to dynamically fetch the list of permissions
/// for display in role management UI.
/// </summary>
[Route("api/admin/permissions")]
public class PermissionsController : AdminBaseController
{
    private readonly IRoleStore _roleStore;
    private readonly IEffectiveRoleResolver _effectiveRoles;
    private readonly IFeatureGate _featureGate;
    private readonly ILogger<PermissionsController> _logger;

    public PermissionsController(
        ITenantContext tenantContext,
        IRoleStore roleStore,
        IEffectiveRoleResolver effectiveRoles,
        IFeatureGate featureGate,
        ILogger<PermissionsController> logger)
        : base(tenantContext)
    {
        _roleStore = roleStore;
        _effectiveRoles = effectiveRoles;
        _featureGate = featureGate;
        _logger = logger;
    }

    /// <summary>
    /// Get all available permissions that can be assigned to roles.
    /// Returns permissions grouped by category with metadata.
    ///
    /// Filtering rules:
    /// - SuperAdmin-only permissions are hidden from non-SuperAdmins
    /// - Module categories are NEVER shown in the main
    ///   roles page - they are managed via each module's dedicated roles page
    /// - Organizations and Tenants categories are restricted to OrgAdmin+
    /// </summary>
    [HttpGet]
    [RequirePermission(AdminPermissions.RolesRead)]
    public ActionResult<PermissionsResponse> GetPermissions()
    {
        var isSuperAdmin = IsSuperAdmin;

        // Read every permission from the canonical registry. Core
        // permissions are backfilled into the registry at Admin startup
        // (see OlusoAdminExtensions.AddOlusoAdmin), so this single source
        // returns core + enterprise + module entries. Filter to FeatureKey
        // == null to keep only platform-level entries (core + enterprise
        // features). Module permissions carry their
        // module's FeatureKey and are managed via each module's dedicated
        // roles page.
        var corePermissions = PermissionRegistry.Instance.GetPluginPermissions()
            .Where(p => p.FeatureKey == null)
            .ToList();

        // Filter out SuperAdmin-only permissions for non-SuperAdmin users
        var filteredPermissions = isSuperAdmin
            ? corePermissions
            : corePermissions.Where(p => !p.RequiresSuperAdmin).ToList();

        if (!isSuperAdmin)
        {
            // Org-level categories require OrgAdmin or higher
            var hiddenCategories = new HashSet<string>();
            if (!IsCurrentOrgAdmin)
            {
                hiddenCategories.Add(AdminPermissions.Categories.Organizations);
                hiddenCategories.Add(AdminPermissions.Categories.Tenants);
            }

            filteredPermissions = filteredPermissions.Where(p =>
            {
                // Block org-level categories for non-OrgAdmin
                if (hiddenCategories.Contains(p.Category))
                    return false;

                return true;
            }).ToList();
        }

        var categories = filteredPermissions
            .GroupBy(p => p.Category)
            .OrderBy(g => GetCategoryOrder(g.Key))
            .Select(g => new PermissionCategoryDto
            {
                Name = g.Key,
                Permissions = g.Select(p => new PermissionDto
                {
                    Name = p.Name,
                    DisplayName = p.DisplayName,
                    Description = p.Description,
                    RequiresSuperAdmin = p.RequiresSuperAdmin
                }).ToList()
            })
            .ToList();

        return Ok(new PermissionsResponse
        {
            Categories = categories,
            TotalCount = filteredPermissions.Count
        });
    }

    /// <summary>
    /// Get a flat list of all permission names (for simple use cases).
    /// Applies the same filtering as GetPermissions (module gating, org-level restriction).
    /// </summary>
    [HttpGet("list")]
    [RequirePermission(AdminPermissions.RolesRead)]
    public ActionResult<IEnumerable<string>> GetPermissionsList()
    {
        // Delegate to GetPermissions to ensure consistent filtering
        var result = GetPermissions();
        var response = (result.Result as OkObjectResult)?.Value as PermissionsResponse;
        if (response == null) return Ok(Enumerable.Empty<string>());

        return Ok(response.Categories.SelectMany(c => c.Permissions.Select(p => p.Name)));
    }

    /// <summary>
    /// Get permissions for a specific role
    /// </summary>
    [HttpGet("role/{roleId}")]
    [RequirePermission(AdminPermissions.RolesRead)]
    public async Task<ActionResult<IEnumerable<string>>> GetRolePermissions(
        string roleId,
        CancellationToken cancellationToken)
    {
        var role = await _roleStore.GetByIdAsync(roleId, cancellationToken);
        if (role == null)
        {
            return NotFound(new { error = "Role not found" });
        }

        // Check tenant access
        if (role.TenantId != null && role.TenantId != TenantId && !IsSuperAdmin)
        {
            return Forbid();
        }

        return Ok(role.GetPermissions());
    }

    /// <summary>
    /// Check if the current user has a specific permission
    /// </summary>
    [HttpGet("check/{permission}")]
    public async Task<ActionResult<PermissionCheckResult>> CheckPermission(
        string permission,
        CancellationToken cancellationToken)
    {
        var explicitOnly = PermissionRegistry.Instance.RequiresExplicitGrant(permission);

        // SuperAdmins hold every permission except those granted by name only
        if (IsSuperAdmin && !explicitOnly)
        {
            return Ok(new PermissionCheckResult
            {
                Permission = permission,
                HasPermission = true,
                GrantedBy = "SuperAdmin role"
            });
        }

        // Check the roles the user holds in this tenant, resolved by id (see GetMyPermissions).
        var userId = AdminUserId;
        var heldRoles = string.IsNullOrEmpty(userId)
            ? Array.Empty<OlusoRole>()
            : (await _effectiveRoles.GetEffectiveRolesAsync(userId, TenantId, cancellationToken)).ToArray();

        foreach (var role in heldRoles)
        {
            var roleName = role.Name ?? role.Id;
            {
                // Get permission claims for this role
                var claims = await _roleStore.GetRoleClaimsAsync(role.Id, cancellationToken);
                var permissions = claims
                    .Where(c => c.Type == OlusoRole.PermissionClaimType)
                    .Select(c => c.Value)
                    .ToList();

                // Check exact match
                if (permissions.Contains(permission, StringComparer.OrdinalIgnoreCase))
                {
                    return Ok(new PermissionCheckResult
                    {
                        Permission = permission,
                        HasPermission = true,
                        GrantedBy = $"Role: {roleName}"
                    });
                }

                if (explicitOnly)
                {
                    continue;
                }

                // Check wildcard
                var category = permission.Split('.').FirstOrDefault();
                if (category != null && permissions.Contains($"{category}.*", StringComparer.OrdinalIgnoreCase))
                {
                    return Ok(new PermissionCheckResult
                    {
                        Permission = permission,
                        HasPermission = true,
                        GrantedBy = $"Role: {roleName} (wildcard {category}.*)"
                    });
                }

                // Check full wildcard
                if (permissions.Contains("*", StringComparer.OrdinalIgnoreCase))
                {
                    return Ok(new PermissionCheckResult
                    {
                        Permission = permission,
                        HasPermission = true,
                        GrantedBy = $"Role: {roleName} (full admin access)"
                    });
                }
            }
        }

        return Ok(new PermissionCheckResult
        {
            Permission = permission,
            HasPermission = false,
            GrantedBy = null
        });
    }

    /// <summary>
    /// Get the current user's effective permissions.
    /// Permissions for disabled features are excluded.
    /// </summary>
    [HttpGet("my")]
    public async Task<ActionResult<MyPermissionsResponse>> GetMyPermissions(CancellationToken cancellationToken)
    {
        var effectivePermissions = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var rolePermissions = new Dictionary<string, List<string>>();

        // Build the set of disabled feature keys for this tenant
        var disabledFeatures = await GetDisabledFeaturesAsync(cancellationToken);

        // Helper: is this permission allowed given feature availability?
        bool IsPermissionAllowed(PermissionDefinition p) =>
            p.FeatureKey == null || !disabledFeatures.Contains(p.FeatureKey);

        // Collect all permissions that require explicit grant (feature-filtered)
        var explicitGrant = AdminPermissions.GetAll()
            .Where(p => (p.RequiresExplicitGrant || p.RequiresSuperAdmin) && IsPermissionAllowed(p))
            .Select(p => p.Name)
            .OrderBy(p => p)
            .ToList();

        // SuperAdmins get the * wildcard, which expands to every permission
        // EXCEPT those flagged RequiresExplicitGrant. Explicit-grant permissions
        // (sensitive data access, module administration, etc.) still require a literal
        // grant on the SuperAdmin role — same rule the runtime check applies,
        // so the nav menu and route guards stay consistent with the API gates.
        if (IsSuperAdmin)
        {
            foreach (var perm in AdminPermissions.GetAll())
            {
                if (!IsPermissionAllowed(perm)) continue;
                if (perm.RequiresExplicitGrant) continue;
                effectivePermissions.Add(perm.Name);
            }

            return Ok(new MyPermissionsResponse
            {
                EffectivePermissions = effectivePermissions.OrderBy(p => p).ToList(),
                RolePermissions = new Dictionary<string, List<string>>
                {
                    { "SuperAdmin", new List<string> { "*" } }
                },
                IsSuperAdmin = true,
                ExplicitGrantPermissions = explicitGrant
            });
        }

        // Build a lookup of permission name → definition for feature checking
        var allPermissions = AdminPermissions.GetAll();
        var permissionLookup = allPermissions.ToDictionary(p => p.Name, StringComparer.OrdinalIgnoreCase);

        // Build lookup of module role names → feature keys so we can skip
        // entire roles whose module feature is disabled for this tenant.
        var moduleRoleFeatures = PermissionRegistry.Instance.GetModuleRoles()
            .Where(r => r.FeatureKey != null)
            .ToDictionary(r => r.Name, r => r.FeatureKey!, StringComparer.OrdinalIgnoreCase);

        // Build lookup of permission categories → feature keys for wildcard filtering.
        // (a permission's category — its name prefix — maps to its module feature key)
        var categoryFeatureKeys = allPermissions
            .Where(p => p.FeatureKey != null)
            .GroupBy(p => p.Name.Split('.')[0], StringComparer.OrdinalIgnoreCase)
            .ToDictionary(g => g.Key, g => g.First().FeatureKey!, StringComparer.OrdinalIgnoreCase);

        // Resolve the role ROWS the user holds in this tenant (tenant rows plus
        // platform-wide rows), keyed by id, the same way token minting and the
        // API authorization handler do. Names are not a safe key: a tenant row
        // can share its name with a platform module role, and picking the wrong
        // one silently drops explicit-grant permissions such as module
        // administration, hiding menus while the APIs still work.
        var userId = AdminUserId;
        if (string.IsNullOrEmpty(userId))
        {
            _logger.LogWarning("GetMyPermissions: No subject in token; returning no permissions");
            return Ok(new MyPermissionsResponse
            {
                EffectivePermissions = new List<string>(),
                RolePermissions = rolePermissions,
                IsSuperAdmin = false,
                ExplicitGrantPermissions = explicitGrant
            });
        }

        // Roles held in this tenant plus the organization-admin grant from membership.
        var heldRoles = (await _effectiveRoles.GetEffectiveRolesAsync(userId, TenantId, cancellationToken)).ToList();
        _logger.LogDebug("GetMyPermissions: TenantId={TenantId}, User={UserId}, Roles=[{Roles}]",
            TenantId, userId, string.Join(", ", heldRoles.Select(r => $"{r.Name}:{(r.TenantId == null ? "platform" : "tenant")}")));

        if (heldRoles.Count == 0)
        {
            _logger.LogWarning("GetMyPermissions: User {UserId} holds no roles in tenant {TenantId}", userId, TenantId);
        }

        foreach (var role in heldRoles)
        {
            var roleName = role.Name ?? role.Id;

            // Skip module roles whose feature is disabled
            if (moduleRoleFeatures.TryGetValue(roleName, out var roleFeatureKey)
                && disabledFeatures.Contains(roleFeatureKey))
            {
                _logger.LogDebug("GetMyPermissions: Skipping role '{RoleName}' — feature '{Feature}' is disabled",
                    roleName, roleFeatureKey);
                continue;
            }

            {
                // Get permission claims for this role
                var claims = await _roleStore.GetRoleClaimsAsync(role.Id, cancellationToken);
                var permissions = claims
                    .Where(c => c.Type == OlusoRole.PermissionClaimType)
                    .Select(c => c.Value)
                    .ToList();

                if (rolePermissions.TryGetValue(roleName, out var already))
                    rolePermissions[roleName] = already.Union(permissions, StringComparer.OrdinalIgnoreCase).ToList();
                else
                    rolePermissions[roleName] = permissions;

                foreach (var permission in permissions)
                {
                    if (permission == "*")
                    {
                        // Expand wildcard into individual permissions.
                        // Do NOT return the literal "*" — the frontend would bypass
                        // feature-based filtering since it treats "*" as "has everything".
                        foreach (var perm in allPermissions)
                        {
                            if (!perm.RequiresSuperAdmin && !perm.RequiresExplicitGrant && IsPermissionAllowed(perm))
                            {
                                effectivePermissions.Add(perm.Name);
                            }
                        }
                    }
                    else if (permission.EndsWith(".*"))
                    {
                        var category = permission[..^2];
                        // Skip entire category if its feature is disabled
                        if (categoryFeatureKeys.TryGetValue(category, out var catFeature)
                            && disabledFeatures.Contains(catFeature))
                        {
                            continue;
                        }
                        // Expand category wildcard into individual permissions.
                        // Do NOT return the literal "category.*" for the same reason as above.
                        foreach (var perm in allPermissions.Where(p =>
                            p.Name.StartsWith($"{category}.") && !p.RequiresExplicitGrant && IsPermissionAllowed(p)))
                        {
                            effectivePermissions.Add(perm.Name);
                        }
                    }
                    else
                    {
                        // Only add if the permission's feature is enabled
                        if (permissionLookup.TryGetValue(permission, out var def))
                        {
                            if (IsPermissionAllowed(def))
                                effectivePermissions.Add(permission);
                        }
                        else
                        {
                            // Unknown permission (custom/legacy) — allow through
                            effectivePermissions.Add(permission);
                        }
                    }
                }
            }
        }

        return Ok(new MyPermissionsResponse
        {
            EffectivePermissions = effectivePermissions.OrderBy(p => p).ToList(),
            RolePermissions = rolePermissions,
            IsSuperAdmin = false,
            ExplicitGrantPermissions = explicitGrant
        });
    }

    /// <summary>
    /// Checks all registered module feature keys and returns the set of disabled ones.
    /// </summary>
    private async Task<HashSet<string>> GetDisabledFeaturesAsync(CancellationToken cancellationToken)
    {
        var disabled = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var featureKeys = PermissionRegistry.Instance.GetFeatureKeys();

        foreach (var key in featureKeys)
        {
            var result = await _featureGate.CheckFeatureAsync(key, cancellationToken: cancellationToken);
            if (!result.IsAllowed)
            {
                disabled.Add(key);
            }
        }

        return disabled;
    }

    private static int GetCategoryOrder(string category)
    {
        return category switch
        {
            AdminPermissions.Categories.Dashboard => 0,
            AdminPermissions.Categories.Users => 1,
            AdminPermissions.Categories.Roles => 2,
            AdminPermissions.Categories.Clients => 3,
            AdminPermissions.Categories.Resources => 4,
            AdminPermissions.Categories.Scopes => 5,
            AdminPermissions.Categories.IdentityResources => 6,
            AdminPermissions.Categories.IdentityProviders => 7,
            AdminPermissions.Categories.Grants => 8,
            AdminPermissions.Categories.Sessions => 9,
            AdminPermissions.Categories.SigningKeys => 10,
            AdminPermissions.Categories.Journeys => 11,
            AdminPermissions.Categories.Webhooks => 12,
            AdminPermissions.Categories.AuditLogs => 13,
            AdminPermissions.Categories.Settings => 14,
            AdminPermissions.Categories.Tenants => 15,
            _ => 100
        };
    }
}
