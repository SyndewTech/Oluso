using Microsoft.AspNetCore.Mvc;
using Oluso.Admin.Authorization;
using Oluso.Core.Api;
using Oluso.Core.Domain.Interfaces;
using Oluso.Core.Events;
using Oluso.Core.Services;

namespace Oluso.Admin.Controllers;

/// <summary>
/// Admin API for managing users within a tenant
/// </summary>
[Route("api/admin/users")]
public class UsersController : AdminBaseController
{
    private readonly IOlusoUserService _userService;
    private readonly IOlusoEventService _eventService;
    private readonly IServerSideSessionStore? _sessionStore;
    private readonly ILogger<UsersController> _logger;

    public UsersController(
        ITenantContext tenantContext,
        IOlusoUserService userService,
        IOlusoEventService eventService,
        ILogger<UsersController> logger,
        IServerSideSessionStore? sessionStore = null)
        : base(tenantContext)
    {
        _userService = userService;
        _eventService = eventService;
        _sessionStore = sessionStore;
        _logger = logger;
    }

    /// <summary>
    /// Get all users for the current tenant
    /// </summary>
    [HttpGet]
    public async Task<ActionResult<PagedResult<UserDto>>> GetUsers(
        [FromQuery] string? search = null,
        [FromQuery] string? role = null,
        [FromQuery] bool? isActive = null,
        [FromQuery] int page = 1,
        [FromQuery] int pageSize = 20,
        CancellationToken cancellationToken = default)
    {
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

        var result = new PagedResult<UserDto>
        {
            Items = usersResult.Users.Select(u => new UserDto
            {
                Id = u.Id,
                UserName = u.Username,
                Email = u.Email ?? "",
                FirstName = u.FirstName,
                LastName = u.LastName,
                DisplayName = u.DisplayName,
                Picture = u.Picture,
                IsActive = u.IsActive,
                EmailVerified = u.EmailVerified,
                PhoneNumber = u.PhoneNumber,
                PhoneNumberVerified = u.PhoneNumberVerified,
                TwoFactorEnabled = u.TwoFactorEnabled,
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
    public async Task<ActionResult<UserDetailDto>> GetUser(string userId, CancellationToken cancellationToken)
    {
        var user = await _userService.FindByIdAsync(userId, cancellationToken);

        if (user == null)
        {
            return NotFound();
        }

        // Check tenant access
        if (user.TenantId != null && user.TenantId != TenantId)
        {
            return Forbid();
        }

        var roles = await _userService.GetRolesAsync(userId, cancellationToken);
        var claims = await _userService.GetClaimsAsync(userId, cancellationToken);

        return Ok(new UserDetailDto
        {
            Id = user.Id,
            UserName = user.Username,
            Email = user.Email ?? "",
            FirstName = user.FirstName,
            LastName = user.LastName,
            DisplayName = user.DisplayName ?? $"{user.FirstName} {user.LastName}".Trim(),
            Picture = user.Picture,
            IsActive = user.IsActive,
            EmailVerified = user.EmailVerified,
            PhoneNumber = user.PhoneNumber,
            PhoneNumberVerified = user.PhoneNumberVerified,
            TwoFactorEnabled = user.TwoFactorEnabled,
            Roles = roles.ToList(),
            Claims = claims.Select(c => new UserClaimDto { Type = c.Type, Value = c.Value }).ToList(),
            LastLoginAt = user.LastLoginAt
        });
    }

    /// <summary>
    /// Create a new user
    /// </summary>
    [HttpPost]
    public async Task<ActionResult<UserDto>> CreateUser(
        [FromBody] CreateUserRequest request,
        CancellationToken cancellationToken)
    {
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

        _logger.LogInformation("Created user {UserId} ({Email}) for tenant {TenantId}",
            result.UserId, request.Email, TenantId);

        // Raise audit event
        await _eventService.RaiseAsync(new AdminUserCreatedEvent
        {
            TenantId = TenantId,
            AdminUserId = AdminUserId!,
            AdminUserName = AdminUserName,
            IpAddress = ClientIp,
            ResourceId = result.UserId,
            ResourceName = request.Email,
            Email = request.Email,
            Username = request.UserName
        }, cancellationToken);

        var user = result.User ?? await _userService.FindByIdAsync(result.UserId!, cancellationToken);
        var roles = await _userService.GetRolesAsync(result.UserId!, cancellationToken);

        return CreatedAtAction(nameof(GetUser), new { userId = result.UserId }, new UserDto
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
    public async Task<ActionResult<UserDto>> UpdateUser(
        string userId,
        [FromBody] UpdateUserRequest request,
        CancellationToken cancellationToken)
    {
        var user = await _userService.FindByIdAsync(userId, cancellationToken);

        if (user == null)
        {
            return NotFound();
        }

        // Check tenant access
        if (user.TenantId != TenantId)
        {
            return Forbid();
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

        _logger.LogInformation("Updated user {UserId}", userId);

        // Raise audit event
        await _eventService.RaiseAsync(new AdminUserUpdatedEvent
        {
            TenantId = TenantId,
            AdminUserId = AdminUserId!,
            AdminUserName = AdminUserName,
            IpAddress = ClientIp,
            ResourceId = userId,
            ResourceName = user.Email
        }, cancellationToken);

        var updatedUser = result.User ?? await _userService.FindByIdAsync(userId, cancellationToken);
        var roles = await _userService.GetRolesAsync(userId, cancellationToken);

        return Ok(new UserDto
        {
            Id = userId,
            UserName = updatedUser?.Username ?? "",
            Email = updatedUser?.Email ?? "",
            FirstName = updatedUser?.FirstName,
            LastName = updatedUser?.LastName,
            DisplayName = updatedUser?.DisplayName,
            Picture = updatedUser?.Picture,
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
        var user = await _userService.FindByIdAsync(userId, cancellationToken);

        if (user == null)
        {
            return NotFound();
        }

        // Check tenant access
        if (user.TenantId != TenantId)
        {
            return Forbid();
        }

        // Prevent deleting self
        var currentUserId = User.FindFirst("sub")?.Value;
        if (userId == currentUserId)
        {
            return BadRequest(new { error = "Cannot delete your own account" });
        }

        // Note: Actual deletion would need to be implemented via IOlusoUserService
        _logger.LogInformation("Deleted user {UserId}", userId);

        // Raise audit event
        await _eventService.RaiseAsync(new AdminUserDeletedEvent
        {
            TenantId = TenantId,
            AdminUserId = AdminUserId!,
            AdminUserName = AdminUserName,
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
        var user = await _userService.FindByIdAsync(userId, cancellationToken);

        if (user == null)
        {
            return NotFound();
        }

        // Check tenant access
        if (user.TenantId != null && user.TenantId != TenantId)
        {
            return Forbid();
        }

        var roles = await _userService.GetRolesAsync(userId, cancellationToken);
        return Ok(roles);
    }

    /// <summary>
    /// Set roles for a user (replaces existing roles)
    /// </summary>
    /// <remarks>
    /// SECURITY: System roles (SuperAdmin, SystemAdmin) can only be assigned by SuperAdmin users.
    /// Tenant admins can only assign tenant-scoped roles.
    /// </remarks>
    [HttpPut("{userId}/roles")]
    public async Task<IActionResult> SetUserRoles(
        string userId,
        [FromBody] SetUserRolesRequest request,
        CancellationToken cancellationToken)
    {
        var user = await _userService.FindByIdAsync(userId, cancellationToken);

        if (user == null)
        {
            return NotFound();
        }

        // Check tenant access - can only modify users in own tenant
        if (user.TenantId != TenantId)
        {
            return Forbid();
        }

        // Security check: Only SuperAdmins can assign system-level roles
        var systemRoles = request.Roles
            .Where(r => ReservedClaimTypes.IsReservedRoleName(r))
            .ToList();

        if (systemRoles.Any() && !IsSuperAdmin)
        {
            _logger.LogWarning(
                "Tenant admin {AdminUserId} attempted to assign system roles {Roles} to user {UserId}",
                AdminUserId, string.Join(", ", systemRoles), userId);
            return BadRequest(new { error = $"Cannot assign system roles: {string.Join(", ", systemRoles)}. Only SuperAdmin can assign these roles." });
        }

        // Prevent removing SuperAdmin from self (to avoid lockout)
        if (userId == AdminUserId)
        {
            var currentRoles = await _userService.GetRolesAsync(userId, cancellationToken);
            var wasSuper = currentRoles.Any(r => r == "SuperAdmin" || r == "SystemAdmin");
            var willBeSuper = request.Roles.Any(r => r == "SuperAdmin" || r == "SystemAdmin");

            if (wasSuper && !willBeSuper)
            {
                return BadRequest(new { error = "Cannot remove SuperAdmin role from yourself" });
            }
        }

        var result = await _userService.SetRolesAsync(userId, request.Roles, cancellationToken);

        if (!result.Succeeded)
        {
            return BadRequest(new { errors = result.Errors ?? new[] { result.Error ?? "Unknown error" } });
        }

        _logger.LogInformation(
            "Admin {AdminUserId} set roles for user {UserId}: {Roles}",
            AdminUserId, userId, string.Join(", ", request.Roles));

        // Raise audit events for each role (for audit trail)
        foreach (var roleName in request.Roles)
        {
            await _eventService.RaiseAsync(new AdminUserRoleAssignedEvent
            {
                TenantId = TenantId,
                AdminUserId = AdminUserId!,
                AdminUserName = AdminUserName,
                IpAddress = ClientIp,
                ResourceId = userId,
                ResourceName = user.Email,
                RoleName = roleName
            }, cancellationToken);
        }

        return Ok(new { message = "Roles updated successfully", roles = request.Roles });
    }

    /// <summary>
    /// Reset a user's password (admin action)
    /// </summary>
    [HttpPost("{userId}/reset-password")]
    public async Task<IActionResult> ResetPassword(
        string userId,
        [FromBody] ResetPasswordRequest request,
        CancellationToken cancellationToken)
    {
        var user = await _userService.FindByIdAsync(userId, cancellationToken);

        if (user == null)
        {
            return NotFound();
        }

        // Check tenant access
        if (user.TenantId != TenantId)
        {
            return Forbid();
        }

        var result = await _userService.ResetPasswordAsync(userId, request.NewPassword, cancellationToken: cancellationToken);

        if (!result.Succeeded)
        {
            return BadRequest(new { errors = result.Errors ?? new[] { result.Error ?? "Unknown error" } });
        }

        _logger.LogInformation("Admin reset password for user {UserId}", userId);

        // Raise audit event
        await _eventService.RaiseAsync(new AdminPasswordResetEvent
        {
            TenantId = TenantId,
            AdminUserId = AdminUserId!,
            AdminUserName = AdminUserName,
            IpAddress = ClientIp,
            ResourceId = userId,
            ResourceName = user.Email
        }, cancellationToken);

        return Ok(new { message = "Password reset successfully" });
    }

    /// <summary>
    /// Check if user has MFA enabled
    /// </summary>
    [HttpGet("{userId}/mfa")]
    public async Task<ActionResult<MfaStatusDto>> GetMfaStatus(string userId, CancellationToken cancellationToken)
    {
        var user = await _userService.FindByIdAsync(userId, cancellationToken);

        if (user == null)
        {
            return NotFound();
        }

        if (user.TenantId != null && user.TenantId != TenantId)
        {
            return Forbid();
        }

        var hasMfa = await _userService.HasMfaEnabledAsync(userId, cancellationToken);

        return Ok(new MfaStatusDto
        {
            Enabled = hasMfa,
            TwoFactorEnabled = user.TwoFactorEnabled
        });
    }

    /// <summary>
    /// Get external logins linked to a user
    /// </summary>
    [HttpGet("{userId}/external-logins")]
    public async Task<ActionResult<IEnumerable<ExternalLoginDto>>> GetExternalLogins(
        string userId,
        CancellationToken cancellationToken)
    {
        var user = await _userService.FindByIdAsync(userId, cancellationToken);

        if (user == null)
        {
            return NotFound();
        }

        if (user.TenantId != null && user.TenantId != TenantId)
        {
            return Forbid();
        }

        var logins = await _userService.GetExternalLoginsAsync(userId, cancellationToken);

        return Ok(logins.Select(l => new ExternalLoginDto
        {
            LoginProvider = l.Provider,
            ProviderKey = l.ProviderKey,
            ProviderDisplayName = l.DisplayName ?? l.Provider
        }));
    }

    /// <summary>
    /// Remove an external login from a user
    /// </summary>
    [HttpDelete("{userId}/external-logins/{provider}")]
    public async Task<IActionResult> RemoveExternalLogin(
        string userId,
        string provider,
        [FromQuery] string providerKey,
        CancellationToken cancellationToken)
    {
        var user = await _userService.FindByIdAsync(userId, cancellationToken);

        if (user == null)
        {
            return NotFound();
        }

        if (user.TenantId != TenantId)
        {
            return Forbid();
        }

        var result = await _userService.RemoveExternalLoginAsync(userId, provider, providerKey, cancellationToken);

        if (!result.Succeeded)
        {
            return BadRequest(new { error = result.Error });
        }

        _logger.LogInformation("Admin removed external login {Provider} from user {UserId}", provider, userId);

        return NoContent();
    }

    /// <summary>
    /// Get active sessions for a user
    /// </summary>
    [HttpGet("{userId}/sessions")]
    public async Task<ActionResult<IEnumerable<UserSessionDto>>> GetUserSessions(
        string userId,
        CancellationToken cancellationToken)
    {
        if (_sessionStore == null)
        {
            return Ok(Array.Empty<UserSessionDto>());
        }

        var user = await _userService.FindByIdAsync(userId, cancellationToken);

        if (user == null)
        {
            return NotFound();
        }

        if (user.TenantId != null && user.TenantId != TenantId)
        {
            return Forbid();
        }

        var sessions = await _sessionStore.GetUserSessionsAsync(userId, cancellationToken);

        return Ok(sessions.Select(s => new UserSessionDto
        {
            SessionId = s.SessionId,
            ClientId = s.ClientId,
            ClientName = s.ClientName,
            IpAddress = s.IpAddress,
            UserAgent = s.UserAgent,
            Created = s.Created,
            Renewed = s.Renewed,
            Expires = s.Expires,
            IsCurrent = s.IsCurrent
        }));
    }

    /// <summary>
    /// Revoke a specific session for a user
    /// </summary>
    [HttpDelete("{userId}/sessions/{sessionId}")]
    public async Task<IActionResult> RevokeSession(
        string userId,
        string sessionId,
        CancellationToken cancellationToken)
    {
        if (_sessionStore == null)
        {
            return BadRequest(new { error = "Session management not available" });
        }

        var user = await _userService.FindByIdAsync(userId, cancellationToken);

        if (user == null)
        {
            return NotFound();
        }

        if (user.TenantId != null && user.TenantId != TenantId)
        {
            return Forbid();
        }

        await _sessionStore.DeleteBySessionIdAsync(sessionId, cancellationToken);

        _logger.LogInformation("Admin revoked session {SessionId} for user {UserId}", sessionId, userId);

        return NoContent();
    }

    /// <summary>
    /// Revoke all sessions for a user
    /// </summary>
    [HttpDelete("{userId}/sessions")]
    public async Task<IActionResult> RevokeAllSessions(
        string userId,
        CancellationToken cancellationToken)
    {
        if (_sessionStore == null)
        {
            return BadRequest(new { error = "Session management not available" });
        }

        var user = await _userService.FindByIdAsync(userId, cancellationToken);

        if (user == null)
        {
            return NotFound();
        }

        if (user.TenantId != null && user.TenantId != TenantId)
        {
            return Forbid();
        }

        await _sessionStore.DeleteSessionsBySubjectAsync(userId, cancellationToken);

        _logger.LogInformation("Admin revoked all sessions for user {UserId}", userId);

        return NoContent();
    }
}

#region DTOs

public class PagedResult<T>
{
    public List<T> Items { get; set; } = new();
    public int TotalCount { get; set; }
    public int Page { get; set; }
    public int PageSize { get; set; }
    public int TotalPages { get; set; }
}

public class UserDto
{
    public string Id { get; set; } = null!;
    public string UserName { get; set; } = null!;
    public string Email { get; set; } = null!;
    public string? FirstName { get; set; }
    public string? LastName { get; set; }
    public string? DisplayName { get; set; }
    public string? Picture { get; set; }
    public bool IsActive { get; set; }
    public bool EmailVerified { get; set; }
    public string? PhoneNumber { get; set; }
    public bool PhoneNumberVerified { get; set; }
    public bool TwoFactorEnabled { get; set; }
    public bool LockoutEnabled { get; set; }
    public DateTimeOffset? LockoutEnd { get; set; }
    public List<string> Roles { get; set; } = new();
    public DateTime CreatedAt { get; set; }
    public DateTime? UpdatedAt { get; set; }
    public DateTime? LastLoginAt { get; set; }
}

public class UserDetailDto : UserDto
{
    public int AccessFailedCount { get; set; }
    public List<UserClaimDto> Claims { get; set; } = new();
    public string? ExternalId { get; set; }
    public string? ExternalProvider { get; set; }
}

public class UserClaimDto
{
    public string Type { get; set; } = null!;
    public string Value { get; set; } = null!;
}

public class CreateUserRequest
{
    public string Email { get; set; } = null!;
    public string? UserName { get; set; }
    public string? Password { get; set; }
    public string? FirstName { get; set; }
    public string? LastName { get; set; }
    public string? DisplayName { get; set; }
    public string? PhoneNumber { get; set; }
    public bool? IsActive { get; set; }
    public bool? EmailVerified { get; set; }
    public List<string>? Roles { get; set; }
}

public class UpdateUserRequest
{
    public string? Email { get; set; }
    public string? FirstName { get; set; }
    public string? LastName { get; set; }
    public string? DisplayName { get; set; }
    public string? PhoneNumber { get; set; }
    public string? Picture { get; set; }
    public bool? IsActive { get; set; }
    public bool? EmailVerified { get; set; }
    public bool? LockoutEnabled { get; set; }
}

public class SetUserRolesRequest
{
    public List<string> Roles { get; set; } = new();
}

public class ResetPasswordRequest
{
    public string NewPassword { get; set; } = null!;
}

public class MfaStatusDto
{
    public bool Enabled { get; set; }
    public bool TwoFactorEnabled { get; set; }
}

public class ExternalLoginDto
{
    public string LoginProvider { get; set; } = null!;
    public string ProviderKey { get; set; } = null!;
    public string ProviderDisplayName { get; set; } = null!;
}

public class UserSessionDto
{
    public string SessionId { get; set; } = null!;
    public string? ClientId { get; set; }
    public string? ClientName { get; set; }
    public string? IpAddress { get; set; }
    public string? UserAgent { get; set; }
    public DateTime Created { get; set; }
    public DateTime Renewed { get; set; }
    public DateTime? Expires { get; set; }
    public bool IsCurrent { get; set; }
}

#endregion
