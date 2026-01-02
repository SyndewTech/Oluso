using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Logging;
using Oluso.Core.Api;
using Oluso.Core.Domain.Interfaces;
using Oluso.Core.Events;
using Oluso.Core.Services;

namespace Oluso.Account.Controllers;

/// <summary>
/// Account API for managing user's own profile
/// </summary>
[Route("api/account/profile")]
public class ProfileController : AccountBaseController
{
    private readonly IOlusoUserService _userService;
    private readonly IOlusoEventService _eventService;
    private readonly ILogger<ProfileController> _logger;

    public ProfileController(
        ITenantContext tenantContext,
        IOlusoUserService userService,
        IOlusoEventService eventService,
        ILogger<ProfileController> logger) : base(tenantContext)
    {
        _userService = userService;
        _eventService = eventService;
        _logger = logger;
    }

    /// <summary>
    /// Get the current user's profile
    /// </summary>
    [HttpGet]
    public async Task<ActionResult<UserProfileDto>> GetProfile(CancellationToken cancellationToken)
    {
        var user = await _userService.FindByIdAsync(UserId, cancellationToken);

        if (user == null)
        {
            return NotFound(new { error = "User not found" });
        }

        return Ok(MapToProfileDto(user));
    }

    /// <summary>
    /// Update the current user's profile
    /// </summary>
    [HttpPut]
    public async Task<ActionResult<UserProfileDto>> UpdateProfile(
        [FromBody] UpdateProfileRequest request,
        CancellationToken cancellationToken)
    {
        var updateRequest = new UpdateUserRequest
        {
            FirstName = request.FirstName,
            LastName = request.LastName,
            PhoneNumber = request.PhoneNumber,
            Picture = request.Picture
        };

        var result = await _userService.UpdateUserAsync(UserId, updateRequest, cancellationToken);

        if (!result.Succeeded)
        {
            return BadRequest(new { errors = result.Errors ?? new[] { result.Error ?? "Update failed" } });
        }

        _logger.LogInformation("User {UserId} updated their profile", UserId);

        await _eventService.RaiseAsync(new UserProfileUpdatedEvent
        {
            SubjectId = UserId,
            TenantId = TenantId,
            IpAddress = ClientIp
        }, cancellationToken);

        var user = result.User ?? await _userService.FindByIdAsync(UserId, cancellationToken);

        return Ok(MapToProfileDto(user!));
    }

    private static UserProfileDto MapToProfileDto(OlusoUserInfo user) => new()
    {
        Id = user.Id,
        Email = user.Email ?? "",
        UserName = user.Username,
        FirstName = user.FirstName,
        LastName = user.LastName,
        DisplayName = user.DisplayName ?? $"{user.FirstName} {user.LastName}".Trim(),
        Picture = user.Picture,
        PhoneNumber = user.PhoneNumber,
        EmailVerified = user.EmailVerified,
        PhoneNumberVerified = user.PhoneNumberVerified
    };

    /// <summary>
    /// Get tenants the user belongs to (for multi-tenant users)
    /// </summary>
    [HttpGet("tenants")]
    public async Task<ActionResult<UserTenantsDto>> GetTenants(CancellationToken cancellationToken)
    {
        var tenantIds = GetUserTenantIds();
        var tenantStore = HttpContext.RequestServices.GetService(typeof(ITenantStore)) as ITenantStore;

        var tenants = new List<TenantInfoDto>();

        if (tenantStore != null)
        {
            foreach (var tenantId in tenantIds)
            {
                var tenant = await tenantStore.GetByIdAsync(tenantId, cancellationToken);
                if (tenant != null && tenant.Enabled)
                {
                    tenants.Add(new TenantInfoDto
                    {
                        Id = tenant.Id,
                        Name = tenant.Name,
                        DisplayName = tenant.DisplayName,
                        LogoUrl = tenant.Branding?.LogoUrl
                    });
                }
            }
        }

        return Ok(new UserTenantsDto
        {
            CurrentTenantId = TenantId,
            Tenants = tenants,
            IsMultiTenant = tenants.Count > 1
        });
    }
}

#region DTOs

public class UserProfileDto
{
    public string Id { get; set; } = null!;
    public string Email { get; set; } = null!;
    public string? UserName { get; set; }
    public string? FirstName { get; set; }
    public string? LastName { get; set; }
    public string? DisplayName { get; set; }
    public string? Picture { get; set; }
    public string? PhoneNumber { get; set; }
    public bool EmailVerified { get; set; }
    public bool PhoneNumberVerified { get; set; }
}

public class UpdateProfileRequest
{
    public string? FirstName { get; set; }
    public string? LastName { get; set; }
    public string? PhoneNumber { get; set; }
    public string? Picture { get; set; }
}

public class UserTenantsDto
{
    public string? CurrentTenantId { get; set; }
    public List<TenantInfoDto> Tenants { get; set; } = new();
    public bool IsMultiTenant { get; set; }
}

public class TenantInfoDto
{
    public string Id { get; set; } = null!;
    public string Name { get; set; } = null!;
    public string? DisplayName { get; set; }
    public string? LogoUrl { get; set; }
}

#endregion

#region Events

public class UserProfileUpdatedEvent : OlusoEvent
{
    public override string Category => "Account";
    public override string EventType => "UserProfileUpdated";
    public string? SubjectId { get; set; }
    public string? IpAddress { get; set; }
}

#endregion
