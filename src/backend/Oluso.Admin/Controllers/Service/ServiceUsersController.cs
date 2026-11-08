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
    private readonly IRoleGrantService _roleGrants;
    private readonly IModuleAdminAssignmentNotifier _moduleAdminNotifier;
    private readonly ILogger<ServiceUsersController> _logger;

    public ServiceUsersController(
        ITenantContext tenantContext,
        IOlusoUserService userService,
        IRoleStore roleStore,
        IOlusoEventService eventService,
        IRoleGrantService roleGrants,
        IModuleAdminAssignmentNotifier moduleAdminNotifier,
        ILogger<ServiceUsersController> logger)
        : base(tenantContext)
    {
        _userService = userService;
        _roleStore = roleStore;
        _eventService = eventService;
        _roleGrants = roleGrants;
        _moduleAdminNotifier = moduleAdminNotifier;
        _logger = logger;
    }

    private RoleGrantActor Actor() => new(ClientId, $"Service client {ClientId}", ClientIp);

    private EventOrigin Origin => EventOrigin.ServiceClient(ClientId, ClientIp);

    private Task RaiseRoleChangeAsync(string userId, Core.Domain.Entities.OlusoRole role, bool removed, CancellationToken cancellationToken) =>
        _eventService.RaiseAsync(removed
            ? new UserRoleRemovedEvent { TenantId = TenantId, SubjectId = userId, RoleId = role.Id, RoleName = role.Name!, Origin = Origin }
            : new UserRoleAssignedEvent { TenantId = TenantId, SubjectId = userId, RoleId = role.Id, RoleName = role.Name!, Origin = Origin },
            cancellationToken);

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

        var user = await _userService.FindByIdInTenantAsync(userId, TenantId!, cancellationToken);

        if (user == null)
        {
            return NotFound();
        }

        var roles = await RoleNamesInTenantAsync(userId, cancellationToken);
        var claims = await _userService.GetClaimsAsync(userId, cancellationToken);

        // GetClaimsAsync adds role claims from the ambient role lookup; this
        // tenant's roles replace them.
        var claimDtos = claims
            .Where(c => c.Type != System.Security.Claims.ClaimTypes.Role)
            .Select(c => new ServiceUserClaimDto { Type = c.Type, Value = c.Value })
            .Concat(roles.Select(r => new ServiceUserClaimDto { Type = System.Security.Claims.ClaimTypes.Role, Value = r }))
            .ToList();

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
            Roles = roles,
            Claims = claimDtos,
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

        var user = await _userService.FindByEmailInTenantAsync(email, TenantId!, cancellationToken);

        if (user == null)
        {
            return NotFound();
        }

        var roles = await RoleNamesInTenantAsync(user.Id, cancellationToken);

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
            Roles = roles,
            LastLoginAt = user.LastLoginAt
        });
    }

    /// <summary>
    /// Create a new user
    /// </summary>
    [HttpPost]
    public async Task<ActionResult<ServiceUserDto>> CreateUser(
        [FromBody] ServiceCreateUserRequest request,
        [FromServices] ITenantEmailDomainStore emailDomains,
        CancellationToken cancellationToken)
    {
        if (!HasScope(ServiceScopes.UsersWrite))
        {
            return Forbid();
        }

        // Saying an address is verified is trusted only for the tenant's own verified domains.
        var markVerified = request.EmailVerified == true;
        if (markVerified && !await emailDomains.IsVerifiedForTenantAsync(TenantId ?? "", request.Email, cancellationToken))
        {
            return BadRequest(new { errors = new[] { "An address can be marked verified only at an email domain this organization has verified. Leave it unverified: the person confirms it with a code when they sign in." } });
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
            RequireEmailVerification = !(request.EmailVerified ?? false),
            ClientId = ClientId
        };

        using var origin = EventOriginScope.Begin(Origin);
        var result = await _userService.CreateUserAsync(createRequest, cancellationToken);

        if (!result.Succeeded)
        {
            return BadRequest(new { errors = result.MessagesOr("Unknown error") });
        }

        _logger.LogInformation("Service client {ClientId} created user {UserId} ({Email}) for tenant {TenantId}",
            ClientId, result.UserId, request.Email, TenantId);

        if (markVerified)
        {
            await _userService.SetEmailVerifiedAsync(result.UserId!, true, cancellationToken);
        }

        var user = await _userService.FindByIdAsync(result.UserId!, cancellationToken);
        var roles = await RoleNamesInTenantAsync(result.UserId!, cancellationToken);

        return CreatedAtAction(nameof(GetUser), new { userId = result.UserId }, new ServiceUserDto
        {
            Id = result.UserId!,
            UserName = user?.Username ?? request.UserName ?? request.Email,
            Email = request.Email,
            FirstName = request.FirstName,
            LastName = request.LastName,
            DisplayName = request.DisplayName,
            IsActive = true,
            EmailVerified = user?.EmailVerified ?? false,
            Roles = roles
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

        var user = await _userService.FindByIdInTenantAsync(userId, TenantId!, cancellationToken);

        if (user == null)
        {
            return NotFound();
        }

        // A member whose record lives in another tenant: their profile is managed
        // from that tenant, the same rule the admin Users API applies.
        if (user.TenantId != TenantId)
        {
            return StatusCode(403, new
            {
                error = "user_owned_by_another_tenant",
                error_description =
                    "This user belongs to another tenant and has access here through tenant membership. " +
                    "Their profile is managed from their home tenant.",
                ownerTenantId = user.TenantId
            });
        }

        var updateRequest = new Oluso.Core.Services.UpdateUserRequest
        {
            Email = request.Email,
            FirstName = request.FirstName,
            LastName = request.LastName,
            PhoneNumber = request.PhoneNumber,
            Picture = request.Picture
        };

        using var origin = EventOriginScope.Begin(Origin);
        var result = await _userService.UpdateUserAsync(userId, updateRequest, cancellationToken);

        if (!result.Succeeded)
        {
            return BadRequest(new { errors = result.MessagesOr("Unknown error") });
        }

        _logger.LogInformation("Service client {ClientId} updated user {UserId}", ClientId, userId);

        var updatedUser = result.User ?? await _userService.FindByIdAsync(userId, cancellationToken);

        var roles = await RoleNamesInTenantAsync(userId, cancellationToken);

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
            Roles = roles
        });
    }

    /// <summary>
    /// Permanently delete a user whose home tenant is the client's tenant.
    /// </summary>
    [HttpDelete("{userId}")]
    public async Task<IActionResult> DeleteUser(string userId, CancellationToken cancellationToken)
    {
        if (!HasScope(ServiceScopes.UsersDelete))
        {
            return Forbid();
        }

        var user = await _userService.FindByIdInTenantAsync(userId, TenantId!, cancellationToken);

        if (user == null)
        {
            return NotFound();
        }

        if (user.TenantId != TenantId)
        {
            return StatusCode(403, new
            {
                error = "user_owned_by_another_tenant",
                error_description =
                    "This user belongs to another tenant and has access here through tenant membership. " +
                    "Their account can only be deleted from their home tenant.",
                ownerTenantId = user.TenantId
            });
        }

        using var origin = EventOriginScope.Begin(Origin);
        var result = await _userService.DeleteUserAsync(userId, cancellationToken);
        if (!result.Succeeded)
        {
            return result.Error switch
            {
                UserDeletionErrors.NotFound => NotFound(),
                UserDeletionErrors.ProtectedIdentity => StatusCode(403, new { error = result.Error, error_description = result.ErrorDescription }),
                _ => BadRequest(new { error = result.Error, error_description = result.MessageOr("The user could not be deleted") })
            };
        }

        _logger.LogInformation("Service client {ClientId} deleted user {UserId}", ClientId, userId);

        return NoContent();
    }

    /// <summary>
    /// Get roles assigned to a user
    /// </summary>
    [HttpGet("{userId}/roles")]
    public async Task<ActionResult<IEnumerable<ServiceUserRoleDto>>> GetUserRoles(string userId, CancellationToken cancellationToken)
    {
        if (!HasScope(ServiceScopes.UsersRead))
        {
            return Forbid();
        }

        var user = await _userService.FindByIdInTenantAsync(userId, TenantId!, cancellationToken);

        if (user == null)
        {
            return NotFound();
        }

        var roles = await _roleStore.GetUserRolesInTenantAsync(userId, TenantId, cancellationToken);
        return Ok(roles.Select(ToServiceUserRoleDto));
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

        var user = await _userService.FindByIdInTenantAsync(userId, TenantId!, cancellationToken);

        if (user == null)
        {
            return NotFound();
        }

        using var origin = EventOriginScope.Begin(Origin);
        var heldRows = (await _roleStore.GetUserRolesInTenantAsync(userId, TenantId, cancellationToken)).ToList();
        var heldIds = heldRows.Select(r => r.Id).ToHashSet(StringComparer.Ordinal);

        // System roles the user already holds are outside a service client's
        // reach: they stay whether or not the request lists them.
        var heldReserved = heldRows
            .Where(r => Authorization.ReservedClaimTypes.IsReservedRoleName(r.Name!))
            .ToList();
        var heldReservedIds = heldReserved.Select(r => r.Id).ToHashSet(StringComparer.Ordinal);

        var requestedIds = request.RoleIds
            .Where(id => !string.IsNullOrWhiteSpace(id))
            .Distinct(StringComparer.Ordinal)
            .Where(id => !heldReservedIds.Contains(id))
            .ToList();
        var (error, requestedRows) = await ResolveAssignableRolesAsync(requestedIds, cancellationToken);
        if (error != null)
        {
            return error;
        }

        var rows = requestedRows.Concat(heldReserved).ToList();
        var result = await _userService.SetRolesByIdAsync(userId, rows.Select(r => r.Id), cancellationToken);

        if (!result.Succeeded)
        {
            return BadRequest(new { errors = result.MessagesOr("Unknown error") });
        }

        var targetIds = rows.Select(r => r.Id).ToHashSet(StringComparer.Ordinal);
        await _moduleAdminNotifier.NotifyAsync(
            TenantId!, userId, rows.Where(r => !heldIds.Contains(r.Id)), Actor(), cancellationToken);
        await _moduleAdminNotifier.NotifyRemovedAsync(
            TenantId!, userId, heldRows.Where(r => !targetIds.Contains(r.Id)), Actor(), cancellationToken);

        var roleNames = rows.Select(r => r.Name!).ToList();

        _logger.LogInformation(
            "Service client {ClientId} set roles for user {UserId}: {Roles}",
            ClientId, userId, string.Join(", ", roleNames));

        foreach (var role in rows.Where(r => !heldIds.Contains(r.Id)))
            await RaiseRoleChangeAsync(userId, role, removed: false, cancellationToken);
        foreach (var role in heldRows.Where(r => !targetIds.Contains(r.Id)))
            await RaiseRoleChangeAsync(userId, role, removed: true, cancellationToken);

        return Ok(new
        {
            message = "Roles updated successfully",
            roleIds = rows.Select(r => r.Id),
            roles = roleNames
        });
    }

    /// <summary>
    /// Add a role to a user
    /// </summary>
    [HttpPost("{userId}/roles/{roleId}")]
    public async Task<IActionResult> AddUserRole(
        string userId,
        string roleId,
        CancellationToken cancellationToken)
    {
        if (!HasScope(ServiceScopes.UsersManageRoles))
        {
            return Forbid();
        }

        var user = await _userService.FindByIdInTenantAsync(userId, TenantId!, cancellationToken);

        if (user == null)
        {
            return NotFound();
        }

        var (error, rows) = await ResolveAssignableRolesAsync(new[] { roleId }, cancellationToken);
        if (error != null)
        {
            return error;
        }

        using var origin = EventOriginScope.Begin(Origin);
        var role = rows[0];
        await _roleGrants.GrantAsync(role.Id, userId, TenantId!, Actor(), cancellationToken);

        _logger.LogInformation(
            "Service client {ClientId} added role {RoleName} ({RoleId}) to user {UserId}",
            ClientId, role.Name, role.Id, userId);

        return Ok(new { message = "Role added successfully", roleId = role.Id, role = role.Name });
    }

    /// <summary>
    /// Remove a role from a user
    /// </summary>
    [HttpDelete("{userId}/roles/{roleId}")]
    public async Task<IActionResult> RemoveUserRole(
        string userId,
        string roleId,
        CancellationToken cancellationToken)
    {
        if (!HasScope(ServiceScopes.UsersManageRoles))
        {
            return Forbid();
        }

        var user = await _userService.FindByIdInTenantAsync(userId, TenantId!, cancellationToken);

        if (user == null)
        {
            return NotFound();
        }

        var role = await _roleStore.GetByIdAsync(roleId, cancellationToken);
        if (role == null || (role.TenantId != null && role.TenantId != TenantId))
        {
            return NotFound(new { error = $"Unknown role id for this tenant: {roleId}" });
        }

        // Service clients can neither grant nor revoke system-level roles
        if (Authorization.ReservedClaimTypes.IsReservedRoleName(role.Name!))
        {
            _logger.LogWarning(
                "Service client {ClientId} attempted to remove system role {RoleName} from user {UserId}",
                ClientId, role.Name, userId);
            return BadRequest(new { error = $"Cannot remove system roles: {role.Name}" });
        }

        using var origin = EventOriginScope.Begin(Origin);
        await _roleGrants.RevokeAsync(role.Id, userId, TenantId!, Actor(), cancellationToken);

        _logger.LogInformation(
            "Service client {ClientId} removed role {RoleName} ({RoleId}) from user {UserId}",
            ClientId, role.Name, role.Id, userId);

        return NoContent();
    }

    /// <summary>
    /// Resolves role ids to rows a service client may assign: rows of this
    /// tenant or platform-wide, never a reserved platform role. An id outside
    /// the tenant is a 404 reported as unknown rather than by name, the same
    /// answer as an id that doesn't exist.
    /// </summary>
    private async Task<(IActionResult? Error, IReadOnlyList<Core.Domain.Entities.OlusoRole> Rows)> ResolveAssignableRolesAsync(
        IReadOnlyList<string> roleIds,
        CancellationToken cancellationToken)
    {
        var rows = (await _roleStore.GetByIdsAsync(roleIds, cancellationToken))
            .Where(r => r.TenantId == null || r.TenantId == TenantId)
            .ToList();

        var unknown = roleIds.Except(rows.Select(r => r.Id), StringComparer.Ordinal).ToList();
        if (unknown.Count > 0)
        {
            return (NotFound(new { error = $"Unknown role id(s) for this tenant: {string.Join(", ", unknown)}" }), rows);
        }

        // Service clients cannot assign system-level roles
        var reserved = rows
            .Where(r => Authorization.ReservedClaimTypes.IsReservedRoleName(r.Name!))
            .Select(r => r.Name!)
            .ToList();
        if (reserved.Count > 0)
        {
            _logger.LogWarning(
                "Service client {ClientId} attempted to assign system roles {Roles}",
                ClientId, string.Join(", ", reserved));
            return (BadRequest(new { error = $"Cannot assign system roles: {string.Join(", ", reserved)}" }), rows);
        }

        return (null, rows);
    }

    /// <summary>
    /// Names of the roles the user holds in the client's tenant, read from the
    /// tenant's role rows by id — never the user's roles elsewhere.
    /// </summary>
    private async Task<List<string>> RoleNamesInTenantAsync(string userId, CancellationToken cancellationToken) =>
        (await _roleStore.GetUserRolesInTenantAsync(userId, TenantId, cancellationToken))
            .Select(r => r.Name!)
            .ToList();

    private static ServiceUserRoleDto ToServiceUserRoleDto(Core.Domain.Entities.OlusoRole role) => new()
    {
        Id = role.Id,
        Name = role.Name!,
        DisplayName = role.DisplayName ?? role.Name!,
        IsGlobal = role.TenantId == null
    };
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

/// <summary>
/// Replaces the user's roles. Role ids, never names: a tenant row and a
/// platform-wide row may share a name.
/// </summary>
public class ServiceSetUserRolesRequest
{
    public List<string> RoleIds { get; set; } = new();
}

/// <summary>
/// A role the user holds in the client's tenant.
/// </summary>
public class ServiceUserRoleDto
{
    public string Id { get; set; } = null!;
    public string Name { get; set; } = null!;
    public string DisplayName { get; set; } = null!;
    public bool IsGlobal { get; set; }
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

    protected override EventOrigin? ImpliedOrigin() => EventOrigin.ServiceClient(ClientId, IpAddress);
}

#endregion
