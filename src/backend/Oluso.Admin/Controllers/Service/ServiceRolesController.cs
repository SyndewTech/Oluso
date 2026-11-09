using Microsoft.AspNetCore.Mvc;
using Oluso.Admin.Authorization;
using Oluso.Core.Authorization;
using Oluso.Core.Api;
using Oluso.Core.Domain.Entities;
using Oluso.Core.Domain.Interfaces;
using Oluso.Core.Events;

namespace Oluso.Admin.Controllers.Service;

/// <summary>
/// Service-to-Service API for managing roles.
/// Used by resource servers and external applications via client_credentials grant.
/// </summary>
/// <remarks>
/// Required scopes:
/// - oluso:roles:read - Read role information
/// - oluso:roles:write - Create and update roles
/// - oluso:roles:delete - Delete roles
///
/// Tenant context comes from client registration, not request headers.
/// Service clients cannot create or modify system-level roles.
/// </remarks>
[Route("api/service/roles")]
public class ServiceRolesController : ServiceBaseController
{
    private readonly IRoleStore _roleStore;
    private readonly IOlusoEventService _eventService;
    private readonly ILogger<ServiceRolesController> _logger;

    public ServiceRolesController(
        ITenantContext tenantContext,
        IRoleStore roleStore,
        IOlusoEventService eventService,
        ILogger<ServiceRolesController> logger)
        : base(tenantContext)
    {
        _roleStore = roleStore;
        _eventService = eventService;
        _logger = logger;
    }

    /// <summary>
    /// Get all roles for the client's tenant
    /// </summary>
    [HttpGet]
    public async Task<ActionResult<IEnumerable<ServiceRoleDto>>> GetRoles(
        [FromQuery] bool includeGlobal = true,
        CancellationToken cancellationToken = default)
    {
        if (!HasScope(ServiceScopes.RolesRead))
        {
            return Forbid();
        }

        var roles = await _roleStore.GetRolesAsync(TenantId, includeGlobal, cancellationToken);

        // Filter out system-level admin roles for service clients
        roles = roles.Where(r => !ReservedClaimTypes.IsReservedRoleName(r.Name!)).ToList();

        // Build result with permissions from claims
        var result = new List<ServiceRoleDto>();
        foreach (var r in roles)
        {
            var claims = await _roleStore.GetRoleClaimsAsync(r.Id, cancellationToken);
            var permissions = claims
                .Where(c => c.Type == OlusoRole.PermissionClaimType)
                .Select(c => c.Value)
                .ToList();

            result.Add(new ServiceRoleDto
            {
                Id = r.Id,
                Name = r.Name!,
                DisplayName = r.DisplayName ?? r.Name!,
                Description = r.Description,
                IsSystemRole = r.IsSystemRole,
                IsGlobal = r.TenantId == null,
                Permissions = permissions,
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
    public async Task<ActionResult<ServiceRoleDetailDto>> GetRole(string roleId, CancellationToken cancellationToken)
    {
        if (!HasScope(ServiceScopes.RolesRead))
        {
            return Forbid();
        }

        var role = await _roleStore.GetByIdAsync(roleId, cancellationToken);

        if (role == null)
        {
            return NotFound();
        }

        // Check tenant access
        if (role.TenantId != null && role.TenantId != TenantId)
        {
            return NotFound();
        }

        // Hide system roles from service clients
        if (ReservedClaimTypes.IsReservedRoleName(role.Name!))
        {
            return NotFound();
        }

        var claims = await _roleStore.GetRoleClaimsAsync(roleId, cancellationToken);

        // Separate permissions from other claims
        var permissions = claims
            .Where(c => c.Type == OlusoRole.PermissionClaimType)
            .Select(c => c.Value)
            .ToList();

        var otherClaims = claims
            .Where(c => c.Type != OlusoRole.PermissionClaimType)
            .Select(c => new ServiceRoleClaimDto
            {
                Type = c.Type,
                Value = c.Value
            }).ToList();

        return Ok(new ServiceRoleDetailDto
        {
            Id = role.Id,
            Name = role.Name!,
            DisplayName = role.DisplayName ?? role.Name!,
            Description = role.Description,
            IsSystemRole = role.IsSystemRole,
            IsGlobal = role.TenantId == null,
            Permissions = permissions,
            Claims = otherClaims,
            CreatedAt = role.CreatedAt,
            UpdatedAt = role.UpdatedAt
        });
    }

    /// <summary>
    /// Get a role by name
    /// </summary>
    [HttpGet("by-name/{roleName}")]
    public async Task<ActionResult<ServiceRoleDto>> GetRoleByName(string roleName, CancellationToken cancellationToken)
    {
        if (!HasScope(ServiceScopes.RolesRead))
        {
            return Forbid();
        }

        // Block access to system roles
        if (ReservedClaimTypes.IsReservedRoleName(roleName))
        {
            return NotFound();
        }

        var role = await _roleStore.GetByNameAsync(roleName, TenantId, cancellationToken);

        if (role == null)
        {
            return NotFound();
        }

        var claims = await _roleStore.GetRoleClaimsAsync(role.Id, cancellationToken);
        var permissions = claims
            .Where(c => c.Type == OlusoRole.PermissionClaimType)
            .Select(c => c.Value)
            .ToList();

        return Ok(new ServiceRoleDto
        {
            Id = role.Id,
            Name = role.Name!,
            DisplayName = role.DisplayName ?? role.Name!,
            Description = role.Description,
            IsSystemRole = role.IsSystemRole,
            IsGlobal = role.TenantId == null,
            Permissions = permissions,
            CreatedAt = role.CreatedAt,
            UpdatedAt = role.UpdatedAt
        });
    }

    /// <summary>
    /// Create a new role for the client's tenant
    /// </summary>
    [HttpPost]
    public async Task<ActionResult<ServiceRoleDto>> CreateRole(
        [FromBody] ServiceCreateRoleRequest request,
        CancellationToken cancellationToken)
    {
        if (!HasScope(ServiceScopes.RolesWrite))
        {
            return Forbid();
        }

        // Block creation of reserved system role names
        if (ReservedClaimTypes.IsReservedRoleName(request.Name))
        {
            _logger.LogWarning(
                "Service client {ClientId} attempted to create reserved role name: {RoleName}",
                ClientId, request.Name);
            return BadRequest(new { error = $"The role name '{request.Name}' is reserved for system use" });
        }

        var validationError = ValidateRoleContent(request.Permissions, request.Claims);
        if (validationError != null)
        {
            return BadRequest(new { error = validationError });
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
            TenantId = TenantId,
            IsSystemRole = false
        };

        var created = await _roleStore.CreateAsync(role, cancellationToken);

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

        _logger.LogInformation("Service client {ClientId} created role {RoleName} for tenant {TenantId}", ClientId, role.Name, TenantId);

        // Raise audit event
        await _eventService.RaiseAsync(new ServiceRoleCreatedEvent
        {
            TenantId = TenantId,
            ClientId = ClientId,
            IpAddress = ClientIp,
            ResourceId = role.Id,
            ResourceName = role.Name,
            RoleName = role.Name
        }, cancellationToken);

        return CreatedAtAction(nameof(GetRole), new { roleId = role.Id }, new ServiceRoleDto
        {
            Id = role.Id,
            Name = role.Name,
            DisplayName = role.DisplayName ?? role.Name,
            Description = role.Description,
            IsSystemRole = role.IsSystemRole,
            IsGlobal = role.TenantId == null,
            Permissions = permissionsList,
            CreatedAt = role.CreatedAt
        });
    }

    /// <summary>
    /// Update an existing role
    /// </summary>
    [HttpPut("{roleId}")]
    public async Task<ActionResult<ServiceRoleDto>> UpdateRole(
        string roleId,
        [FromBody] ServiceUpdateRoleRequest request,
        CancellationToken cancellationToken)
    {
        if (!HasScope(ServiceScopes.RolesWrite))
        {
            return Forbid();
        }

        var role = await _roleStore.GetByIdAsync(roleId, cancellationToken);

        if (role == null)
        {
            return NotFound();
        }

        // Check tenant access - can only update own roles
        if (role.TenantId != TenantId)
        {
            return NotFound();
        }

        // System roles cannot be modified by service clients
        if (role.IsSystemRole)
        {
            return BadRequest(new { error = "System roles cannot be modified" });
        }

        // Validated before the first write, so a rejected request leaves the role untouched.
        var validationError = ValidateRoleContent(request.Permissions, request.Claims);
        if (validationError != null)
        {
            return BadRequest(new { error = validationError });
        }

        // Check for name conflict if name is being changed
        if (!string.IsNullOrEmpty(request.Name) && request.Name != role.Name)
        {
            // Block renaming to reserved system role names
            if (ReservedClaimTypes.IsReservedRoleName(request.Name))
            {
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

        role.UpdatedAt = DateTime.UtcNow;

        await _roleStore.UpdateAsync(role, cancellationToken);

        // Get existing claims
        var existingClaims = await _roleStore.GetRoleClaimsAsync(roleId, cancellationToken);

        // Update permissions if provided
        List<string> currentPermissions;
        if (request.Permissions != null)
        {
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

            currentPermissions = request.Permissions.ToList();
        }
        else
        {
            // Keep existing permissions
            currentPermissions = existingClaims
                .Where(c => c.Type == OlusoRole.PermissionClaimType)
                .Select(c => c.Value)
                .ToList();
        }

        // Update other claims if provided
        if (request.Claims != null)
        {
            // Entitlements are managed by the admin entitlements field; the
            // Service API neither writes nor sweeps them.
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
        }

        _logger.LogInformation("Service client {ClientId} updated role {RoleId}", ClientId, roleId);

        // Raise audit event
        await _eventService.RaiseAsync(new ServiceRoleUpdatedEvent
        {
            TenantId = TenantId,
            ClientId = ClientId,
            IpAddress = ClientIp,
            ResourceId = roleId,
            ResourceName = role.Name,
            RoleName = role.Name!
        }, cancellationToken);

        return Ok(new ServiceRoleDto
        {
            Id = role.Id,
            Name = role.Name!,
            DisplayName = role.DisplayName ?? role.Name!,
            Description = role.Description,
            IsSystemRole = role.IsSystemRole,
            IsGlobal = role.TenantId == null,
            Permissions = currentPermissions,
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
        if (!HasScope(ServiceScopes.RolesDelete))
        {
            return Forbid();
        }

        var role = await _roleStore.GetByIdAsync(roleId, cancellationToken);

        if (role == null)
        {
            return NotFound();
        }

        // Check tenant access - can only delete own roles
        if (role.TenantId != TenantId)
        {
            return NotFound();
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

        _logger.LogInformation("Service client {ClientId} deleted role {RoleId}", ClientId, roleId);

        // Raise audit event
        await _eventService.RaiseAsync(new ServiceRoleDeletedEvent
        {
            TenantId = TenantId,
            ClientId = ClientId,
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
    public async Task<ActionResult<IEnumerable<ServiceRoleUserDto>>> GetRoleUsers(
        string roleId,
        CancellationToken cancellationToken)
    {
        if (!HasScope(ServiceScopes.RolesRead))
        {
            return Forbid();
        }

        var role = await _roleStore.GetByIdAsync(roleId, cancellationToken);

        if (role == null)
        {
            return NotFound();
        }

        // Check tenant access; system roles are hidden from service clients
        if ((role.TenantId != null && role.TenantId != TenantId)
            || ReservedClaimTypes.IsReservedRoleName(role.Name!))
        {
            return NotFound();
        }

        // Holders in this tenant only: a platform-wide role is held across
        // tenants, and the ambient lookup widens to all of them at platform scope.
        var users = await _roleStore.GetUsersByRoleInTenantAsync(roleId, TenantId!, cancellationToken);

        return Ok(users.Select(u => new ServiceRoleUserDto
        {
            Id = u.Id,
            UserName = u.UserName,
            Email = u.Email,
            DisplayName = u.DisplayName
        }));
    }

    /// <summary>
    /// The permission and claim rules a role written by a service client must
    /// meet — the same <see cref="RolePermissionValidator"/> the admin Roles
    /// API uses, with a service client's standing (neither SuperAdmin nor
    /// organization admin).
    /// </summary>
    private static string? ValidateRoleContent(
        IEnumerable<string>? permissions,
        IEnumerable<ServiceRoleClaimDto>? claims) =>
        RolePermissionValidator.ValidateRoleClaims(claims?.Select(c => c.Type))
        ?? RolePermissionValidator.Validate(permissions, RolePermissionGrantor.ServiceClient);
}

#region DTOs

public class ServiceRoleDto
{
    public string Id { get; set; } = null!;
    public string Name { get; set; } = null!;
    public string DisplayName { get; set; } = null!;
    public string? Description { get; set; }
    public bool IsSystemRole { get; set; }
    public bool IsGlobal { get; set; }
    public List<string> Permissions { get; set; } = new();
    public DateTime CreatedAt { get; set; }
    public DateTime? UpdatedAt { get; set; }
}

public class ServiceRoleDetailDto : ServiceRoleDto
{
    public List<ServiceRoleClaimDto> Claims { get; set; } = new();
}

public class ServiceRoleClaimDto
{
    public string Type { get; set; } = null!;
    public string Value { get; set; } = null!;
}

public class ServiceCreateRoleRequest
{
    public string Name { get; set; } = null!;
    public string? DisplayName { get; set; }
    public string? Description { get; set; }
    public List<string>? Permissions { get; set; }
    public List<ServiceRoleClaimDto>? Claims { get; set; }
}

public class ServiceUpdateRoleRequest
{
    public string? Name { get; set; }
    public string? DisplayName { get; set; }
    public string? Description { get; set; }
    public List<string>? Permissions { get; set; }
    public List<ServiceRoleClaimDto>? Claims { get; set; }
}

public class ServiceRoleUserDto
{
    public string Id { get; set; } = null!;
    public string UserName { get; set; } = null!;
    public string Email { get; set; } = null!;
    public string? DisplayName { get; set; }
}

#endregion

#region Events

[WebhookEvent("Service Role Created", Description = "Fired when a service (M2M) client creates a role")]
public class ServiceRoleCreatedEvent : ServiceActionEvent
{
    public override string EventType => "service.role.created";
    public override string ResourceType => "Role";
    public override string? WebhookEventType => "service.role_created";

    public required string RoleName { get; init; }
}

[WebhookEvent("Service Role Updated", Description = "Fired when a service (M2M) client updates a role")]
public class ServiceRoleUpdatedEvent : ServiceActionEvent
{
    public override string EventType => "service.role.updated";
    public override string ResourceType => "Role";
    public override string? WebhookEventType => "service.role_updated";

    public required string RoleName { get; init; }
}

[WebhookEvent("Service Role Deleted", Description = "Fired when a service (M2M) client deletes a role")]
public class ServiceRoleDeletedEvent : ServiceActionEvent
{
    public override string EventType => "service.role.deleted";
    public override string ResourceType => "Role";
    public override string? WebhookEventType => "service.role_deleted";

    public required string RoleName { get; init; }
}

#endregion