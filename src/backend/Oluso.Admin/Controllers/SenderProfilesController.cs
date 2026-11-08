using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Oluso.Admin.Authorization;
using Oluso.Core.Api;
using Oluso.Core.Authorization;
using Oluso.Core.Domain.Entities;
using Oluso.Core.Domain.Interfaces;
using Oluso.Core.Services;
using Oluso.Admin.Dto.SenderProfiles;

namespace Oluso.Admin.Controllers;

/// <summary>
/// Admin API for outbound sender profiles. Each tenant has one or more
/// profiles; exactly one is the default (used by transactional / system
/// mail). Workspace surfaces consume the read-only DTO and never see
/// credentials.
/// </summary>
/// <summary>
/// Sender profiles of the tenant in context (api/admin/sender-profiles) or of a
/// tenant named in the route (api/admin/tenants/{tenantId}/sender-profiles) for
/// admins managing that tenant from Organizations → tenant → settings. The route
/// form is gated by the tenant permissions; the context form by the settings ones.
/// </summary>
[Route("api/admin/sender-profiles")]
[Route("api/admin/tenants/{tenantId}/sender-profiles")]
public class SenderProfilesController : AdminBaseController
{
    private readonly ITenantContext _tenantContext;
    private readonly ISenderProfileStore _store;
    private readonly ITenantConfigurationService _configService;
    private readonly IProfileEmailSender _profileSender;
    private readonly ITenantStore _tenantStore;
    private readonly IAuthorizationService _authorization;
    private readonly IReplyToVerificationService _replyTo;
    private readonly ILogger<SenderProfilesController> _logger;

    public SenderProfilesController(
        ITenantContext tenantContext,
        ISenderProfileStore store,
        ITenantConfigurationService configService,
        IProfileEmailSender profileSender,
        ITenantStore tenantStore,
        IAuthorizationService authorization,
        IReplyToVerificationService replyTo,
        ILogger<SenderProfilesController> logger) : base(tenantContext)
    {
        _tenantContext = tenantContext;
        _store = store;
        _configService = configService;
        _profileSender = profileSender;
        _tenantStore = tenantStore;
        _authorization = authorization;
        _replyTo = replyTo;
        _logger = logger;
    }

    /// <summary>
    /// The tenant an action works on, with the permission that applies to how it
    /// was reached. Null tenant with no error means "no tenant in context".
    /// </summary>
    private async Task<(string? TenantId, ActionResult? Error)> ResolveTenantAsync(bool write, CancellationToken cancellationToken)
    {
        var routeTenantId = RouteData.Values["tenantId"] as string;
        if (!string.IsNullOrEmpty(routeTenantId))
        {
            if (!await HasPermissionAsync(write ? AdminPermissions.TenantsManageSettings : AdminPermissions.TenantsRead))
                return (null, Forbid());
            var tenant = await _tenantStore.GetByIdAsync(routeTenantId, cancellationToken);
            if (tenant == null)
                return (null, NotFound());
            if (!CanAccessTenant(tenant))
                return (null, Forbid());
            return (tenant.Id, null);
        }

        if (!await HasPermissionAsync(write ? AdminPermissions.SettingsWrite : AdminPermissions.SettingsRead))
            return (null, Forbid());
        if (!_tenantContext.HasTenant || _tenantContext.TenantId == null)
            return (null, null);
        return (_tenantContext.TenantId, null);
    }

    private async Task<bool> HasPermissionAsync(string permission)
    {
        var result = await _authorization.AuthorizeAsync(User, null, PermissionPolicyConstants.PolicyPrefix + permission);
        return result.Succeeded;
    }

    [HttpGet]
    [RequireAnyPermission(AdminPermissions.SettingsRead, AdminPermissions.TenantsRead)]
    public async Task<ActionResult<IReadOnlyList<SenderProfileDto>>> List(CancellationToken cancellationToken)
    {
        var (tenantId, denied) = await ResolveTenantAsync(write: false, cancellationToken);
        if (denied != null) return denied;
        if (tenantId == null)
            return Ok(Array.Empty<SenderProfileDto>());

        var profiles = await _store.ListAsync(tenantId, cancellationToken);
        var dtos = new List<SenderProfileDto>(profiles.Count);
        foreach (var profile in profiles)
        {
            var credentials = await _configService.GetSectionAsync<SenderCredentials>(
                tenantId, SenderCredentials.BuildSectionKey(profile.Id), cancellationToken);
            dtos.Add(ToDto(profile, credentials, masked: true));
        }
        return Ok(dtos);
    }

    /// <summary>
    /// The tenant's reply-to inbox: used when a sender profile sets no reply-to of
    /// its own, and on the platform's shared sender. Changes take effect once the
    /// new inbox confirms a link mailed to it.
    /// </summary>
    [HttpGet("reply-to")]
    [RequireAnyPermission(AdminPermissions.SettingsRead, AdminPermissions.TenantsRead)]
    public async Task<ActionResult<TenantReplyToDto>> GetReplyTo(CancellationToken cancellationToken)
    {
        var (tenantId, denied) = await ResolveTenantAsync(write: false, cancellationToken);
        if (denied != null) return denied;
        if (tenantId == null) return BadRequest(new { error = "No tenant in context" });
        return Ok(ToDto(await _replyTo.GetStatusAsync(tenantId, cancellationToken)));
    }

    /// <summary>Mails a confirmation link to the address; an empty address removes the reply-to.</summary>
    [HttpPut("reply-to")]
    [RequireAnyPermission(AdminPermissions.SettingsWrite, AdminPermissions.TenantsManageSettings)]
    public async Task<ActionResult<TenantReplyToDto>> RequestReplyTo([FromBody] RequestReplyToDto input, CancellationToken cancellationToken)
    {
        var (tenantId, denied) = await ResolveTenantAsync(write: true, cancellationToken);
        if (denied != null) return denied;
        if (tenantId == null) return BadRequest(new { error = "No tenant in context" });

        if (string.IsNullOrWhiteSpace(input.ReplyToAddress))
            return Ok(ToDto(await _replyTo.RemoveAsync(tenantId, cancellationToken)));

        var result = await _replyTo.RequestAsync(tenantId, input.ReplyToAddress, cancellationToken);
        if (result.Error != null) return BadRequest(new { error = result.Error });
        return Ok(ToDto(result.Status));
    }

    [HttpPost("reply-to/resend")]
    [RequireAnyPermission(AdminPermissions.SettingsWrite, AdminPermissions.TenantsManageSettings)]
    public async Task<ActionResult<TenantReplyToDto>> ResendReplyTo(CancellationToken cancellationToken)
    {
        var (tenantId, denied) = await ResolveTenantAsync(write: true, cancellationToken);
        if (denied != null) return denied;
        if (tenantId == null) return BadRequest(new { error = "No tenant in context" });

        var result = await _replyTo.ResendAsync(tenantId, cancellationToken);
        if (result.Error != null) return BadRequest(new { error = result.Error });
        return Ok(ToDto(result.Status));
    }

    [HttpDelete("reply-to/pending")]
    [RequireAnyPermission(AdminPermissions.SettingsWrite, AdminPermissions.TenantsManageSettings)]
    public async Task<ActionResult<TenantReplyToDto>> CancelPendingReplyTo(CancellationToken cancellationToken)
    {
        var (tenantId, denied) = await ResolveTenantAsync(write: true, cancellationToken);
        if (denied != null) return denied;
        if (tenantId == null) return BadRequest(new { error = "No tenant in context" });
        return Ok(ToDto(await _replyTo.CancelPendingAsync(tenantId, cancellationToken)));
    }

    private static TenantReplyToDto ToDto(ReplyToStatus status) => new()
    {
        ReplyToAddress = status.ReplyToAddress,
        VerifiedAt = status.VerifiedAt,
        PendingAddress = status.PendingAddress,
        PendingSentAt = status.PendingSentAt,
    };

    [HttpGet("{id}")]
    [RequireAnyPermission(AdminPermissions.SettingsRead, AdminPermissions.TenantsRead)]
    public async Task<ActionResult<SenderProfileDto>> Get(string id, CancellationToken cancellationToken)
    {
        var (tenantId, denied) = await ResolveTenantAsync(write: false, cancellationToken);
        if (denied != null) return denied;
        if (tenantId == null)
            return BadRequest(new { error = "No tenant context available" });

        var profile = await _store.GetAsync(tenantId, id, cancellationToken);
        if (profile == null)
            return NotFound();

        var credentials = await _configService.GetSectionAsync<SenderCredentials>(
            tenantId, SenderCredentials.BuildSectionKey(profile.Id), cancellationToken);

        return Ok(ToDto(profile, credentials, masked: true));
    }

    [HttpPost]
    [RequireAnyPermission(AdminPermissions.SettingsWrite, AdminPermissions.TenantsManageSettings)]
    public async Task<ActionResult<SenderProfileDto>> Create(
        [FromBody] SenderProfileDto input,
        CancellationToken cancellationToken)
    {
        var (tenantId, denied) = await ResolveTenantAsync(write: true, cancellationToken);
        if (denied != null) return denied;
        if (tenantId == null)
            return BadRequest(new { error = "No tenant context available" });

        if (string.IsNullOrWhiteSpace(input.Name))
            return BadRequest(new { error = "Name is required" });
        if (string.IsNullOrWhiteSpace(input.FromAddress))
            return BadRequest(new { error = "FromAddress is required" });

        var profile = new SenderProfile
        {
            Id = Guid.NewGuid().ToString(),
            TenantId = tenantId,
            Name = input.Name.Trim(),
            Provider = input.Provider,
            FromAddress = input.FromAddress.Trim(),
            FromName = string.IsNullOrWhiteSpace(input.FromName) ? input.FromAddress.Trim() : input.FromName.Trim(),
            ReplyToAddress = string.IsNullOrWhiteSpace(input.ReplyToAddress) ? null : input.ReplyToAddress.Trim(),
            IsActive = input.IsActive,
            IsDefault = false,
            CreatedBy = AdminUserId,
        };

        await _store.CreateAsync(profile, cancellationToken);

        var credentials = BuildCredentials(input);
        await _configService.SetSectionAsync(
            tenantId, SenderCredentials.BuildSectionKey(profile.Id), credentials, cancellationToken);

        if (input.IsDefault)
            await _store.SetDefaultAsync(tenantId, profile.Id, cancellationToken);

        _logger.LogInformation("Created sender profile {ProfileId} ({Name}) for tenant {TenantId}",
            profile.Id, profile.Name, tenantId);

        var reloaded = await _store.GetAsync(tenantId, profile.Id, cancellationToken);
        return Ok(ToDto(reloaded!, credentials, masked: true));
    }

    [HttpPut("{id}")]
    [RequireAnyPermission(AdminPermissions.SettingsWrite, AdminPermissions.TenantsManageSettings)]
    public async Task<ActionResult<SenderProfileDto>> Update(
        string id,
        [FromBody] SenderProfileDto input,
        CancellationToken cancellationToken)
    {
        var (tenantId, denied) = await ResolveTenantAsync(write: true, cancellationToken);
        if (denied != null) return denied;
        if (tenantId == null)
            return BadRequest(new { error = "No tenant context available" });

        var profile = await _store.GetAsync(tenantId, id, cancellationToken);
        if (profile == null)
            return NotFound();

        profile.Name = string.IsNullOrWhiteSpace(input.Name) ? profile.Name : input.Name.Trim();
        profile.Provider = input.Provider;
        profile.FromAddress = string.IsNullOrWhiteSpace(input.FromAddress) ? profile.FromAddress : input.FromAddress.Trim();
        profile.FromName = string.IsNullOrWhiteSpace(input.FromName) ? profile.FromAddress.Trim() : input.FromName.Trim();
        profile.ReplyToAddress = string.IsNullOrWhiteSpace(input.ReplyToAddress) ? null : input.ReplyToAddress.Trim();
        profile.IsActive = input.IsActive;
        profile.UpdatedBy = AdminUserId;

        await _store.UpdateAsync(profile, cancellationToken);

        var existingCredentials = await _configService.GetSectionAsync<SenderCredentials>(
            tenantId, SenderCredentials.BuildSectionKey(profile.Id), cancellationToken)
            ?? new SenderCredentials();

        var merged = MergeCredentials(existingCredentials, input);
        await _configService.SetSectionAsync(
            tenantId, SenderCredentials.BuildSectionKey(profile.Id), merged, cancellationToken);

        if (input.IsDefault && !profile.IsDefault)
            await _store.SetDefaultAsync(tenantId, profile.Id, cancellationToken);

        _logger.LogInformation("Updated sender profile {ProfileId} for tenant {TenantId}", profile.Id, tenantId);

        var reloaded = await _store.GetAsync(tenantId, profile.Id, cancellationToken);
        return Ok(ToDto(reloaded!, merged, masked: true));
    }

    [HttpDelete("{id}")]
    [RequireAnyPermission(AdminPermissions.SettingsWrite, AdminPermissions.TenantsManageSettings)]
    public async Task<ActionResult> Delete(string id, CancellationToken cancellationToken)
    {
        var (tenantId, denied) = await ResolveTenantAsync(write: true, cancellationToken);
        if (denied != null) return denied;
        if (tenantId == null)
            return BadRequest(new { error = "No tenant context available" });

        var profile = await _store.GetAsync(tenantId, id, cancellationToken);
        if (profile == null)
            return NotFound();

        if (profile.IsDefault)
            return BadRequest(new { error = "Cannot delete the default sender profile. Set another profile as default first." });

        await _store.DeleteAsync(tenantId, id, cancellationToken);
        await _configService.RemoveSectionAsync(
            tenantId, SenderCredentials.BuildSectionKey(id), cancellationToken);

        _logger.LogInformation("Deleted sender profile {ProfileId} for tenant {TenantId}", id, tenantId);
        return NoContent();
    }

    [HttpPost("{id}/set-default")]
    [RequireAnyPermission(AdminPermissions.SettingsWrite, AdminPermissions.TenantsManageSettings)]
    public async Task<ActionResult> SetDefault(string id, CancellationToken cancellationToken)
    {
        var (tenantId, denied) = await ResolveTenantAsync(write: true, cancellationToken);
        if (denied != null) return denied;
        if (tenantId == null)
            return BadRequest(new { error = "No tenant context available" });

        try
        {
            await _store.SetDefaultAsync(tenantId, id, cancellationToken);
        }
        catch (InvalidOperationException ex)
        {
            return NotFound(new { error = ex.Message });
        }

        _logger.LogInformation("Set sender profile {ProfileId} as default for tenant {TenantId}", id, tenantId);
        return NoContent();
    }

    [HttpPost("{id}/test")]
    [RequireAnyPermission(AdminPermissions.SettingsWrite, AdminPermissions.TenantsManageSettings)]
    public async Task<ActionResult> SendTest(
        string id,
        [FromBody] SendTestEmailRequest request,
        CancellationToken cancellationToken)
    {
        var (tenantId, denied) = await ResolveTenantAsync(write: true, cancellationToken);
        if (denied != null) return denied;
        if (tenantId == null)
            return BadRequest(new { error = "No tenant context available" });
        if (string.IsNullOrWhiteSpace(request.To))
            return BadRequest(new { error = "Recipient email is required" });

        var profile = await _store.GetAsync(tenantId, id, cancellationToken);
        if (profile == null)
            return NotFound();

        var subject = $"Test email from {profile.Name}";
        var html = $"<p>This is a test email sent through sender profile <strong>{System.Net.WebUtility.HtmlEncode(profile.Name)}</strong> ({profile.Provider}).</p>" +
                   $"<p>If you received this, the profile's credentials are working.</p>";

        var result = await _profileSender.SendThroughProfileAsync(
            tenantId, profile.Id, request.To, subject, html, cancellationToken);

        var now = DateTime.UtcNow;
        if (result.Success)
        {
            profile.LastTestSendAt = now;
            profile.LastTestError = null;
            await _store.UpdateAsync(profile, cancellationToken);
            return Ok(new { message = $"Test email sent to {request.To}", messageId = result.MessageId });
        }

        profile.LastTestError = result.Error;
        await _store.UpdateAsync(profile, cancellationToken);
        return BadRequest(new { error = result.Error });
    }

    private static SenderProfileDto ToDto(SenderProfile profile, SenderCredentials? credentials, bool masked)
    {
        return new SenderProfileDto
        {
            Id = profile.Id,
            Name = profile.Name,
            Provider = profile.Provider,
            FromAddress = profile.FromAddress,
            FromName = profile.FromName,
            ReplyToAddress = profile.ReplyToAddress,
            IsActive = profile.IsActive,
            IsDefault = profile.IsDefault,
            LastUsedAt = profile.LastUsedAt,
            LastTestSendAt = profile.LastTestSendAt,
            LastTestError = profile.LastTestError,
            ApiKey = masked ? MaskSecret(credentials?.ApiKey) : credentials?.ApiKey,
            CloudflareAccountId = credentials?.CloudflareAccountId,
            SmtpHost = credentials?.SmtpHost,
            SmtpPort = credentials?.SmtpPort ?? 587,
            SmtpUsername = credentials?.SmtpUsername,
            SmtpPassword = masked ? MaskSecret(credentials?.SmtpPassword) : credentials?.SmtpPassword,
            SmtpUseSsl = credentials?.SmtpUseSsl ?? true,
        };
    }

    private static SenderCredentials BuildCredentials(SenderProfileDto input)
    {
        return new SenderCredentials
        {
            ApiKey = IsMasked(input.ApiKey) ? null : input.ApiKey,
            CloudflareAccountId = input.CloudflareAccountId,
            SmtpHost = input.SmtpHost,
            SmtpPort = input.SmtpPort,
            SmtpUsername = input.SmtpUsername,
            SmtpPassword = IsMasked(input.SmtpPassword) ? null : input.SmtpPassword,
            SmtpUseSsl = input.SmtpUseSsl,
        };
    }

    private static SenderCredentials MergeCredentials(SenderCredentials existing, SenderProfileDto input)
    {
        return new SenderCredentials
        {
            ApiKey = IsMasked(input.ApiKey) ? existing.ApiKey : input.ApiKey,
            CloudflareAccountId = input.CloudflareAccountId,
            SmtpHost = input.SmtpHost,
            SmtpPort = input.SmtpPort,
            SmtpUsername = input.SmtpUsername,
            SmtpPassword = IsMasked(input.SmtpPassword) ? existing.SmtpPassword : input.SmtpPassword,
            SmtpUseSsl = input.SmtpUseSsl,
        };
    }

    private static string? MaskSecret(string? value)
    {
        if (string.IsNullOrEmpty(value)) return value;
        if (value.Length <= 8) return "••••••••";
        return value[..4] + "••••••••" + value[^4..];
    }

    private static bool IsMasked(string? value)
        => value != null && value.Contains("••••");
}
