using Microsoft.AspNetCore.Mvc;
using Oluso.Admin.Authorization;
using Oluso.Core.Authorization;
using Oluso.Core.Api;
using Oluso.Core.Domain.Interfaces;
using Oluso.Core.Events;
using Oluso.Core.Services;
using Oluso.Admin.Dto.Users;

namespace Oluso.Admin.Controllers;

/// <summary>
/// Admin API for managing users within a tenant
/// </summary>
[Route("api/admin/users")]
public class UsersController : AdminBaseController
{
    private readonly IOlusoUserService _userService;
    private readonly IOlusoEventService _eventService;
    private readonly IRoleStore _roleStore;
    private readonly ITenantMembershipStore _tenantMembershipStore;
    private readonly ITenantStore _tenantStore;
    private readonly IOrganizationMembershipStore? _organizationMembershipStore;
    private readonly IServerSideSessionStore? _sessionStore;
    private readonly IModuleAdminAssignmentNotifier _moduleAdminNotifier;
    private readonly ILogger<UsersController> _logger;

    public UsersController(
        ITenantContext tenantContext,
        IOlusoUserService userService,
        IOlusoEventService eventService,
        IRoleStore roleStore,
        ITenantMembershipStore tenantMembershipStore,
        ITenantStore tenantStore,
        IModuleAdminAssignmentNotifier moduleAdminNotifier,
        ILogger<UsersController> logger,
        IOrganizationMembershipStore? organizationMembershipStore = null,
        IServerSideSessionStore? sessionStore = null)
        : base(tenantContext)
    {
        _organizationMembershipStore = organizationMembershipStore;
        _userService = userService;
        _eventService = eventService;
        _roleStore = roleStore;
        _tenantMembershipStore = tenantMembershipStore;
        _tenantStore = tenantStore;
        _sessionStore = sessionStore;
        _moduleAdminNotifier = moduleAdminNotifier;
        _logger = logger;
    }

    /// <summary>
    /// Rejects profile-level changes to a user whose record lives in another
    /// tenant. They work here through a TenantMembership, but the record is
    /// owned by their home tenant — editing it from here would silently change
    /// what that tenant's admins see. Tenant-scoped changes (roles, seats,
    /// tenant access) stay available; only identity-level ones are refused.
    /// </summary>
    private ActionResult? RejectIfNotHomeTenant(OlusoUserInfo user)
    {
        if (user.TenantId == TenantId) return null;

        return StatusCode(403, new
        {
            error = "user_owned_by_another_tenant",
            error_description =
                "This user belongs to another tenant and has access here through tenant membership. " +
                "Their profile, credentials and sessions are managed from their home tenant.",
            ownerTenantId = user.TenantId
        });
    }

    /// <summary>
    /// Whether the user belongs to the current tenant — as their home tenant, or
    /// through a TenantMembership granted afterwards. Comparing TenantId alone
    /// rejected everyone added to the tenant later, who show up in its user list
    /// but then couldn't be acted on.
    /// </summary>
    private async Task<bool> IsUserInCurrentTenantAsync(OlusoUserInfo user, CancellationToken cancellationToken)
    {
        if (user.TenantId == TenantId) return true;
        if (string.IsNullOrEmpty(TenantId)) return false;

        return await _tenantMembershipStore.GetByTenantAndUserAsync(TenantId, user.Id, cancellationToken) != null;
    }

    /// <summary>
    /// Get all users for the current tenant
    /// </summary>
    [HttpGet]
    [RequirePermission(AdminPermissions.UsersRead)]
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

        // The list can include users whose home tenant is a sibling — they reach
        // this tenant through a TenantMembership. Resolve those tenant names once
        // per distinct tenant so the UI can show whose record it is.
        var externalTenantIds = usersResult.Users
            .Select(u => u.TenantId)
            .Where(id => !string.IsNullOrEmpty(id) && id != TenantId)
            .Distinct()
            .ToList();

        var ownerTenantNames = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (var id in externalTenantIds)
        {
            var owner = await _tenantStore.GetByIdAsync(id!);
            if (owner != null) ownerTenantNames[id!] = owner.DisplayName ?? owner.Name;
        }

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
                LockoutEnabled = u.LockoutEnabled,
                LockoutEnd = u.LockoutEnd,
                CreatedAt = u.CreatedAt,
                UpdatedAt = u.UpdatedAt,
                Roles = u.Roles?.ToList() ?? new List<string>(),
                LastLoginAt = u.LastLoginAt,
                HasPassword = u.HasPassword,
                IsFederated = u.IsFederated,
                LinkedProviders = u.LinkedProviders?.ToList() ?? new List<string>(),
                TenantId = u.TenantId,
                IsExternalToTenant = u.TenantId != TenantId,
                OwnerTenantName = u.TenantId != null && ownerTenantNames.TryGetValue(u.TenantId, out var owner)
                    ? owner
                    : null
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
    [RequirePermission(AdminPermissions.UsersRead)]
    public async Task<ActionResult<UserDetailDto>> GetUser(string userId, CancellationToken cancellationToken)
    {
        var user = await _userService.FindByIdAsync(userId, cancellationToken);

        if (user == null)
        {
            return NotFound();
        }

        // Check tenant access
        if (!await IsUserInCurrentTenantAsync(user, cancellationToken))
        {
            return Forbid();
        }

        var roles = await RoleNamesInScopeAsync(userId, ResolveRoleScopeTenantId(user), cancellationToken);
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
            LockoutEnabled = user.LockoutEnabled,
            LockoutEnd = user.LockoutEnd,
            AccessFailedCount = user.AccessFailedCount,
            HasPassword = await _userService.HasPasswordAsync(userId, cancellationToken),
            CreatedAt = user.CreatedAt,
            UpdatedAt = user.UpdatedAt,
            Roles = roles.ToList(),
            Claims = claims.Select(c => new UserClaimDto { Type = c.Type, Value = c.Value }).ToList(),
            CustomProperties = user.CustomProperties?.ToDictionary(k => k.Key, v => v.Value),
            LastLoginAt = user.LastLoginAt,
            TenantId = user.TenantId,
            IsExternalToTenant = user.TenantId != TenantId,
            OwnerTenantName = user.TenantId != TenantId && user.TenantId != null
                ? (await _tenantStore.GetByIdAsync(user.TenantId))?.Name
                : null
        });
    }

    /// <summary>
    /// Create a new user
    /// </summary>
    [HttpPost]
    [RequirePermission(AdminPermissions.UsersWrite)]
    public async Task<ActionResult<UserDto>> CreateUser(
        [FromBody] Dto.Users.CreateUserRequest request,
        [FromServices] IIssuerResolver issuerResolver,
        [FromServices] ITenantSettingsProvider tenantSettings,
        [FromServices] IEmailSender? emailSender,
        [FromServices] IPlatformEmailTemplateService? platformTemplates,
        [FromServices] ITenantEmailDomainStore emailDomains,
        CancellationToken cancellationToken)
    {
        if (request.SendInvite && !string.IsNullOrEmpty(request.Password))
        {
            return BadRequest(new { errors = new[] { "Leave the password empty to send an invite: the user sets their own." } });
        }

        // Saying an address is verified is trusted only for the tenant's own verified domains.
        var markVerified = request.EmailVerified == true;
        if (markVerified && !await emailDomains.IsVerifiedForTenantAsync(TenantId ?? "", request.Email, cancellationToken))
        {
            return BadRequest(new { errors = new[] { "An address can be marked verified only at an email domain this organization has verified. Leave it unverified: the person confirms it with a code when they sign in." } });
        }

        // Validate the requested roles before anything is written, so a bad
        // role id never leaves behind a half-created user.
        RoleAssignmentPlan? rolePlan = null;
        if (request.RoleIds is { Count: > 0 })
        {
            rolePlan = await PlanRoleAssignmentAsync(
                TenantId, null, Array.Empty<Core.Domain.Entities.OlusoRole>(), request.RoleIds, cancellationToken);
            if (rolePlan.Error != null)
            {
                return rolePlan.Error;
            }
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

        using var origin = EventOriginScope.Begin(Origin);
        var result = await _userService.CreateUserAsync(createRequest, cancellationToken);

        if (!result.Succeeded)
        {
            return BadRequest(new { errors = result.MessagesOr("Unknown error") });
        }

        // Create TenantMembership for the user's home tenant
        if (!string.IsNullOrEmpty(TenantId) && !string.IsNullOrEmpty(result.UserId))
        {
            await _tenantMembershipStore.AddAsync(new Core.Domain.Entities.TenantMembership
            {
                TenantId = TenantId,
                UserId = result.UserId,
                InvitedByUserId = AdminUserId
            }, cancellationToken);
        }

        _logger.LogInformation("Created user {UserId} ({Email}) for tenant {TenantId}",
            result.UserId, request.Email, TenantId);

        if (markVerified)
        {
            await _userService.SetEmailVerifiedAsync(result.UserId!, true, cancellationToken);
        }

        if (rolePlan != null && rolePlan.Target.Count > 0)
        {
            var roleResult = await _userService.SetRolesByIdAsync(result.UserId!, rolePlan.Target.Keys, cancellationToken);
            if (!roleResult.Succeeded)
            {
                return BadRequest(new { errors = roleResult.MessagesOr("Unknown error") });
            }

            await RaiseRoleChangesAsync(
                TenantId, result.UserId!, Array.Empty<Core.Domain.Entities.OlusoRole>(), rolePlan.Target.Values.ToList(), cancellationToken);
            await _moduleAdminNotifier.NotifyAsync(
                TenantId, result.UserId!, rolePlan.Target.Values, Actor(), cancellationToken);
        }

        string? inviteError = null;
        if (request.SendInvite)
        {
            inviteError = await SendInviteAsync(result.UserId!, request.Email,
                $"{request.FirstName} {request.LastName}".Trim(), issuerResolver, tenantSettings, emailSender, platformTemplates, cancellationToken);
        }

        var user = await _userService.FindByIdAsync(result.UserId!, cancellationToken);
        var roles = await RoleNamesInScopeAsync(result.UserId!, TenantId, cancellationToken);

        return CreatedAtAction(nameof(GetUser), new { userId = result.UserId }, new UserDto
        {
            InviteError = inviteError,
            Id = result.UserId!,
            UserName = user?.Username ?? request.UserName ?? request.Email,
            Email = request.Email,
            FirstName = request.FirstName,
            LastName = request.LastName,
            DisplayName = request.DisplayName,
            IsActive = true,
            EmailVerified = user?.EmailVerified ?? false,
            Roles = roles.ToList()
        });
    }

    /// <summary>
    /// Sends the set-password invite again to a user who hasn't set a password yet, with a fresh link valid for the
    /// tenant's account-link lifetime.
    /// </summary>
    [HttpPost("{userId}/invite")]
    [RequirePermission(AdminPermissions.UsersWrite)]
    public async Task<IActionResult> ResendInvite(
        string userId,
        [FromServices] IIssuerResolver issuerResolver,
        [FromServices] ITenantSettingsProvider tenantSettings,
        [FromServices] IEmailSender? emailSender,
        [FromServices] IPlatformEmailTemplateService? platformTemplates,
        CancellationToken cancellationToken)
    {
        var user = await _userService.FindByIdAsync(userId, cancellationToken);
        if (user == null) return NotFound();
        if (!await IsUserInCurrentTenantAsync(user, cancellationToken)) return Forbid();
        var ownershipError = RejectIfNotHomeTenant(user);
        if (ownershipError != null) return ownershipError;
        if (string.IsNullOrEmpty(user.Email))
            return BadRequest(new { errors = new[] { "This user has no email address to send the invite to." } });
        if (await _userService.HasPasswordAsync(userId, cancellationToken))
            return Conflict(new { errors = new[] { "This user has already set a password. Use Reset password instead." } });

        var error = await SendInviteAsync(userId, user.Email, $"{user.FirstName} {user.LastName}".Trim(),
            issuerResolver, tenantSettings, emailSender, platformTemplates, cancellationToken);
        if (error != null) return StatusCode(Microsoft.AspNetCore.Http.StatusCodes.Status502BadGateway, new { errors = new[] { error } });

        _logger.LogInformation("Resent the set-password invite to user {UserId} in tenant {TenantId}", userId, TenantId);
        return Ok(new { message = "Invite sent" });
    }

    /// <summary>
    /// Emails the user a link to set their password: a reset token on this tenant's own sign-in host, valid for the
    /// tenant's account-link lifetime (stated in the email). Returns why it couldn't be sent, or null when it was.
    /// </summary>
    private async Task<string?> SendInviteAsync(string userId, string email, string? userName,
        IIssuerResolver issuerResolver, ITenantSettingsProvider tenantSettings, IEmailSender? emailSender,
        IPlatformEmailTemplateService? platformTemplates, CancellationToken cancellationToken)
    {
        if (emailSender == null)
            return "Email isn't set up, so the invite couldn't be sent.";

        var token = await _userService.GeneratePasswordResetTokenAsync(userId, cancellationToken);
        if (token == null)
            return "This account signs in through another provider, so it has no password to set.";

        var tenant = await _tenantStore.GetByIdAsync(TenantId!, cancellationToken);
        if (tenant == null)
            return "The tenant could not be found, so the invite link couldn't be built.";

        var issuer = (await issuerResolver.GetIssuerForTenantAsync(tenant, cancellationToken)).TrimEnd('/');
        var link = $"{issuer}/account/reset-password?token={Uri.EscapeDataString(token)}&email={Uri.EscapeDataString(email)}";
        var expiresIn = Durations.Describe(TimeSpan.FromMinutes(
            (await tenantSettings.GetAccountEmailSettingsAsync(tenant.Id, cancellationToken)).AccountLinkLifetimeMinutes));

        var rendered = platformTemplates == null ? null : await platformTemplates.RenderAsync(
            PlatformTemplateNames.AccountInvite,
            new Dictionary<string, object>
            {
                [PlatformTemplateScopeKeys.ResetLink] = link,
                [PlatformTemplateScopeKeys.ExpiresIn] = expiresIn,
                [PlatformTemplateScopeKeys.UserEmail] = email,
                [PlatformTemplateScopeKeys.UserName] = userName ?? string.Empty,
                ["inviter_name"] = AdminUserName ?? string.Empty,
            },
            cancellationToken);

        string subject, html;
        if (rendered != null)
        {
            subject = rendered.Subject;
            html = rendered.HtmlBody;
        }
        else
        {
            var name = System.Net.WebUtility.HtmlEncode(tenant.DisplayName ?? tenant.Name);
            var accent = BrandedEmailLayout.DefaultAccent;
            subject = $"You're invited to {tenant.DisplayName ?? tenant.Name}: set your password";
            html = BrandedEmailLayout.Document(
                title: $"Welcome to {name}",
                preheader: "An account has been created for you. Set your password to sign in.",
                accent: accent,
                brand: BrandedEmailLayout.BrandMark(new EmailBrand(tenant.DisplayName ?? tenant.Name, accent, null)),
                eyebrow: "You're invited",
                heading: $"Welcome to {name}",
                rows:
                    BrandedEmailLayout.Lead($"An account has been created for you at {BrandedEmailLayout.Strong(name)}. Set your password to sign in.") +
                    BrandedEmailLayout.Pill("Your sign-in", System.Net.WebUtility.HtmlEncode(email), accent) +
                    BrandedEmailLayout.Button(link, "Set your password &rarr;", accent) +
                    BrandedEmailLayout.Note($"This link expires in <strong>{expiresIn}</strong>. If it has expired, ask your administrator to send the invite again.") +
                    BrandedEmailLayout.Divider() +
                    BrandedEmailLayout.LinkFallback(link, accent),
                footnote: $"You received this because an account was created for this address at {name}.");
        }

        var sent = await emailSender.SendAsync(email, subject, html, cancellationToken);
        if (!sent.Success)
        {
            _logger.LogWarning("Set-password invite to user {UserId} failed: {Error}", userId, sent.Error);
            return $"The invite email couldn't be sent: {sent.Error}";
        }
        return null;
    }

    /// <summary>
    /// Update a user
    /// </summary>
    [HttpPut("{userId}")]
    [RequirePermission(AdminPermissions.UsersWrite)]
    public async Task<ActionResult<UserDto>> UpdateUser(
        string userId,
        [FromBody] UserDetailDto request,
        [FromServices] ITenantEmailDomainStore emailDomains,
        CancellationToken cancellationToken)
    {
        var user = await _userService.FindByIdAsync(userId, cancellationToken);

        if (user == null)
        {
            return NotFound();
        }

        // Check tenant access
        if (!await IsUserInCurrentTenantAsync(user, cancellationToken))
        {
            return Forbid();
        }

        var ownershipError = RejectIfNotHomeTenant(user);
        if (ownershipError != null) return ownershipError;

        // Marking an address verified is trusted only at the tenant's own verified domains; a new address starts
        // unverified unless it's at one. Clearing the flag on the same address is always allowed.
        var targetEmail = request.Email ?? user.Email;
        var sameMailbox = string.Equals(targetEmail, user.Email, StringComparison.OrdinalIgnoreCase);
        bool? setEmailVerified = null;
        if (request.EmailVerified && (!sameMailbox || !user.EmailVerified))
        {
            if (!await emailDomains.IsVerifiedForTenantAsync(user.TenantId ?? "", targetEmail, cancellationToken))
            {
                return BadRequest(new { errors = new[] { "An address can be marked verified only at an email domain this organization has verified. Leave it unverified: the person confirms it with a code when they sign in." } });
            }
            setEmailVerified = true;
        }
        else if (!request.EmailVerified && sameMailbox && user.EmailVerified)
        {
            setEmailVerified = false;
        }

        var updateRequest = new Oluso.Core.Services.UpdateUserRequest
        {
            Email = request.Email,
            FirstName = request.FirstName,
            LastName = request.LastName,
            PhoneNumber = request.PhoneNumber,
            Picture = request.Picture,
            CustomProperties = request.CustomProperties
        };

        using var origin = EventOriginScope.Begin(Origin);
        var result = await _userService.UpdateUserAsync(userId, updateRequest, cancellationToken);

        if (!result.Succeeded)
        {
            return BadRequest(new { errors = result.MessagesOr("Unknown error") });
        }

        // Update IsActive if changed
        if (request.IsActive != user.IsActive)
        {
            await _userService.SetUserActiveAsync(userId, request.IsActive, cancellationToken);
        }

        if (setEmailVerified is { } verified)
        {
            await _userService.SetEmailVerifiedAsync(userId, verified, cancellationToken);
        }

        _logger.LogInformation("Updated user {UserId}", userId);

        var updatedUser = await _userService.FindByIdAsync(userId, cancellationToken);

        var roles = await RoleNamesInScopeAsync(userId, ResolveRoleScopeTenantId(user), cancellationToken);

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
            PhoneNumber = updatedUser?.PhoneNumber,
            PhoneNumberVerified = updatedUser?.PhoneNumberVerified ?? false,
            TwoFactorEnabled = updatedUser?.TwoFactorEnabled ?? false,
            LockoutEnabled = updatedUser?.LockoutEnabled ?? false,
            LockoutEnd = updatedUser?.LockoutEnd,
            CreatedAt = updatedUser?.CreatedAt ?? DateTime.UtcNow,
            UpdatedAt = updatedUser?.UpdatedAt,
            LastLoginAt = updatedUser?.LastLoginAt,
            Roles = roles.ToList()
        });
    }

    /// <summary>
    /// End a user's lockout now (after too many failed sign-in attempts, or an admin lock) and
    /// clear their failed-attempt count.
    /// </summary>
    [HttpPost("{userId}/unlock")]
    [RequirePermission(AdminPermissions.UsersWrite)]
    public async Task<ActionResult<UserDetailDto>> UnlockUser(string userId, CancellationToken cancellationToken)
    {
        var user = await _userService.FindByIdAsync(userId, cancellationToken);
        if (user == null)
        {
            return NotFound();
        }
        if (!await IsUserInCurrentTenantAsync(user, cancellationToken))
        {
            return Forbid();
        }
        var ownershipError = RejectIfNotHomeTenant(user);
        if (ownershipError != null) return ownershipError;

        if (!await _userService.UnlockAsync(userId, cancellationToken))
        {
            return BadRequest(new { error = "unlock_failed", error_description = "The account could not be unlocked" });
        }

        _logger.LogInformation("User {UserId} unlocked by {AdminId}", userId, User.FindFirst("sub")?.Value);
        return await GetUser(userId, cancellationToken);
    }

    /// <summary>
    /// Lock a user out until a time, or until unlocked, and end their active sessions.
    /// </summary>
    [HttpPost("{userId}/lock")]
    [RequirePermission(AdminPermissions.UsersWrite)]
    public async Task<ActionResult<UserDetailDto>> LockUser(
        string userId,
        [FromBody] LockUserRequest request,
        CancellationToken cancellationToken)
    {
        var user = await _userService.FindByIdAsync(userId, cancellationToken);
        if (user == null)
        {
            return NotFound();
        }
        if (!await IsUserInCurrentTenantAsync(user, cancellationToken))
        {
            return Forbid();
        }
        var ownershipError = RejectIfNotHomeTenant(user);
        if (ownershipError != null) return ownershipError;

        if (userId == User.FindFirst("sub")?.Value)
        {
            return BadRequest(new { error = "invalid_request", error_description = "You cannot lock your own account" });
        }
        if (request.LockoutEnd is { } end && end <= DateTimeOffset.UtcNow)
        {
            return BadRequest(new { error = "invalid_request", error_description = "lockoutEnd must be in the future" });
        }

        if (!await _userService.LockAsync(userId, request.LockoutEnd, cancellationToken))
        {
            return BadRequest(new { error = "lock_failed", error_description = "The account could not be locked" });
        }

        if (_sessionStore != null)
        {
            await _sessionStore.DeleteSessionsBySubjectAsync(userId, cancellationToken);
        }

        _logger.LogInformation("User {UserId} locked until {LockoutEnd} by {AdminId}",
            userId, request.LockoutEnd?.ToString("o") ?? "unlocked by an admin", User.FindFirst("sub")?.Value);
        return await GetUser(userId, cancellationToken);
    }

    /// <summary>
    /// Permanently delete a user. Needs <c>users.delete</c> granted by name: wildcards
    /// and the SuperAdmin shortcut don't cover it.
    /// </summary>
    [HttpDelete("{userId}")]
    [RequirePermission(AdminPermissions.UsersDelete)]
    public async Task<IActionResult> DeleteUser(string userId, CancellationToken cancellationToken)
    {
        var user = await _userService.FindByIdInTenantAsync(userId, TenantId, cancellationToken);

        if (user == null)
        {
            return NotFound();
        }

        var ownershipError = RejectIfNotHomeTenant(user);
        if (ownershipError != null) return ownershipError;

        if (userId == User.FindFirst("sub")?.Value)
        {
            return BadRequest(new { error = "invalid_request", error_description = "You cannot delete your own account" });
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

        _logger.LogInformation("User {UserId} deleted by {AdminId}", userId, AdminUserId);

        return NoContent();
    }

    /// <summary>
    /// Get roles assigned to a user
    /// </summary>
    [HttpGet("{userId}/roles")]
    [RequirePermission(AdminPermissions.UsersRead)]
    public async Task<ActionResult<IEnumerable<UserRoleDto>>> GetUserRoles(string userId, CancellationToken cancellationToken)
    {
        var user = await _userService.FindByIdAsync(userId, cancellationToken);

        if (user == null)
        {
            return NotFound();
        }

        // Check tenant access
        if (!await IsUserInCurrentTenantAsync(user, cancellationToken))
        {
            return Forbid();
        }

        var roles = await _roleStore.GetUserRolesInTenantAsync(userId, ResolveRoleScopeTenantId(user), cancellationToken);
        return Ok(roles.Select(ToUserRoleDto));
    }

    /// <summary>
    /// Whether the user is Owner or Admin of any organization — the state that
    /// entitles them to the OrgAdmin role.
    /// </summary>
    private async Task<bool> HasOrganizationLeadershipAsync(string userId, CancellationToken cancellationToken)
    {
        if (_organizationMembershipStore == null)
        {
            // Can't tell, so don't strip on a guess.
            return true;
        }

        var memberships = await _organizationMembershipStore.GetByUserAsync(userId, cancellationToken);
        return memberships.Any(m =>
            m.Role == Core.Domain.Entities.OrganizationRole.Owner
            || m.Role == Core.Domain.Entities.OrganizationRole.Admin);
    }

    /// <summary>
    /// Set roles for a user (replaces existing roles)
    /// </summary>
    /// <remarks>
    /// SECURITY: System roles (SuperAdmin, SystemAdmin) can only be assigned by SuperAdmin users.
    /// Tenant admins can only assign tenant-scoped roles.
    /// </remarks>
    [HttpPut("{userId}/roles")]
    [RequirePermission(AdminPermissions.UsersManageRoles)]
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

        // Check tenant access - can only modify users in own tenant.
        // SuperAdmins are cross-tenant by definition; without this they can't
        // grant OrgAdmin from the org screen, because that screen never switches
        // the X-Tenant-ID header to the target user's tenant.
        if (!IsSuperAdmin && !await IsUserInCurrentTenantAsync(user, cancellationToken))
        {
            return Forbid();
        }

        var scopeTenantId = ResolveRoleScopeTenantId(user);
        var heldRows = (await _roleStore.GetUserRolesInTenantAsync(userId, scopeTenantId, cancellationToken)).ToList();

        var plan = await PlanRoleAssignmentAsync(scopeTenantId, userId, heldRows, request.RoleIds, cancellationToken);
        if (plan.Error != null)
        {
            return plan.Error;
        }

        var result = await _userService.SetRolesByIdAsync(userId, plan.Target.Keys, cancellationToken);

        if (!result.Succeeded)
        {
            return BadRequest(new { errors = result.MessagesOr("Unknown error") });
        }

        var finalRoles = plan.Target.Values.ToList();

        _logger.LogInformation(
            "Admin {AdminUserId} set roles for user {UserId}: {Roles}",
            AdminUserId, userId, string.Join(", ", finalRoles.Select(r => r.Name)));

        await RaiseRoleChangesAsync(scopeTenantId, userId, heldRows, finalRoles, cancellationToken);

        var heldIds = heldRows.Select(r => r.Id).ToHashSet(StringComparer.Ordinal);
        var finalIds = finalRoles.Select(r => r.Id).ToHashSet(StringComparer.Ordinal);
        await _moduleAdminNotifier.NotifyAsync(
            scopeTenantId!, userId, finalRoles.Where(r => !heldIds.Contains(r.Id)), Actor(), cancellationToken);
        await _moduleAdminNotifier.NotifyRemovedAsync(
            scopeTenantId!, userId, heldRows.Where(r => !finalIds.Contains(r.Id)), Actor(), cancellationToken);

        return Ok(new
        {
            message = "Roles updated successfully",
            roleIds = finalRoles.Select(r => r.Id),
            roles = finalRoles.Select(r => r.Name)
        });
    }

    private sealed record RoleAssignmentPlan(
        ActionResult? Error,
        Dictionary<string, Core.Domain.Entities.OlusoRole> Target);

    /// <summary>
    /// Resolves requested role ids to rows and applies the assignment policy.
    /// The result is the exact set of rows the user should hold in scope, or
    /// the error response to return. <paramref name="targetUserId"/> is null
    /// while the user is still being created.
    /// </summary>
    private async Task<RoleAssignmentPlan> PlanRoleAssignmentAsync(
        string? scopeTenantId,
        string? targetUserId,
        IReadOnlyList<Core.Domain.Entities.OlusoRole> heldRows,
        IEnumerable<string> roleIds,
        CancellationToken cancellationToken)
    {
        var empty = new Dictionary<string, Core.Domain.Entities.OlusoRole>(StringComparer.Ordinal);
        var userLabel = targetUserId ?? "(new user)";

        // Ids come in, rows come out. A row outside this tenant is reported as
        // unknown, so the caller learns nothing about other tenants' roles.
        var requestedIds = roleIds
            .Where(id => !string.IsNullOrWhiteSpace(id))
            .Distinct(StringComparer.Ordinal)
            .ToList();
        var requestedRows = (await _roleStore.GetByIdsAsync(requestedIds, cancellationToken))
            .Where(r => r.TenantId == scopeTenantId || r.TenantId == null)
            .ToList();
        var unknownIds = requestedIds.Except(requestedRows.Select(r => r.Id), StringComparer.Ordinal).ToList();
        if (unknownIds.Count > 0)
        {
            return new RoleAssignmentPlan(
                BadRequest(new { error = $"Unknown role id(s) for this tenant: {string.Join(", ", unknownIds)}" }),
                empty);
        }

        var currentUserRoles = heldRows.Select(r => r.Name!).ToList();

        // Derived roles are owned by the system, so they are neither granted nor
        // revoked here: asking for one the user doesn't hold is refused, and one
        // they do hold survives a request that omits it. Without the second half,
        // any ordinary role edit would strip a role the caller never chose to
        // touch, because this endpoint replaces the whole set.
        // Preserve a derived role only while the state behind it still holds.
        // A role kept unconditionally could never be shed — including by rows
        // predating the derivation, granted when OrgAdmin was assignable by hand.
        var heldDerivedRoles = currentUserRoles
            .Where(r => ReservedClaimTypes.DerivedRoles.Contains(r))
            .ToList();

        if (heldDerivedRoles.Count > 0
            && targetUserId != null
            && !await HasOrganizationLeadershipAsync(targetUserId, cancellationToken))
        {
            heldDerivedRoles.Clear();
        }

        var newDerivedRoles = requestedRows.Select(r => r.Name!)
            .Where(r => ReservedClaimTypes.DerivedRoles.Contains(r))
            .Where(r => !heldDerivedRoles.Contains(r, StringComparer.OrdinalIgnoreCase))
            .ToList();

        if (newDerivedRoles.Any())
        {
            _logger.LogWarning(
                "Admin {AdminUserId} attempted to assign derived role(s) {Roles} to user {UserId} directly",
                AdminUserId, string.Join(", ", newDerivedRoles), userLabel);

            return new RoleAssignmentPlan(
                BadRequest(new
                {
                    error = $"Cannot assign {string.Join(", ", newDerivedRoles)} directly. " +
                            "It follows organization membership — make the user an Owner or Admin of the organization instead."
                }),
                empty);
        }

        var requestedRoleRows = requestedRows
            .Where(r => !ReservedClaimTypes.DerivedRoles.Contains(r.Name!))
            .ToList();
        var requestedRoles = requestedRoleRows.Select(r => r.Name!).ToList();

        // Security check — 3-tier reserved role assignment:
        //   Tier 1: Platform roles (SuperAdmin, SystemAdmin, etc.) → SuperAdmin only
        //   Tier 2: Org-level roles (OrgAdmin) → OrgAdmin or SuperAdmin
        //   Tier 3: Tenant-level roles (TenantAdmin, Admin) → TenantAdmin, OrgAdmin, or SuperAdmin
        var isTenantAdmin = HasAnyRole("TenantAdmin", "Admin");

        var superAdminOnlyRoles = requestedRoles
            .Where(r => ReservedClaimTypes.IsSuperAdminOnlyRole(r))
            .ToList();

        if (superAdminOnlyRoles.Any() && !IsSuperAdmin)
        {
            _logger.LogWarning(
                "Non-SuperAdmin {AdminUserId} attempted to assign platform roles {Roles} to user {UserId}",
                AdminUserId, string.Join(", ", superAdminOnlyRoles), userLabel);
            return new RoleAssignmentPlan(
                BadRequest(new { error = $"Cannot assign platform roles: {string.Join(", ", superAdminOnlyRoles)}. Only SuperAdmin can assign these roles." }),
                empty);
        }

        var orgOnlyRoles = requestedRoles
            .Where(r => ReservedClaimTypes.OrgAdminAssignableRoles.Contains(r))
            .ToList();

        if (orgOnlyRoles.Any() && !IsSuperAdmin && !IsCurrentOrgAdmin)
        {
            _logger.LogWarning(
                "Non-OrgAdmin {AdminUserId} attempted to assign org-level roles {Roles} to user {UserId}",
                AdminUserId, string.Join(", ", orgOnlyRoles), userLabel);
            return new RoleAssignmentPlan(
                BadRequest(new { error = $"Cannot assign roles: {string.Join(", ", orgOnlyRoles)}. Only organization admins or SuperAdmin can assign these roles." }),
                empty);
        }

        var tenantAdminRoles = requestedRoles
            .Where(r => ReservedClaimTypes.IsTenantAdminAssignableRole(r))
            .ToList();

        if (tenantAdminRoles.Any() && !IsSuperAdmin && !IsCurrentOrgAdmin && !isTenantAdmin)
        {
            _logger.LogWarning(
                "Non-TenantAdmin {AdminUserId} attempted to assign tenant admin roles {Roles} to user {UserId}",
                AdminUserId, string.Join(", ", tenantAdminRoles), userLabel);
            return new RoleAssignmentPlan(
                BadRequest(new { error = $"Cannot assign roles: {string.Join(", ", tenantAdminRoles)}. Only tenant admins, organization admins, or SuperAdmin can assign these roles." }),
                empty);
        }

        // Prevent removing SuperAdmin from self (to avoid lockout)
        if (targetUserId != null && targetUserId == AdminUserId)
        {
            var wasSuper = currentUserRoles.Any(r => r == "SuperAdmin" || r == "SystemAdmin");
            var willBeSuper = requestedRoles.Any(r => r == "SuperAdmin" || r == "SystemAdmin");

            if (wasSuper && !willBeSuper)
            {
                return new RoleAssignmentPlan(
                    BadRequest(new { error = "Cannot remove SuperAdmin role from yourself" }),
                    empty);
            }
        }

        // Security check: Plugin-managed roles can only be modified by their managing role.
        // E.g., a module's roles (ManagedByRole = "SomeModuleAdmin") can only be added/removed by users with that role.
        var adminRoles = User.FindAll("role").Select(c => c.Value).ToHashSet(StringComparer.OrdinalIgnoreCase);
        var allTenantRoles = await _roleStore.GetRolesAsync(scopeTenantId, cancellationToken: cancellationToken);

        var rolesToSet = requestedRoleRows.ToDictionary(r => r.Id, StringComparer.Ordinal);
        foreach (var roleInfo in allTenantRoles)
        {
            // Skip roles that are system-managed (null, "System", "SuperAdmin") — already handled above
            if (roleInfo.ManagedByRole is null or "System" or "SuperAdmin")
                continue;

            // This role is managed by a plugin role (e.g., "HRAdmin")
            var isCurrentlyAssigned = heldRows.Any(h => h.Id == roleInfo.Id);
            var isRequestedAssigned = rolesToSet.ContainsKey(roleInfo.Id);
            var adminCanManage = IsSuperAdmin || adminRoles.Contains(roleInfo.ManagedByRole);

            if (isCurrentlyAssigned != isRequestedAssigned && !adminCanManage)
            {
                // Admin is trying to add/remove a role they can't manage — preserve current state
                if (isCurrentlyAssigned)
                    rolesToSet[roleInfo.Id] = roleInfo;
                else
                    rolesToSet.Remove(roleInfo.Id);
            }
        }

        foreach (var derived in heldRows.Where(h => heldDerivedRoles.Contains(h.Name!, StringComparer.OrdinalIgnoreCase)))
        {
            rolesToSet[derived.Id] = derived;
        }

        return new RoleAssignmentPlan(null, rolesToSet);
    }

    /// <summary>
    /// One assigned event per role the user gained and one removed event per
    /// role they lost in the scope tenant. The module-admin notification is
    /// raised separately.
    /// </summary>
    private async Task RaiseRoleChangesAsync(
        string? scopeTenantId,
        string userId,
        IReadOnlyCollection<Core.Domain.Entities.OlusoRole> heldRows,
        IReadOnlyCollection<Core.Domain.Entities.OlusoRole> finalRows,
        CancellationToken cancellationToken)
    {
        var heldIds = heldRows.Select(r => r.Id).ToHashSet(StringComparer.Ordinal);
        var finalIds = finalRows.Select(r => r.Id).ToHashSet(StringComparer.Ordinal);

        foreach (var role in finalRows.Where(r => !heldIds.Contains(r.Id)))
        {
            await _eventService.RaiseAsync(new UserRoleAssignedEvent
            {
                TenantId = scopeTenantId,
                SubjectId = userId,
                RoleId = role.Id,
                RoleName = role.Name ?? role.Id,
                Origin = Origin
            }, cancellationToken);
        }

        foreach (var role in heldRows.Where(r => !finalIds.Contains(r.Id)))
        {
            await _eventService.RaiseAsync(new UserRoleRemovedEvent
            {
                TenantId = scopeTenantId,
                SubjectId = userId,
                RoleId = role.Id,
                RoleName = role.Name ?? role.Id,
                Origin = Origin
            }, cancellationToken);
        }
    }

    /// <summary>
    /// Reset a user's password (admin action)
    /// </summary>
    [HttpPost("{userId}/reset-password")]
    [RequirePermission(AdminPermissions.UsersWrite)]
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
        if (!await IsUserInCurrentTenantAsync(user, cancellationToken))
        {
            return Forbid();
        }

        var ownershipError = RejectIfNotHomeTenant(user);
        if (ownershipError != null) return ownershipError;

        var result = await _userService.ResetPasswordAsync(userId, request.NewPassword, cancellationToken: cancellationToken);

        if (!result.Succeeded)
        {
            return BadRequest(new { errors = result.MessagesOr("Unknown error") });
        }

        _logger.LogInformation("Admin reset password for user {UserId}", userId);

        await _eventService.RaiseAsync(new UserPasswordChangedEvent
        {
            TenantId = TenantId,
            SubjectId = userId,
            ChangedByAdmin = true,
            Origin = Origin
        }, cancellationToken);

        return Ok(new { message = "Password reset successfully" });
    }

    /// <summary>
    /// Check if user has MFA enabled
    /// </summary>
    [HttpGet("{userId}/mfa")]
    [RequirePermission(AdminPermissions.UsersRead)]
    public async Task<ActionResult<AdminMfaStatusDto>> GetMfaStatus(string userId, CancellationToken cancellationToken)
    {
        var user = await _userService.FindByIdAsync(userId, cancellationToken);

        if (user == null)
        {
            return NotFound();
        }

        if (!await IsUserInCurrentTenantAsync(user, cancellationToken))
        {
            return Forbid();
        }

        var hasMfa = await _userService.HasMfaEnabledAsync(userId, cancellationToken);

        return Ok(new AdminMfaStatusDto
        {
            Enabled = hasMfa,
            TwoFactorEnabled = user.TwoFactorEnabled
        });
    }

    /// <summary>
    /// Get external logins linked to a user
    /// </summary>
    [HttpGet("{userId}/external-logins")]
    [RequirePermission(AdminPermissions.UsersRead)]
    public async Task<ActionResult<IEnumerable<AdminExternalLoginDto>>> GetExternalLogins(
        string userId,
        CancellationToken cancellationToken)
    {
        var user = await _userService.FindByIdAsync(userId, cancellationToken);

        if (user == null)
        {
            return NotFound();
        }

        if (!await IsUserInCurrentTenantAsync(user, cancellationToken))
        {
            return Forbid();
        }

        var logins = await _userService.GetExternalLoginsAsync(userId, cancellationToken);

        return Ok(logins.Select(l => new AdminExternalLoginDto
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
    [RequirePermission(AdminPermissions.UsersWrite)]
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

        if (!await IsUserInCurrentTenantAsync(user, cancellationToken))
        {
            return Forbid();
        }

        var ownershipError = RejectIfNotHomeTenant(user);
        if (ownershipError != null) return ownershipError;

        var result = await _userService.RemoveExternalLoginAsync(userId, provider, providerKey, cancellationToken);

        if (!result.Succeeded)
        {
            return BadRequest(new { error = result.MessageOr("Failed to remove external login") });
        }

        _logger.LogInformation("Admin removed external login {Provider} from user {UserId}", provider, userId);

        return NoContent();
    }

    /// <summary>
    /// Get active sessions for a user
    /// </summary>
    [HttpGet("{userId}/sessions")]
    [RequirePermission(AdminPermissions.SessionsRead)]
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

        if (!await IsUserInCurrentTenantAsync(user, cancellationToken))
        {
            return Forbid();
        }

        var sessions = await _sessionStore.GetUserSessionsAsync(userId, cancellationToken);
        var now = DateTime.UtcNow;

        return Ok(sessions.Where(s => s.Expires == null || s.Expires > now).Select(s => new UserSessionDto
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
    [RequirePermission(AdminPermissions.SessionsRevoke)]
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

        if (!await IsUserInCurrentTenantAsync(user, cancellationToken))
        {
            return Forbid();
        }

        var ownershipError = RejectIfNotHomeTenant(user);
        if (ownershipError != null) return ownershipError;

        await _sessionStore.DeleteBySessionIdAsync(sessionId, cancellationToken);

        _logger.LogInformation("Admin revoked session {SessionId} for user {UserId}", sessionId, userId);

        return NoContent();
    }

    /// <summary>
    /// Revoke all sessions for a user
    /// </summary>
    [HttpDelete("{userId}/sessions")]
    [RequirePermission(AdminPermissions.SessionsRevoke)]
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

        if (!await IsUserInCurrentTenantAsync(user, cancellationToken))
        {
            return Forbid();
        }

        var ownershipError = RejectIfNotHomeTenant(user);
        if (ownershipError != null) return ownershipError;

        await _sessionStore.DeleteSessionsBySubjectAsync(userId, cancellationToken);

        _logger.LogInformation("Admin revoked all sessions for user {UserId}", userId);

        return NoContent();
    }

    /// <summary>
    /// Role assignments are scoped to the request's tenant. A SuperAdmin working
    /// from the platform meta-tenant (the organization screens never switch the
    /// tenant header) acts on the user's home tenant instead.
    /// </summary>
    private string? ResolveRoleScopeTenantId(OlusoUserInfo user) =>
        Core.Authorization.RoleAssignmentTenant.Resolve(TenantId, user.TenantId);

    /// <summary>
    /// Names of the roles the user holds in <paramref name="scopeTenantId"/>,
    /// read from role rows by id.
    /// </summary>
    private async Task<List<string>> RoleNamesInScopeAsync(
        string userId, string? scopeTenantId, CancellationToken cancellationToken) =>
        (await _roleStore.GetUserRolesInTenantAsync(userId, scopeTenantId, cancellationToken))
            .Select(r => r.Name!)
            .ToList();

    private static UserRoleDto ToUserRoleDto(Core.Domain.Entities.OlusoRole role) => new()
    {
        Id = role.Id,
        Name = role.Name!,
        DisplayName = role.DisplayName ?? role.Name!,
        IsGlobal = role.TenantId == null
    };

    private RoleGrantActor Actor() => new(AdminUserId, AdminUserName, ClientIp);

    private EventOrigin Origin => EventOrigin.Admin(AdminUserId!, AdminUserName, ClientIp);
}
