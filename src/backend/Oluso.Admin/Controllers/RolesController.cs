using Microsoft.AspNetCore.Mvc;
using Oluso.Admin.Authorization;
using Oluso.Core.Authorization;
using Oluso.Core.Api;
using Oluso.Core.Domain.Entities;
using Oluso.Core.Domain.Interfaces;
using Oluso.Core.Events;
using Oluso.Core.Licensing;
using Oluso.Core.Services;
using Oluso.Admin.Dto.Roles;
using Oluso.Admin.Services;

namespace Oluso.Admin.Controllers;

/// <summary>
/// Admin API for managing roles within a tenant
/// </summary>
[Route("api/admin/roles")]
public class RolesController : AdminBaseController
{
    private readonly IRoleStore _roleStore;
    private readonly IOlusoUserService _userService;
    private readonly IOlusoEventService _eventService;
    private readonly IFeatureGate _featureGate;
    private readonly IEntitlementStore _entitlementStore;
    private readonly IEntitlementScopeProvisioner _entitlementScopeProvisioner;
    private readonly ILogger<RolesController> _logger;

    public RolesController(
        ITenantContext tenantContext,
        IRoleStore roleStore,
        IOlusoUserService userService,
        IOlusoEventService eventService,
        IFeatureGate featureGate,
        IEntitlementStore entitlementStore,
        IEntitlementScopeProvisioner entitlementScopeProvisioner,
        ILogger<RolesController> logger)
        : base(tenantContext)
    {
        _roleStore = roleStore;
        _userService = userService;
        _eventService = eventService;
        _featureGate = featureGate;
        _entitlementStore = entitlementStore;
        _entitlementScopeProvisioner = entitlementScopeProvisioner;
        _logger = logger;
    }

    /// <summary>
    /// Registry roles are re-created at every startup, so deleting their
    /// platform row would only last until the next deploy.
    /// </summary>
    private static bool IsPlatformSeededRole(string roleName) =>
        PermissionRegistry.Instance.GetModuleRoles().Any(r =>
            r.Name.Equals(roleName, StringComparison.OrdinalIgnoreCase));

    /// <summary>
    /// A role a module's administrator manages (ManagedByRole set to that module's admin role) belongs to that module's
    /// own roles page; this API neither lists nor reads, edits or deletes it.
    /// </summary>
    private static bool IsManagedByModule(OlusoRole role) => role.ManagedByRole is not (null or "System" or "SuperAdmin");

    /// <summary>
    /// Tenant-owned rows are deletable by the tenant. Platform-wide rows only
    /// by SuperAdmin, and never the reserved ones or those the registry seeds.
    /// </summary>
    private bool IsRoleDeletable(OlusoRole role)
    {
        if (role.TenantId == null)
        {
            return IsSuperAdmin
                && !ReservedClaimTypes.IsReservedRoleName(role.Name!)
                && !IsPlatformSeededRole(role.Name!);
        }

        return role.TenantId == TenantId && !role.IsSystemRole;
    }

    /// <summary>
    /// Get all roles for the current tenant (includes global/system roles)
    /// </summary>
    /// <remarks>
    /// System roles (SuperAdmin, SystemAdmin) are only visible to SuperAdmin users.
    /// Tenant admins only see tenant-scoped roles and non-system global roles.
    /// Module roles (HRAdmin, CRMAdmin, etc.) are hidden when the feature is disabled.
    /// </remarks>
    [HttpGet]
    [RequirePermission(AdminPermissions.RolesRead)]
    public async Task<ActionResult<IEnumerable<RoleDto>>> GetRoles(
        [FromQuery] bool includeSystem = true,
        CancellationToken cancellationToken = default)
    {
        var roles = await _roleStore.GetRolesAsync(TenantId, includeSystem, cancellationToken);

        // Filter out reserved roles based on caller's privilege level:
        // - SuperAdmin sees all roles
        // - OrgAdmin sees org + tenant-level reserved roles, but not platform roles
        // - TenantAdmin sees tenant-level reserved roles (TenantAdmin, Admin) but not platform or org roles
        // - Everyone else sees no reserved roles
        if (!IsSuperAdmin)
        {
            if (IsCurrentOrgAdmin)
            {
                // OrgAdmins see everything except platform-level roles
                roles = roles.Where(r => !ReservedClaimTypes.IsSuperAdminOnlyRole(r.Name!)).ToList();
            }
            else if (HasAnyRole("TenantAdmin", "Admin"))
            {
                // TenantAdmins see tenant-level reserved roles only
                roles = roles.Where(r =>
                    !ReservedClaimTypes.IsReservedRoleName(r.Name!)
                    || ReservedClaimTypes.IsTenantAdminAssignableRole(r.Name!)).ToList();
            }
            else
            {
                roles = roles.Where(r => !ReservedClaimTypes.IsReservedRoleName(r.Name!)).ToList();
            }
        }

        roles = roles.Where(r => !IsManagedByModule(r)).ToList();

        // Filter out module roles whose feature is disabled for this tenant
        var moduleRoles = PermissionRegistry.Instance.GetModuleRoles();
        var disabledModuleRoleNames = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var moduleRole in moduleRoles.Where(mr => mr.FeatureKey != null))
        {
            var featureResult = await _featureGate.CheckFeatureAsync(moduleRole.FeatureKey!, cancellationToken: cancellationToken);
            if (!featureResult.IsAllowed)
            {
                disabledModuleRoleNames.Add(moduleRole.Name);
            }
        }

        if (disabledModuleRoleNames.Count > 0)
        {
            roles = roles.Where(r => !disabledModuleRoleNames.Contains(r.Name!)).ToList();
        }

        // Build result with permissions from claims
        var result = new List<RoleDto>();
        foreach (var r in roles)
        {
            var claims = await _roleStore.GetRoleClaimsAsync(r.Id, cancellationToken);
            var permissions = claims
                .Where(c => c.Type == OlusoRole.PermissionClaimType)
                .Select(c => c.Value)
                .ToList();
            var entitlements = claims
                .Where(c => c.Type == OlusoRole.EntitlementClaimType)
                .Select(c => c.Value)
                .ToList();

            result.Add(new RoleDto
            {
                Id = r.Id,
                Name = r.Name!,
                DisplayName = r.DisplayName ?? r.Name!,
                Description = r.Description,
                IsSystemRole = r.IsSystemRole,
                TenantId = r.TenantId,
                IsGlobal = r.TenantId == null,
                IsDeletable = IsRoleDeletable(r),
                Permissions = permissions,
                Entitlements = entitlements,
                CreatedAt = r.CreatedAt,
                UpdatedAt = r.UpdatedAt
            });
        }

        return Ok(result);
    }

    /// <summary>
    /// Get a specific role by ID
    /// </summary>
    [HttpGet("{roleId}")]
    [RequirePermission(AdminPermissions.RolesRead)]
    public async Task<ActionResult<RoleDetailDto>> GetRole(string roleId, CancellationToken cancellationToken)
    {
        var role = await _roleStore.GetByIdAsync(roleId, cancellationToken);

        if (role == null || IsManagedByModule(role))
        {
            return NotFound();
        }

        // Check tenant access
        if (role.TenantId != null && role.TenantId != TenantId)
        {
            return Forbid();
        }

        var claims = await _roleStore.GetRoleClaimsAsync(roleId, cancellationToken);

        // Separate permissions from other claims
        var permissions = claims
            .Where(c => c.Type == OlusoRole.PermissionClaimType)
            .Select(c => c.Value)
            .ToList();

        var otherClaims = claims
            .Where(c => c.Type != OlusoRole.PermissionClaimType)
            .Select(c => new RoleClaimDto
            {
                Type = c.Type,
                Value = c.Value
            }).ToList();

        return Ok(new RoleDetailDto
        {
            Id = role.Id,
            Name = role.Name!,
            DisplayName = role.DisplayName ?? role.Name!,
            Description = role.Description,
            IsSystemRole = role.IsSystemRole,
            TenantId = role.TenantId,
            IsGlobal = role.TenantId == null,
            IsDeletable = IsRoleDeletable(role),
            Permissions = permissions,
            Entitlements = claims
                .Where(c => c.Type == OlusoRole.EntitlementClaimType)
                .Select(c => c.Value)
                .ToList(),
            Claims = otherClaims,
            CreatedAt = role.CreatedAt,
            UpdatedAt = role.UpdatedAt
        });
    }

    /// <summary>
    /// Create a new role for the current tenant
    /// </summary>
    [HttpPost]
    [RequirePermission(AdminPermissions.RolesWrite)]
    public async Task<ActionResult<RoleDto>> CreateRole(
        [FromBody] CreateRoleRequest request,
        CancellationToken cancellationToken)
    {
        // Block creation of reserved system role names at tenant level
        if (ReservedClaimTypes.IsReservedRoleName(request.Name))
        {
            _logger.LogWarning(
                "Tenant {TenantId} attempted to create reserved role name: {RoleName}",
                TenantId, request.Name);
            return BadRequest(new { error = $"The role name '{request.Name}' is reserved for system use and cannot be created at tenant level" });
        }

        // Validate claims don't contain protected claim types (reserved + "permission")
        if (request.Claims?.Any() == true)
        {
            var protectedClaims = ReservedClaimTypes.GetProtectedClaimTypes(
                request.Claims.Select(c => (c.Type, c.Value))).ToList();

            if (protectedClaims.Any())
            {
                _logger.LogWarning(
                    "Tenant {TenantId} attempted to create role with protected claim types: {ClaimTypes}",
                    TenantId, string.Join(", ", protectedClaims));
                return BadRequest(new { error = $"The following claim types are protected: {string.Join(", ", protectedClaims)}. Use the 'permissions' field for permission claims." });
            }
        }

        // Validate permissions are valid system-defined permissions
        var permissionValidationError = ValidatePermissions(request.Permissions);
        if (permissionValidationError != null)
        {
            return BadRequest(new { error = permissionValidationError });
        }

        // Check if role with same name already exists in tenant
        var existingRole = await _roleStore.GetByNameAsync(request.Name, TenantId, cancellationToken);

        if (existingRole != null)
        {
            return BadRequest(new { error = "A role with this name already exists" });
        }

        var role = new OlusoRole
        {
            Id = Guid.NewGuid().ToString(),
            Name = request.Name,
            DisplayName = request.DisplayName ?? request.Name,
            Description = request.Description,
            TenantId = TenantId, // Always scoped to current tenant
            IsSystemRole = false // Tenant-created roles are never system roles
        };

        var rawEntitlementError = ValidateNoRawEntitlementClaims(request.Claims);
        if (rawEntitlementError != null)
        {
            return BadRequest(new { error = rawEntitlementError });
        }

        var entitlementError = await ValidateEntitlementsAsync(request.Entitlements, cancellationToken);
        if (entitlementError != null)
        {
            return BadRequest(new { error = entitlementError });
        }

        var created = await _roleStore.CreateAsync(role, cancellationToken);

        await ApplyEntitlementsAsync(created.Id, request.Entitlements, existingClaims: null, cancellationToken);

        // Add permissions as claims
        var permissionsList = request.Permissions ?? new List<string>();
        foreach (var permission in permissionsList)
        {
            await _roleStore.AddRoleClaimAsync(created.Id, OlusoRole.PermissionClaimType, permission, cancellationToken);
        }

        // Add other claims if provided
        if (request.Claims?.Any() == true)
        {
            foreach (var claim in request.Claims)
            {
                await _roleStore.AddRoleClaimAsync(created.Id, claim.Type, claim.Value, cancellationToken);
            }
        }

        _logger.LogInformation("Created role {RoleName} for tenant {TenantId}", role.Name, TenantId);

        // Raise audit event
        await _eventService.RaiseAsync(new AdminRoleCreatedEvent
        {
            TenantId = TenantId,
            AdminUserId = AdminUserId!,
            AdminUserName = AdminUserName,
            IpAddress = ClientIp,
            ResourceId = role.Id,
            ResourceName = role.Name,
            RoleName = role.Name
        }, cancellationToken);

        return CreatedAtAction(nameof(GetRole), new { roleId = role.Id }, new RoleDto
        {
            Id = role.Id,
            Name = role.Name,
            DisplayName = role.DisplayName ?? role.Name,
            Description = role.Description,
            IsSystemRole = role.IsSystemRole,
            TenantId = role.TenantId,
            IsGlobal = role.TenantId == null,
            IsDeletable = IsRoleDeletable(role),
            Permissions = permissionsList,
            Entitlements = request.Entitlements?.Distinct().ToList() ?? new List<string>(),
            CreatedAt = role.CreatedAt
        });
    }

    /// <summary>
    /// Update an existing role
    /// </summary>
    [HttpPut("{roleId}")]
    [RequirePermission(AdminPermissions.RolesWrite)]
    public async Task<ActionResult<RoleDto>> UpdateRole(
        string roleId,
        [FromBody] RoleDetailDto request,
        CancellationToken cancellationToken)
    {
        var role = await _roleStore.GetByIdAsync(roleId, cancellationToken);

        if (role == null || IsManagedByModule(role))
        {
            return NotFound();
        }

        // Check tenant access - can only update own roles
        if (role.TenantId != TenantId)
        {
            return Forbid();
        }

        // System roles cannot be modified by tenant admins
        if (role.IsSystemRole)
        {
            return BadRequest(new { error = "System roles cannot be modified" });
        }

        // Everything is validated before the first write, so a rejected request
        // leaves the role untouched.
        // Validate permissions are valid system-defined permissions
        var permissionValidationError = ValidatePermissions(request.Permissions);
        if (permissionValidationError != null)
        {
            return BadRequest(new { error = permissionValidationError });
        }

        var rawEntitlementError = ValidateNoRawEntitlementClaims(request.Claims);
        if (rawEntitlementError != null)
        {
            return BadRequest(new { error = rawEntitlementError });
        }

        var entitlementError = await ValidateEntitlementsAsync(request.Entitlements, cancellationToken);
        if (entitlementError != null)
        {
            return BadRequest(new { error = entitlementError });
        }

        // Validate claims don't contain protected claim types (reserved + "permission")
        var protectedClaims = ReservedClaimTypes.GetProtectedClaimTypes(
            request.Claims.Select(c => (c.Type, c.Value))).ToList();

        if (protectedClaims.Any())
        {
            _logger.LogWarning(
                "Tenant {TenantId} attempted to update role with protected claim types: {ClaimTypes}",
                TenantId, string.Join(", ", protectedClaims));
            return BadRequest(new { error = $"The following claim types are protected: {string.Join(", ", protectedClaims)}. Use the 'permissions' field for permission claims." });
        }

        // Check for name conflict if name is being changed
        if (!string.IsNullOrEmpty(request.Name) && request.Name != role.Name)
        {
            // Block renaming to reserved system role names
            if (ReservedClaimTypes.IsReservedRoleName(request.Name))
            {
                _logger.LogWarning(
                    "Tenant {TenantId} attempted to rename role to reserved name: {RoleName}",
                    TenantId, request.Name);
                return BadRequest(new { error = $"The role name '{request.Name}' is reserved for system use" });
            }

            var existingRole = await _roleStore.GetByNameAsync(request.Name, TenantId, cancellationToken);
            if (existingRole != null && existingRole.Id != roleId)
            {
                return BadRequest(new { error = "A role with this name already exists" });
            }

            role.Name = request.Name;
        }

        role.DisplayName = request.DisplayName;
        role.Description = request.Description;

        role.UpdatedAt = DateTime.UtcNow;

        var updated = await _roleStore.UpdateAsync(role, cancellationToken);

        // Get existing claims
        var existingClaims = await _roleStore.GetRoleClaimsAsync(roleId, cancellationToken);

        await ApplyEntitlementsAsync(roleId, request.Entitlements, existingClaims, cancellationToken);

        // Remove old permission claims
        var existingPermissions = existingClaims.Where(c => c.Type == OlusoRole.PermissionClaimType).ToList();
        foreach (var perm in existingPermissions)
        {
            await _roleStore.RemoveRoleClaimAsync(roleId, perm.Type, perm.Value, cancellationToken);
        }

        // Add new permission claims
        foreach (var permission in request.Permissions)
        {
            await _roleStore.AddRoleClaimAsync(roleId, OlusoRole.PermissionClaimType, permission, cancellationToken);
        }

        var currentPermissions = request.Permissions.ToList();

        // Remove old non-permission claims. Entitlements are excluded: they have
        // their own field and were just reconciled above, so sweeping them here
        // would delete what ApplyEntitlementsAsync had written.
        var existingOtherClaims = existingClaims
            .Where(c => c.Type != OlusoRole.PermissionClaimType
                && c.Type != OlusoRole.EntitlementClaimType)
            .ToList();
        foreach (var claim in existingOtherClaims)
        {
            await _roleStore.RemoveRoleClaimAsync(roleId, claim.Type, claim.Value, cancellationToken);
        }

        // Add new claims
        foreach (var claim in request.Claims)
        {
            await _roleStore.AddRoleClaimAsync(roleId, claim.Type, claim.Value, cancellationToken);
        }

        _logger.LogInformation("Updated role {RoleId}", roleId);

        // Raise audit event
        await _eventService.RaiseAsync(new AdminRoleUpdatedEvent
        {
            TenantId = TenantId,
            AdminUserId = AdminUserId!,
            AdminUserName = AdminUserName,
            IpAddress = ClientIp,
            ResourceId = roleId,
            ResourceName = role.Name,
            RoleName = role.Name!
        }, cancellationToken);

        return Ok(new RoleDto
        {
            Id = role.Id,
            Name = role.Name!,
            DisplayName = role.DisplayName ?? role.Name!,
            Description = role.Description,
            IsSystemRole = role.IsSystemRole,
            TenantId = role.TenantId,
            IsGlobal = role.TenantId == null,
            IsDeletable = IsRoleDeletable(role),
            Permissions = currentPermissions,
            Entitlements = request.Entitlements?.Distinct().ToList() ?? new List<string>(),
            CreatedAt = role.CreatedAt,
            UpdatedAt = role.UpdatedAt
        });
    }

    /// <summary>
    /// Delete a role
    /// </summary>
    [HttpDelete("{roleId}")]
    [RequirePermission(AdminPermissions.RolesDelete)]
    public async Task<IActionResult> DeleteRole(string roleId, CancellationToken cancellationToken)
    {
        var role = await _roleStore.GetByIdAsync(roleId, cancellationToken);

        if (role == null || IsManagedByModule(role))
        {
            return NotFound();
        }

        if (role.TenantId == null)
        {
            // Platform-wide rows: SuperAdmin only, never the reserved ones
            // (SuperAdmin, OrgAdmin, ...) nor anything the registry re-seeds at startup.
            if (!IsSuperAdmin)
            {
                return Forbid();
            }

            if (ReservedClaimTypes.IsReservedRoleName(role.Name!) || IsPlatformSeededRole(role.Name!))
            {
                return BadRequest(new { error = $"'{role.Name}' is a platform role and cannot be deleted." });
            }
        }
        else
        {
            if (role.TenantId != TenantId)
            {
                return Forbid();
            }

            if (role.IsSystemRole)
            {
                return BadRequest(new { error = "System roles cannot be deleted" });
            }
        }

        var usersInRole = await _roleStore.GetUsersInRoleAsync(roleId, cancellationToken);

        if (usersInRole > 0)
        {
            return BadRequest(new { error = $"Cannot delete role: {usersInRole} user(s) are assigned to it. Remove them from the role first." });
        }

        await _roleStore.DeleteAsync(roleId, cancellationToken);

        _logger.LogInformation("Deleted role {RoleId}", roleId);

        // Raise audit event
        await _eventService.RaiseAsync(new AdminRoleDeletedEvent
        {
            TenantId = TenantId,
            AdminUserId = AdminUserId!,
            AdminUserName = AdminUserName,
            IpAddress = ClientIp,
            ResourceId = roleId,
            ResourceName = role.Name,
            RoleName = role.Name!
        }, cancellationToken);

        return NoContent();
    }

    /// <summary>
    /// Get users assigned to a role
    /// </summary>
    [HttpGet("{roleId}/users")]
    [RequirePermission(AdminPermissions.RolesRead)]
    public async Task<ActionResult<IEnumerable<RoleUserDto>>> GetRoleUsers(
        string roleId,
        CancellationToken cancellationToken)
    {
        var role = await _roleStore.GetByIdAsync(roleId, cancellationToken);

        if (role == null || IsManagedByModule(role))
        {
            return NotFound();
        }

        // Check tenant access
        if (role.TenantId != null && role.TenantId != TenantId)
        {
            return Forbid();
        }

        var users = await _roleStore.GetUsersByRoleAsync(roleId, cancellationToken);

        return Ok(users.Select(u => new RoleUserDto
        {
            Id = u.Id,
            UserName = u.UserName,
            Email = u.Email,
            DisplayName = u.DisplayName
        }));
    }

    /// <summary>
    /// Validates that all permissions in the list are valid system-defined permissions
    /// and that the caller is authorized to assign them.
    ///
    /// Enforcement rules:
    /// - Only core permissions can be assigned via the main roles endpoint
    /// - Module permissions must be assigned via each module's
    ///   dedicated roles endpoint (e.g., the module's own admin roles endpoint)
    /// - Unknown permission names are rejected
    /// - RequiresSuperAdmin permissions can only be assigned by SuperAdmin
    /// - Organization/Tenants category permissions are restricted to OrgAdmin+
    /// </summary>
    /// <summary>
    /// Rejects entitlement claims supplied through the generic claims field.
    /// They have a dedicated, catalog-validated field; allowing them here would
    /// be a way to attach an entitlement that isn't defined.
    /// </summary>
    private static string? ValidateNoRawEntitlementClaims(IEnumerable<RoleClaimDto>? claims)
    {
        return claims == null
            ? null
            : RolePermissionValidator.RawEntitlementClaimError(claims.Select(c => c.Type));
    }

    /// <summary>
    /// Checks every requested entitlement exists in the tenant's catalog. Keeps
    /// the picker authoritative: a name that isn't defined is a typo, and left
    /// unchecked it would become a claim the tenant's app never matches.
    /// </summary>
    private async Task<string?> ValidateEntitlementsAsync(
        ICollection<string>? entitlements,
        CancellationToken cancellationToken)
    {
        if (entitlements == null || entitlements.Count == 0)
        {
            return null;
        }

        var requested = entitlements
            .Where(e => !string.IsNullOrWhiteSpace(e))
            .Select(e => e.Trim())
            .Distinct(StringComparer.Ordinal)
            .ToList();

        var known = (await _entitlementStore.FindByNamesAsync(TenantId, requested, cancellationToken))
            .Select(e => e.Name)
            .ToHashSet(StringComparer.Ordinal);

        var unknown = requested.Where(e => !known.Contains(e)).ToList();

        return unknown.Count > 0
            ? $"The following entitlements are not defined for this tenant: {string.Join(", ", unknown)}. "
                + "Add them to the entitlement catalog first."
            : null;
    }

    /// <summary>
    /// Reconciles a role's entitlement claims to the requested set, and makes
    /// sure the scope that releases them exists — assigning an entitlement that
    /// no scope can deliver is the dead end this feature removes.
    /// </summary>
    private async Task ApplyEntitlementsAsync(
        string roleId,
        ICollection<string>? entitlements,
        IEnumerable<RoleClaim>? existingClaims,
        CancellationToken cancellationToken)
    {
        var desired = (entitlements ?? Array.Empty<string>())
            .Where(e => !string.IsNullOrWhiteSpace(e))
            .Select(e => e.Trim())
            .Distinct(StringComparer.Ordinal)
            .ToHashSet(StringComparer.Ordinal);

        var current = (existingClaims ?? Array.Empty<RoleClaim>())
            .Where(c => c.Type == OlusoRole.EntitlementClaimType)
            .Select(c => c.Value)
            .ToHashSet(StringComparer.Ordinal);

        foreach (var removed in current.Except(desired, StringComparer.Ordinal))
        {
            await _roleStore.RemoveRoleClaimAsync(
                roleId, OlusoRole.EntitlementClaimType, removed, cancellationToken);
        }

        foreach (var added in desired.Except(current, StringComparer.Ordinal))
        {
            await _roleStore.AddRoleClaimAsync(
                roleId, OlusoRole.EntitlementClaimType, added, cancellationToken);
        }

        if (desired.Count > 0)
        {
            await _entitlementScopeProvisioner.EnsureAsync(cancellationToken);
        }
    }

    private string? ValidatePermissions(ICollection<string>? permissions) =>
        RolePermissionValidator.Validate(permissions, new RolePermissionGrantor(IsSuperAdmin, IsCurrentOrgAdmin));
}
