using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Oluso.Admin.Authorization;
using Oluso.Core.Api;
using Oluso.Core.Domain.Interfaces;

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
    private readonly ILogger<PermissionsController> _logger;

    public PermissionsController(
        ITenantContext tenantContext,
        IRoleStore roleStore,
        ILogger<PermissionsController> logger)
        : base(tenantContext)
    {
        _roleStore = roleStore;
        _logger = logger;
    }

    /// <summary>
    /// Get all available permissions that can be assigned to roles.
    /// Returns permissions grouped by category with metadata.
    /// </summary>
    [HttpGet]
    public ActionResult<PermissionsResponse> GetPermissions()
    {
        var isSuperAdmin = IsSuperAdmin;
        var allPermissions = AdminPermissions.GetAll();

        // Filter out SuperAdmin-only permissions for non-SuperAdmin users
        var filteredPermissions = isSuperAdmin
            ? allPermissions
            : allPermissions.Where(p => !p.RequiresSuperAdmin).ToList();

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
    /// Get a flat list of all permission names (for simple use cases)
    /// </summary>
    [HttpGet("list")]
    public ActionResult<IEnumerable<string>> GetPermissionsList()
    {
        var isSuperAdmin = IsSuperAdmin;
        var allPermissions = AdminPermissions.GetAll();

        var names = isSuperAdmin
            ? allPermissions.Select(p => p.Name)
            : allPermissions.Where(p => !p.RequiresSuperAdmin).Select(p => p.Name);

        return Ok(names);
    }

    /// <summary>
    /// Get permissions for a specific role
    /// </summary>
    [HttpGet("role/{roleId}")]
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
        // SuperAdmins have all permissions
        if (IsSuperAdmin)
        {
            return Ok(new PermissionCheckResult
            {
                Permission = permission,
                HasPermission = true,
                GrantedBy = "SuperAdmin role"
            });
        }

        // Check user's roles for the permission
        var roles = GetAdminRoles();
        foreach (var roleName in roles)
        {
            var role = await _roleStore.GetByNameAsync(roleName, TenantId, cancellationToken);
            if (role == null)
            {
                role = await _roleStore.GetByNameAsync(roleName, null, cancellationToken);
            }

            if (role != null)
            {
                var permissions = role.GetPermissions();

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
    /// Get the current user's effective permissions
    /// </summary>
    [HttpGet("my")]
    public async Task<ActionResult<MyPermissionsResponse>> GetMyPermissions(CancellationToken cancellationToken)
    {
        var effectivePermissions = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var rolePermissions = new Dictionary<string, List<string>>();

        // SuperAdmins have all permissions
        if (IsSuperAdmin)
        {
            foreach (var perm in AdminPermissions.GetAllNames())
            {
                effectivePermissions.Add(perm);
            }

            return Ok(new MyPermissionsResponse
            {
                EffectivePermissions = effectivePermissions.OrderBy(p => p).ToList(),
                RolePermissions = new Dictionary<string, List<string>>
                {
                    { "SuperAdmin", new List<string> { "*" } }
                },
                IsSuperAdmin = true
            });
        }

        // Get permissions from each role
        var roles = GetAdminRoles();
        foreach (var roleName in roles)
        {
            var role = await _roleStore.GetByNameAsync(roleName, TenantId, cancellationToken);
            if (role == null)
            {
                role = await _roleStore.GetByNameAsync(roleName, null, cancellationToken);
            }

            if (role != null)
            {
                var permissions = role.GetPermissions().ToList();
                rolePermissions[roleName] = permissions;

                foreach (var permission in permissions)
                {
                    if (permission == "*")
                    {
                        // Full wildcard - add all permissions
                        foreach (var perm in AdminPermissions.GetAllNames())
                        {
                            effectivePermissions.Add(perm);
                        }
                    }
                    else if (permission.EndsWith(".*"))
                    {
                        // Category wildcard
                        var category = permission[..^2];
                        foreach (var perm in AdminPermissions.GetAllNames().Where(p => p.StartsWith($"{category}.")))
                        {
                            effectivePermissions.Add(perm);
                        }
                    }
                    else
                    {
                        effectivePermissions.Add(permission);
                    }
                }
            }
        }

        return Ok(new MyPermissionsResponse
        {
            EffectivePermissions = effectivePermissions.OrderBy(p => p).ToList(),
            RolePermissions = rolePermissions,
            IsSuperAdmin = false
        });
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

#region DTOs

public class PermissionsResponse
{
    public List<PermissionCategoryDto> Categories { get; set; } = new();
    public int TotalCount { get; set; }
}

public class PermissionCategoryDto
{
    public string Name { get; set; } = null!;
    public List<PermissionDto> Permissions { get; set; } = new();
}

public class PermissionDto
{
    public string Name { get; set; } = null!;
    public string DisplayName { get; set; } = null!;
    public string Description { get; set; } = null!;
    public bool RequiresSuperAdmin { get; set; }
}

public class PermissionCheckResult
{
    public string Permission { get; set; } = null!;
    public bool HasPermission { get; set; }
    public string? GrantedBy { get; set; }
}

public class MyPermissionsResponse
{
    public List<string> EffectivePermissions { get; set; } = new();
    public Dictionary<string, List<string>> RolePermissions { get; set; } = new();
    public bool IsSuperAdmin { get; set; }
}

#endregion
