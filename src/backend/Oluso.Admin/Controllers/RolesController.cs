using Microsoft.AspNetCore.Mvc;
using Oluso.Admin.Authorization;
using Oluso.Core.Api;
using Oluso.Core.Domain.Entities;
using Oluso.Core.Domain.Interfaces;
using Oluso.Core.Events;
using Oluso.Core.Services;

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
    private readonly ILogger<RolesController> _logger;

    public RolesController(
        ITenantContext tenantContext,
        IRoleStore roleStore,
        IOlusoUserService userService,
        IOlusoEventService eventService,
        ILogger<RolesController> logger)
        : base(tenantContext)
    {
        _roleStore = roleStore;
        _userService = userService;
        _eventService = eventService;
        _logger = logger;
    }

    /// <summary>
    /// Get all roles for the current tenant (includes global/system roles)
    /// </summary>
    /// <remarks>
    /// System roles (SuperAdmin, SystemAdmin) are only visible to SuperAdmin users.
    /// Tenant admins only see tenant-scoped roles and non-system global roles.
    /// </remarks>
    [HttpGet]
    public async Task<ActionResult<IEnumerable<RoleDto>>> GetRoles(
        [FromQuery] bool includeSystem = true,
        CancellationToken cancellationToken = default)
    {
        var roles = await _roleStore.GetRolesAsync(TenantId, includeSystem, cancellationToken);

        // Filter out system-level admin roles for non-SuperAdmin users
        // These roles should not be visible or assignable by tenant admins
        if (!IsSuperAdmin)
        {
            roles = roles.Where(r => !ReservedClaimTypes.IsReservedRoleName(r.Name)).ToList();
        }

        var result = roles.Select(r => new RoleDto
        {
            Id = r.Id,
            Name = r.Name,
            DisplayName = r.DisplayName ?? r.Name,
            Description = r.Description,
            IsSystemRole = r.IsSystemRole,
            TenantId = r.TenantId,
            IsGlobal = r.TenantId == null,
            Permissions = r.Permissions?.Split(',', StringSplitOptions.RemoveEmptyEntries).ToList() ?? new List<string>(),
            CreatedAt = r.CreatedAt,
            UpdatedAt = r.UpdatedAt
        });

        return Ok(result);
    }

    /// <summary>
    /// Get a specific role by ID
    /// </summary>
    [HttpGet("{roleId}")]
    public async Task<ActionResult<RoleDetailDto>> GetRole(string roleId, CancellationToken cancellationToken)
    {
        var role = await _roleStore.GetByIdAsync(roleId, cancellationToken);

        if (role == null)
        {
            return NotFound();
        }

        // Check tenant access
        if (role.TenantId != null && role.TenantId != TenantId)
        {
            return Forbid();
        }

        var claims = await _roleStore.GetRoleClaimsAsync(roleId, cancellationToken);

        return Ok(new RoleDetailDto
        {
            Id = role.Id,
            Name = role.Name,
            DisplayName = role.DisplayName ?? role.Name,
            Description = role.Description,
            IsSystemRole = role.IsSystemRole,
            TenantId = role.TenantId,
            IsGlobal = role.TenantId == null,
            Permissions = role.Permissions?.Split(',', StringSplitOptions.RemoveEmptyEntries).ToList() ?? new List<string>(),
            Claims = claims.Select(c => new RoleClaimDto
            {
                Type = c.Type,
                Value = c.Value
            }).ToList(),
            CreatedAt = role.CreatedAt,
            UpdatedAt = role.UpdatedAt
        });
    }

    /// <summary>
    /// Create a new role for the current tenant
    /// </summary>
    [HttpPost]
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

        // Validate claims don't contain reserved claim types
        if (request.Claims?.Any() == true)
        {
            var reservedClaims = ReservedClaimTypes.GetReservedClaimTypes(
                request.Claims.Select(c => (c.Type, c.Value))).ToList();

            if (reservedClaims.Any())
            {
                _logger.LogWarning(
                    "Tenant {TenantId} attempted to create role with reserved claim types: {ClaimTypes}",
                    TenantId, string.Join(", ", reservedClaims));
                return BadRequest(new { error = $"The following claim types are reserved for system use: {string.Join(", ", reservedClaims)}" });
            }
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
            IsSystemRole = false, // Tenant-created roles are never system roles
            Permissions = request.Permissions != null ? string.Join(",", request.Permissions) : null
        };

        var created = await _roleStore.CreateAsync(role, cancellationToken);

        // Add claims if provided
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
            Permissions = role.Permissions?.Split(',', StringSplitOptions.RemoveEmptyEntries).ToList() ?? new List<string>(),
            CreatedAt = role.CreatedAt
        });
    }

    /// <summary>
    /// Update an existing role
    /// </summary>
    [HttpPut("{roleId}")]
    public async Task<ActionResult<RoleDto>> UpdateRole(
        string roleId,
        [FromBody] UpdateRoleRequest request,
        CancellationToken cancellationToken)
    {
        var role = await _roleStore.GetByIdAsync(roleId, cancellationToken);

        if (role == null)
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

        // Update other fields
        if (request.DisplayName != null)
            role.DisplayName = request.DisplayName;

        if (request.Description != null)
            role.Description = request.Description;

        if (request.Permissions != null)
            role.Permissions = string.Join(",", request.Permissions);

        role.UpdatedAt = DateTime.UtcNow;

        var updated = await _roleStore.UpdateAsync(role, cancellationToken);

        // Update claims if provided
        if (request.Claims != null)
        {
            // Validate claims don't contain reserved claim types
            var reservedClaims = ReservedClaimTypes.GetReservedClaimTypes(
                request.Claims.Select(c => (c.Type, c.Value))).ToList();

            if (reservedClaims.Any())
            {
                _logger.LogWarning(
                    "Tenant {TenantId} attempted to update role with reserved claim types: {ClaimTypes}",
                    TenantId, string.Join(", ", reservedClaims));
                return BadRequest(new { error = $"The following claim types are reserved for system use: {string.Join(", ", reservedClaims)}" });
            }

            var existingClaims = await _roleStore.GetRoleClaimsAsync(roleId, cancellationToken);
            foreach (var claim in existingClaims)
            {
                await _roleStore.RemoveRoleClaimAsync(roleId, claim.Type, claim.Value, cancellationToken);
            }

            foreach (var claim in request.Claims)
            {
                await _roleStore.AddRoleClaimAsync(roleId, claim.Type, claim.Value, cancellationToken);
            }
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
            RoleName = role.Name
        }, cancellationToken);

        return Ok(new RoleDto
        {
            Id = role.Id,
            Name = role.Name,
            DisplayName = role.DisplayName ?? role.Name,
            Description = role.Description,
            IsSystemRole = role.IsSystemRole,
            TenantId = role.TenantId,
            IsGlobal = role.TenantId == null,
            Permissions = role.Permissions?.Split(',', StringSplitOptions.RemoveEmptyEntries).ToList() ?? new List<string>(),
            CreatedAt = role.CreatedAt,
            UpdatedAt = role.UpdatedAt
        });
    }

    /// <summary>
    /// Delete a role
    /// </summary>
    [HttpDelete("{roleId}")]
    public async Task<IActionResult> DeleteRole(string roleId, CancellationToken cancellationToken)
    {
        var role = await _roleStore.GetByIdAsync(roleId, cancellationToken);

        if (role == null)
        {
            return NotFound();
        }

        // Check tenant access - can only delete own roles
        if (role.TenantId != TenantId)
        {
            return Forbid();
        }

        // System roles cannot be deleted
        if (role.IsSystemRole)
        {
            return BadRequest(new { error = "System roles cannot be deleted" });
        }

        // Check if any users are assigned to this role
        var usersInRole = await _roleStore.GetUsersInRoleAsync(roleId, cancellationToken);

        if (usersInRole > 0)
        {
            return BadRequest(new { error = $"Cannot delete role. {usersInRole} user(s) are assigned to this role." });
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
            RoleName = role.Name
        }, cancellationToken);

        return NoContent();
    }

    /// <summary>
    /// Get users assigned to a role
    /// </summary>
    [HttpGet("{roleId}/users")]
    public async Task<ActionResult<IEnumerable<RoleUserDto>>> GetRoleUsers(
        string roleId,
        CancellationToken cancellationToken)
    {
        var role = await _roleStore.GetByIdAsync(roleId, cancellationToken);

        if (role == null)
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
}

#region DTOs

public class RoleDto
{
    public string Id { get; set; } = null!;
    public string Name { get; set; } = null!;
    public string DisplayName { get; set; } = null!;
    public string? Description { get; set; }
    public bool IsSystemRole { get; set; }
    public string? TenantId { get; set; }
    public bool IsGlobal { get; set; }
    public List<string> Permissions { get; set; } = new();
    public DateTime CreatedAt { get; set; }
    public DateTime? UpdatedAt { get; set; }
}

public class RoleDetailDto : RoleDto
{
    public List<RoleClaimDto> Claims { get; set; } = new();
}

public class RoleClaimDto
{
    public string Type { get; set; } = null!;
    public string Value { get; set; } = null!;
}

public class CreateRoleRequest
{
    public string Name { get; set; } = null!;
    public string? DisplayName { get; set; }
    public string? Description { get; set; }
    public List<string>? Permissions { get; set; }
    public List<RoleClaimDto>? Claims { get; set; }
}

public class UpdateRoleRequest
{
    public string? Name { get; set; }
    public string? DisplayName { get; set; }
    public string? Description { get; set; }
    public List<string>? Permissions { get; set; }
    public List<RoleClaimDto>? Claims { get; set; }
}

public class RoleUserDto
{
    public string Id { get; set; } = null!;
    public string UserName { get; set; } = null!;
    public string Email { get; set; } = null!;
    public string? DisplayName { get; set; }
}

#endregion
