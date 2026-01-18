using Microsoft.AspNetCore.Mvc;
using Oluso.Core.Api;
using Oluso.Core.Domain.Interfaces;
using Oluso.Core.Events;
using Oluso.Core.Services;

namespace Oluso.Admin.Controllers.Service;

/// <summary>
/// Service-to-Service API for managing users.
/// Used by resource servers and external applications via client_credentials grant.
/// </summary>
/// <remarks>
/// Required scopes:
/// - oluso:users:read - Read user information
/// - oluso:users:write - Create and update users
/// - oluso:users:delete - Delete users
/// - oluso:users:manage-roles - Manage user role assignments
///
/// Tenant context comes from client registration, not request headers.
/// </remarks>
[Route("api/service/users")]
public class ServiceUsersController : ServiceBaseController
{
    private readonly IOlusoUserService _userService;
    private readonly IRoleStore _roleStore;
    private readonly IOlusoEventService _eventService;
    private readonly ILogger<ServiceUsersController> _logger;

    public ServiceUsersController(
        ITenantContext tenantContext,
        IOlusoUserService userService,
        IRoleStore roleStore,
        IOlusoEventService eventService,
        ILogger<ServiceUsersController> logger)
        : base(tenantContext)
    {
        _userService = userService;
        _roleStore = roleStore;
        _eventService = eventService;
        _logger = logger;
    }

    /// <summary>
    /// Get all users for the client's tenant
    /// </summary>
    [HttpGet]
    public async Task<ActionResult<ServicePagedResult<ServiceUserDto>>> GetUsers(
        [FromQuery] string? search = null,
        [FromQuery] string? role = null,
        [FromQuery] bool? isActive = null,
        [FromQuery] int page = 1,
        [FromQuery] int pageSize = 20,
        CancellationToken cancellationToken = default)
    {
        if (!HasScope(ServiceScopes.UsersRead))
        {
            return Forbid();
        }

        var query = new UsersQuery
        {
            Search = search,
            Role = role,
            IsActive = isActive,
            TenantId = TenantId,
            Page = page,
            PageSize = pageSize
        };

        var usersResult = await _userService.GetUsersAsync(query, cancellationToken);

        var result = new ServicePagedResult<ServiceUserDto>
        {
            Items = usersResult.Users.Select(u => new ServiceUserDto
            {
                Id = u.Id,
                UserName = u.Username,
                Email = u.Email ?? "",
                FirstName = u.FirstName,
                LastName = u.LastName,
                DisplayName = u.DisplayName,
                IsActive = u.IsActive,
                EmailVerified = u.EmailVerified,
                PhoneNumber = u.PhoneNumber,
                Roles = u.Roles?.ToList() ?? new List<string>(),
                LastLoginAt = u.LastLoginAt
            }).ToList(),
            TotalCount = usersResult.TotalCount,
            Page = usersResult.Page,
            PageSize = usersResult.PageSize,
            TotalPages = usersResult.TotalPages
        };

        return Ok(result);
    }

    /// <summary>
    /// Get a specific user by ID
    /// </summary>
    [HttpGet("{userId}")]
    public async Task<ActionResult<ServiceUserDetailDto>> GetUser(string userId, CancellationToken cancellationToken)
    {
        if (!HasScope(ServiceScopes.UsersRead))
        {
            return Forbid();
        }

        var user = await _userService.FindByIdAsync(userId, cancellationToken);

        if (user == null || user.TenantId != TenantId)
        {
            return NotFound();
        }

        var roles = await _userService.GetRolesAsync(userId, cancellationToken);
        var claims = await _userService.GetClaimsAsync(userId, cancellationToken);

        return Ok(new ServiceUserDetailDto
        {
            Id = user.Id,
            UserName = user.Username,
            Email = user.Email ?? "",
            FirstName = user.FirstName,
            LastName = user.LastName,
            DisplayName = user.DisplayName ?? $"{user.FirstName} {user.LastName}".Trim(),
            IsActive = user.IsActive,
            EmailVerified = user.EmailVerified,
            PhoneNumber = user.PhoneNumber,
            Roles = roles.ToList(),
            Claims = claims.Select(c => new ServiceUserClaimDto { Type = c.Type, Value = c.Value }).ToList(),
            CustomProperties = user.CustomProperties?.ToDictionary(k => k.Key, v => v.Value),
            LastLoginAt = user.LastLoginAt
        });
    }

    /// <summary>
    /// Find a user by email
    /// </summary>
    [HttpGet("by-email/{email}")]
    public async Task<ActionResult<ServiceUserDto>> GetUserByEmail(string email, CancellationToken cancellationToken)
    {
        if (!HasScope(ServiceScopes.UsersRead))
        {
            return Forbid();
        }

        var user = await _userService.FindByEmailAsync(email, cancellationToken);

        if (user == null || user.TenantId != TenantId)
        {
            return NotFound();
        }

        var roles = await _userService.GetRolesAsync(user.Id, cancellationToken);

        return Ok(new ServiceUserDto
        {
            Id = user.Id,
            UserName = user.Username,
            Email = user.Email ?? "",
            FirstName = user.FirstName,
            LastName = user.LastName,
            DisplayName = user.DisplayName,
            IsActive = user.IsActive,
            EmailVerified = user.EmailVerified,
            PhoneNumber = user.PhoneNumber,
            Roles = roles.ToList(),
            LastLoginAt = user.LastLoginAt
        });
    }

    /// <summary>
    /// Create a new user
    /// </summary>
    [HttpPost]
    public async Task<ActionResult<ServiceUserDto>> CreateUser(
        [FromBody] ServiceCreateUserRequest request,
        CancellationToken cancellationToken)
    {
        if (!HasScope(ServiceScopes.UsersWrite))
        {
            return Forbid();
        }

        var createRequest = new Oluso.Core.Services.CreateUserRequest
        {
            Email = request.Email,
            Username = request.UserName,
            Password = request.Password ?? "",
            FirstName = request.FirstName,
            LastName = request.LastName,
            PhoneNumber = request.PhoneNumber,
            TenantId = TenantId,
            RequireEmailVerification = !(request.EmailVerified ?? false)
        };

        var result = await _userService.CreateUserAsync(createRequest, cancellationToken);

        if (!result.Succeeded)
        {
            return BadRequest(new { errors = result.Errors ?? new[] { result.Error ?? "Unknown error" } });
        }

        _logger.LogInformation("Service client {ClientId} created user {UserId} ({Email}) for tenant {TenantId}",
            ClientId, result.UserId, request.Email, TenantId);

        // Raise audit event
        await _eventService.RaiseAsync(new ServiceUserCreatedEvent
        {
            TenantId = TenantId,
            ClientId = ClientId,
            IpAddress = ClientIp,
            ResourceId = result.UserId,
            ResourceName = request.Email,
            Email = request.Email,
            Username = request.UserName
        }, cancellationToken);

        var user = result.User ?? await _userService.FindByIdAsync(result.UserId!, cancellationToken);
        var roles = await _userService.GetRolesAsync(result.UserId!, cancellationToken);

        return CreatedAtAction(nameof(GetUser), new { userId = result.UserId }, new ServiceUserDto
        {
            Id = result.UserId!,
            UserName = user?.Username ?? request.UserName ?? request.Email,
            Email = request.Email,
            FirstName = request.FirstName,
            LastName = request.LastName,
            DisplayName = request.DisplayName,
            IsActive = true,
            EmailVerified = request.EmailVerified ?? false,
            Roles = roles.ToList()
        });
    }

    /// <summary>
    /// Update a user
    /// </summary>
    [HttpPut("{userId}")]
    public async Task<ActionResult<ServiceUserDto>> UpdateUser(
        string userId,
        [FromBody] ServiceUpdateUserRequest request,
        CancellationToken cancellationToken)
    {
        if (!HasScope(ServiceScopes.UsersWrite))
        {
            return Forbid();
        }

        var user = await _userService.FindByIdAsync(userId, cancellationToken);

        if (user == null)
        {
            return NotFound();
        }

        // Check tenant access
        if (user.TenantId != TenantId)
        {
            return NotFound();
        }

        var updateRequest = new Oluso.Core.Services.UpdateUserRequest
        {
            Email = request.Email,
            FirstName = request.FirstName,
            LastName = request.LastName,
            PhoneNumber = request.PhoneNumber,
            Picture = request.Picture
        };

        var result = await _userService.UpdateUserAsync(userId, updateRequest, cancellationToken);

        if (!result.Succeeded)
        {
            return BadRequest(new { errors = result.Errors ?? new[] { result.Error ?? "Unknown error" } });
        }

        _logger.LogInformation("Service client {ClientId} updated user {UserId}", ClientId, userId);

        // Raise audit event
        await _eventService.RaiseAsync(new ServiceUserUpdatedEvent
        {
            TenantId = TenantId,
            ClientId = ClientId,
            IpAddress = ClientIp,
            ResourceId = userId,
            ResourceName = user.Email
        }, cancellationToken);

        var updatedUser = result.User ?? await _userService.FindByIdAsync(userId, cancellationToken);
        var roles = await _userService.GetRolesAsync(userId, cancellationToken);

        return Ok(new ServiceUserDto
        {
            Id = userId,
            UserName = updatedUser?.Username ?? "",
            Email = updatedUser?.Email ?? "",
            FirstName = updatedUser?.FirstName,
            LastName = updatedUser?.LastName,
            DisplayName = updatedUser?.DisplayName,
            IsActive = updatedUser?.IsActive ?? true,
            EmailVerified = updatedUser?.EmailVerified ?? false,
            Roles = roles.ToList()
        });
    }

    /// <summary>
    /// Delete a user
    /// </summary>
    [HttpDelete("{userId}")]
    public async Task<IActionResult> DeleteUser(string userId, CancellationToken cancellationToken)
    {
        if (!HasScope(ServiceScopes.UsersDelete))
        {
            return Forbid();
        }

        var user = await _userService.FindByIdAsync(userId, cancellationToken);

        if (user == null)
        {
            return NotFound();
        }

        // Check tenant access
        if (user.TenantId != TenantId)
        {
            return NotFound();
        }

        _logger.LogInformation("Service client {ClientId} deleted user {UserId}", ClientId, userId);

        // Raise audit event
        await _eventService.RaiseAsync(new ServiceUserDeletedEvent
        {
            TenantId = TenantId,
            ClientId = ClientId,
            IpAddress = ClientIp,
            ResourceId = userId,
            ResourceName = user.Email
        }, cancellationToken);

        return NoContent();
    }

    /// <summary>
    /// Get roles assigned to a user
    /// </summary>
    [HttpGet("{userId}/roles")]
    public async Task<ActionResult<IEnumerable<string>>> GetUserRoles(string userId, CancellationToken cancellationToken)
    {
        if (!HasScope(ServiceScopes.UsersRead))
        {
            return Forbid();
        }

        var user = await _userService.FindByIdAsync(userId, cancellationToken);

        if (user == null)
        {
            return NotFound();
        }

        // Check tenant access
        if (user.TenantId != TenantId)
        {
            return NotFound();
        }

        var roles = await _userService.GetRolesAsync(userId, cancellationToken);
        return Ok(roles);
    }

    /// <summary>
    /// Set roles for a user (replaces existing roles)
    /// </summary>
    [HttpPut("{userId}/roles")]
    public async Task<IActionResult> SetUserRoles(
        string userId,
        [FromBody] ServiceSetUserRolesRequest request,
        CancellationToken cancellationToken)
    {
        if (!HasScope(ServiceScopes.UsersManageRoles))
        {
            return Forbid();
        }

        var user = await _userService.FindByIdAsync(userId, cancellationToken);

        if (user == null)
        {
            return NotFound();
        }

        // Check tenant access - can only modify users in own tenant
        if (user.TenantId != TenantId)
        {
            return NotFound();
        }

        // Service clients cannot assign system-level roles
        var systemRoles = request.Roles
            .Where(r => Authorization.ReservedClaimTypes.IsReservedRoleName(r))
            .ToList();

        if (systemRoles.Any())
        {
            _logger.LogWarning(
                "Service client {ClientId} attempted to assign system roles {Roles} to user {UserId}",
                ClientId, string.Join(", ", systemRoles), userId);
            return BadRequest(new { error = $"Cannot assign system roles: {string.Join(", ", systemRoles)}" });
        }

        // Validate all roles belong to the tenant or are global roles
        var invalidRoles = new List<string>();
        foreach (var roleName in request.Roles)
        {
            var role = await _roleStore.GetByNameAsync(roleName, TenantId, cancellationToken);
            if (role == null)
            {
                invalidRoles.Add(roleName);
            }
            else if (role.TenantId != null && role.TenantId != TenantId)
            {
                invalidRoles.Add(roleName);
            }
        }

        if (invalidRoles.Any())
        {
            return BadRequest(new { error = $"Invalid or inaccessible roles: {string.Join(", ", invalidRoles)}" });
        }

        var result = await _userService.SetRolesAsync(userId, request.Roles, cancellationToken);

        if (!result.Succeeded)
        {
            return BadRequest(new { errors = result.Errors ?? new[] { result.Error ?? "Unknown error" } });
        }

        _logger.LogInformation(
            "Service client {ClientId} set roles for user {UserId}: {Roles}",
            ClientId, userId, string.Join(", ", request.Roles));

        // Raise audit event
        await _eventService.RaiseAsync(new ServiceUserRolesUpdatedEvent
        {
            TenantId = TenantId,
            ClientId = ClientId,
            IpAddress = ClientIp,
            ResourceId = userId,
            ResourceName = user.Email,
            Roles = request.Roles
        }, cancellationToken);

        return Ok(new { message = "Roles updated successfully", roles = request.Roles });
    }

    /// <summary>
    /// Add a role to a user
    /// </summary>
    [HttpPost("{userId}/roles/{roleName}")]
    public async Task<IActionResult> AddUserRole(
        string userId,
        string roleName,
        CancellationToken cancellationToken)
    {
        if (!HasScope(ServiceScopes.UsersManageRoles))
        {
            return Forbid();
        }

        var user = await _userService.FindByIdAsync(userId, cancellationToken);

        if (user == null)
        {
            return NotFound();
        }

        if (user.TenantId != TenantId)
        {
            return NotFound();
        }

        // Service clients cannot assign system-level roles
        if (Authorization.ReservedClaimTypes.IsReservedRoleName(roleName))
        {
            return BadRequest(new { error = $"Cannot assign system role: {roleName}" });
        }

        // Validate role belongs to the tenant or is a global role
        var role = await _roleStore.GetByNameAsync(roleName, TenantId, cancellationToken);
        if (role == null || (role.TenantId != null && role.TenantId != TenantId))
        {
            return BadRequest(new { error = $"Invalid or inaccessible role: {roleName}" });
        }

        var currentRoles = await _userService.GetRolesAsync(userId, cancellationToken);
        var newRoles = currentRoles.Append(roleName).Distinct().ToList();

        var result = await _userService.SetRolesAsync(userId, newRoles, cancellationToken);

        if (!result.Succeeded)
        {
            return BadRequest(new { errors = result.Errors ?? new[] { result.Error ?? "Unknown error" } });
        }

        _logger.LogInformation(
            "Service client {ClientId} added role {RoleName} to user {UserId}",
            ClientId, roleName, userId);

        return Ok(new { message = "Role added successfully" });
    }

    /// <summary>
    /// Remove a role from a user
    /// </summary>
    [HttpDelete("{userId}/roles/{roleName}")]
    public async Task<IActionResult> RemoveUserRole(
        string userId,
        string roleName,
        CancellationToken cancellationToken)
    {
        if (!HasScope(ServiceScopes.UsersManageRoles))
        {
            return Forbid();
        }

        var user = await _userService.FindByIdAsync(userId, cancellationToken);

        if (user == null)
        {
            return NotFound();
        }

        if (user.TenantId != TenantId)
        {
            return NotFound();
        }

        var currentRoles = await _userService.GetRolesAsync(userId, cancellationToken);
        var newRoles = currentRoles.Where(r => r != roleName).ToList();

        var result = await _userService.SetRolesAsync(userId, newRoles, cancellationToken);

        if (!result.Succeeded)
        {
            return BadRequest(new { errors = result.Errors ?? new[] { result.Error ?? "Unknown error" } });
        }

        _logger.LogInformation(
            "Service client {ClientId} removed role {RoleName} from user {UserId}",
            ClientId, roleName, userId);

        return NoContent();
    }
}

#region DTOs

public class ServicePagedResult<T>
{
    public List<T> Items { get; set; } = new();
    public int TotalCount { get; set; }
    public int Page { get; set; }
    public int PageSize { get; set; }
    public int TotalPages { get; set; }
}

public class ServiceUserDto
{
    public string Id { get; set; } = null!;
    public string UserName { get; set; } = null!;
    public string Email { get; set; } = null!;
    public string? FirstName { get; set; }
    public string? LastName { get; set; }
    public string? DisplayName { get; set; }
    public bool IsActive { get; set; }
    public bool EmailVerified { get; set; }
    public string? PhoneNumber { get; set; }
    public List<string> Roles { get; set; } = new();
    public DateTime? LastLoginAt { get; set; }
}

public class ServiceUserDetailDto : ServiceUserDto
{
    public List<ServiceUserClaimDto> Claims { get; set; } = new();
    public Dictionary<string, string>? CustomProperties { get; set; }
}

public class ServiceUserClaimDto
{
    public string Type { get; set; } = null!;
    public string Value { get; set; } = null!;
}

public class ServiceCreateUserRequest
{
    public string Email { get; set; } = null!;
    public string? UserName { get; set; }
    public string? Password { get; set; }
    public string? FirstName { get; set; }
    public string? LastName { get; set; }
    public string? DisplayName { get; set; }
    public string? PhoneNumber { get; set; }
    public bool? EmailVerified { get; set; }
}

public class ServiceUpdateUserRequest
{
    public string? Email { get; set; }
    public string? FirstName { get; set; }
    public string? LastName { get; set; }
    public string? DisplayName { get; set; }
    public string? PhoneNumber { get; set; }
    public string? Picture { get; set; }
    public bool? IsActive { get; set; }
}

public class ServiceSetUserRolesRequest
{
    public List<string> Roles { get; set; } = new();
}

#endregion

#region Events

/// <summary>
/// Base class for service API actions (M2M operations)
/// </summary>
public abstract class ServiceActionEvent : OlusoEvent
{
    public override string Category => "Service";

    /// <summary>
    /// Client ID that performed the action
    /// </summary>
    public required string ClientId { get; init; }

    /// <summary>
    /// IP address of the client making the request
    /// </summary>
    public string? IpAddress { get; init; }

    /// <summary>
    /// Resource type being modified
    /// </summary>
    public abstract string ResourceType { get; }

    /// <summary>
    /// ID of the resource being modified
    /// </summary>
    public string? ResourceId { get; init; }

    /// <summary>
    /// Name of the resource for display purposes
    /// </summary>
    public string? ResourceName { get; init; }
}

public class ServiceUserCreatedEvent : ServiceActionEvent
{
    public override string EventType => "service.user.created";
    public override string ResourceType => "User";
    public override string? WebhookEventType => "service.user_created";

    public required string Email { get; init; }
    public string? Username { get; init; }
}

public class ServiceUserUpdatedEvent : ServiceActionEvent
{
    public override string EventType => "service.user.updated";
    public override string ResourceType => "User";
    public override string? WebhookEventType => "service.user_updated";
}

public class ServiceUserDeletedEvent : ServiceActionEvent
{
    public override string EventType => "service.user.deleted";
    public override string ResourceType => "User";
    public override string? WebhookEventType => "service.user_deleted";
}

public class ServiceUserRolesUpdatedEvent : ServiceActionEvent
{
    public override string EventType => "service.user.roles_updated";
    public override string ResourceType => "UserRole";
    public override string? WebhookEventType => "service.user_roles_updated";

    public List<string> Roles { get; init; } = new();
}

#endregion
