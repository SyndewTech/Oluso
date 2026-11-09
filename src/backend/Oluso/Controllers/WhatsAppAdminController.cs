using Microsoft.AspNetCore.Mvc;
using Oluso.Core.Api;
using Oluso.Core.Authorization;
using Oluso.Core.Domain.Entities;
using Oluso.Core.Domain.Interfaces;
using Oluso.Core.Locale;
using Oluso.Core.Services;
using Oluso.Dto.Messaging;
using Oluso.Messaging;
using Oluso.Messaging.WhatsApp;

namespace Oluso.Controllers;

/// <summary>
/// Tenant-level WhatsApp Business configuration: credentials (stored
/// encrypted), connection test against Meta, template registry sync, contact
/// consent, and a test send. Lives at tenant level because the channel is
/// shared by every module, not owned by one.
/// </summary>
[Route("api/admin/messaging/whatsapp")]
public class WhatsAppAdminController : AdminBaseController
{
    private const string SettingsRead = "settings.read";
    private const string SettingsWrite = "settings.write";

    private readonly IWhatsAppSettingsResolver _settings;
    private readonly IWhatsAppTemplateService _templates;
    private readonly IWhatsAppTemplateStore _templateStore;
    private readonly IWhatsAppContactStore _contacts;
    private readonly IWhatsAppContactService _contactService;
    private readonly IWhatsAppAccountStore _accounts;
    private readonly IWhatsAppSender _sender;
    private readonly ITenantStore _tenants;
    private readonly IIssuerResolver _issuerResolver;

    public WhatsAppAdminController(
        ITenantContext tenantContext,
        IWhatsAppSettingsResolver settings,
        IWhatsAppTemplateService templates,
        IWhatsAppTemplateStore templateStore,
        IWhatsAppContactStore contacts,
        IWhatsAppContactService contactService,
        IWhatsAppAccountStore accounts,
        IWhatsAppSender sender,
        ITenantStore tenants,
        IIssuerResolver issuerResolver) : base(tenantContext)
    {
        _issuerResolver = issuerResolver;
        _settings = settings;
        _templates = templates;
        _templateStore = templateStore;
        _contacts = contacts;
        _contactService = contactService;
        _accounts = accounts;
        _sender = sender;
        _tenants = tenants;
    }

    [HttpGet("settings")]
    [RequirePermission(SettingsRead)]
    public async Task<ActionResult<WhatsAppSettingsDto>> GetSettings(CancellationToken cancellationToken)
    {
        var own = await _settings.GetTenantSettingsAsync(TenantId, cancellationToken) ?? new WhatsAppSettings();
        var effective = await _settings.ResolveAsync(TenantId, cancellationToken);
        var account = await _accounts.GetForTenantAsync(TenantId, cancellationToken);
        var tenant = await _tenants.GetByIdAsync(TenantId, cancellationToken);

        return Ok(new WhatsAppSettingsDto
        {
            Enabled = own.Enabled,
            HasAccessToken = !string.IsNullOrWhiteSpace(own.AccessToken),
            HasAppSecret = !string.IsNullOrWhiteSpace(own.AppSecret),
            PhoneNumberId = own.PhoneNumberId,
            BusinessAccountId = own.BusinessAccountId,
            ApiVersion = own.ApiVersion,
            DefaultCountry = own.DefaultCountry,
            WebhookVerifyToken = own.WebhookVerifyToken,
            EffectiveSource = effective?.Source.ToString(),
            IsConfigured = effective != null,
            WebhookUrl = await BuildTenantWebhookUrlAsync(tenant, cancellationToken),
            Account = account == null ? null : ToDto(account),
        });
    }

    [HttpPut("settings")]
    [RequirePermission(SettingsWrite)]
    public async Task<ActionResult<WhatsAppSettingsDto>> UpdateSettings(
        [FromBody] UpdateWhatsAppSettingsRequest request, CancellationToken cancellationToken)
    {
        var current = await _settings.GetTenantSettingsAsync(TenantId, cancellationToken) ?? new WhatsAppSettings();

        if (!string.IsNullOrWhiteSpace(request.DefaultCountry) && SupportedCountries.GetByCode(request.DefaultCountry.Trim()) == null)
            return BadRequest(new { Message = $"Unknown country code '{request.DefaultCountry}'. Use an ISO 3166-1 alpha-2 code such as NG or GB." });

        if (!string.IsNullOrWhiteSpace(request.ApiVersion) && !System.Text.RegularExpressions.Regex.IsMatch(request.ApiVersion.Trim(), @"^v\d+\.\d+$"))
            return BadRequest(new { Message = "API version must look like v21.0." });

        // Blank secret fields keep what is stored; every other field is taken as given.
        current.Enabled = request.Enabled;
        current.AccessToken = Keep(request.AccessToken, current.AccessToken);
        current.AppSecret = Keep(request.AppSecret, current.AppSecret);
        current.PhoneNumberId = request.PhoneNumberId?.Trim();
        current.BusinessAccountId = request.BusinessAccountId?.Trim();
        current.WebhookVerifyToken = string.IsNullOrWhiteSpace(request.WebhookVerifyToken)
            ? (current.WebhookVerifyToken ?? Guid.NewGuid().ToString("N"))
            : request.WebhookVerifyToken.Trim();
        current.ApiVersion = request.ApiVersion?.Trim();
        current.DefaultCountry = request.DefaultCountry?.Trim().ToUpperInvariant();

        if (current.Enabled && !current.HasCredentials)
            return BadRequest(new { Message = "An access token and a phone number id are required to enable WhatsApp." });

        await _settings.SaveTenantSettingsAsync(TenantId, current, cancellationToken);

        // Keep the phone-number-id → tenant map current so webhooks route
        // even before the first connection test.
        if (!string.IsNullOrWhiteSpace(current.PhoneNumberId))
        {
            try
            {
                await _accounts.UpsertAsync(new WhatsAppAccount
                {
                    TenantId = TenantId,
                    PhoneNumberId = current.PhoneNumberId,
                    BusinessAccountId = current.BusinessAccountId,
                }, cancellationToken);
            }
            catch (InvalidOperationException ex)
            {
                return Conflict(new { Message = ex.Message });
            }
        }
        else
        {
            await _accounts.RemoveForTenantAsync(TenantId, cancellationToken);
        }

        return await GetSettings(cancellationToken);
    }

    [HttpPost("test")]
    [RequirePermission(SettingsWrite)]
    public async Task<ActionResult<WhatsAppTestConnectionResult>> TestConnection(CancellationToken cancellationToken)
    {
        var check = await _templates.VerifyAccountAsync(TenantId, cancellationToken);
        return Ok(new WhatsAppTestConnectionResult
        {
            Success = check.Success,
            Message = check.Success
                ? $"Connected as {check.Account?.VerifiedName ?? "(unnamed)"} {check.Account?.DisplayPhoneNumber}".Trim()
                : check.Error,
            Account = check.Account == null ? null : ToDto(check.Account),
        });
    }

    [HttpGet("templates")]
    [RequirePermission(SettingsRead)]
    public async Task<ActionResult<IEnumerable<WhatsAppTemplateDto>>> ListTemplates(
        [FromQuery] bool approvedOnly = false, CancellationToken cancellationToken = default)
    {
        var rows = await _templateStore.ListAsync(TenantId, approvedOnly, cancellationToken);
        return Ok(rows.Select(ToDto));
    }

    [HttpPost("templates/sync")]
    [RequirePermission(SettingsWrite)]
    public async Task<ActionResult<WhatsAppTemplateSyncResult>> SyncTemplates(CancellationToken cancellationToken)
    {
        var (ok, error, count) = await _templates.SyncAsync(TenantId, cancellationToken);
        return Ok(new WhatsAppTemplateSyncResult
        {
            Success = ok,
            Message = ok ? $"{count} template(s) synced from Meta." : error,
            Count = count,
        });
    }

    [HttpGet("contacts")]
    [RequirePermission(SettingsRead)]
    public async Task<ActionResult<WhatsAppContactListResponse>> ListContacts(
        [FromQuery] string? search, [FromQuery] string? consent, [FromQuery] int page = 1, [FromQuery] int pageSize = 25,
        CancellationToken cancellationToken = default)
    {
        WhatsAppConsentStatus? status = null;
        if (!string.IsNullOrWhiteSpace(consent))
        {
            if (!Enum.TryParse<WhatsAppConsentStatus>(consent, ignoreCase: true, out var parsed))
                return BadRequest(new { Message = $"Unknown consent filter '{consent}'." });
            status = parsed;
        }

        page = Math.Max(1, page);
        pageSize = Math.Clamp(pageSize, 1, 200);
        var (items, total) = await _contacts.SearchAsync(TenantId, search, status, (page - 1) * pageSize, pageSize, cancellationToken);
        var now = DateTime.UtcNow;
        return Ok(new WhatsAppContactListResponse
        {
            Items = items.Select(c => ToDto(c, now)).ToList(),
            TotalCount = total,
            Page = page,
            PageSize = pageSize,
        });
    }

    [HttpPut("contacts/{contactId}/consent")]
    [RequirePermission(SettingsWrite)]
    public async Task<ActionResult<WhatsAppContactDto>> UpdateConsent(
        string contactId, [FromBody] UpdateWhatsAppConsentRequest request, CancellationToken cancellationToken)
    {
        if (!Enum.TryParse<WhatsAppConsentStatus>(request.ConsentStatus, ignoreCase: true, out var status)
            || status == WhatsAppConsentStatus.Unknown)
            return BadRequest(new { Message = "consentStatus must be OptedIn or OptedOut." });

        var contact = await _contacts.GetByIdAsync(TenantId, contactId, cancellationToken);
        if (contact == null) return NotFound();

        var updated = await _contactService.SetConsentAsync(TenantId, contact.PhoneNumber, status, "admin", AdminUserId, cancellationToken);
        return Ok(ToDto(updated, DateTime.UtcNow));
    }

    /// <summary>
    /// Sends one message with the tenant's credentials. Useful right after
    /// setup: a template send proves the number and template work; a text
    /// send proves the session logic (it is refused until the recipient has
    /// written in).
    /// </summary>
    [HttpPost("send-test")]
    [RequirePermission(SettingsWrite)]
    public async Task<ActionResult<WhatsAppSendTestResult>> SendTest(
        [FromBody] WhatsAppSendTestRequest request, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(request.To))
            return BadRequest(new { Message = "A recipient phone number is required." });

        WhatsAppResult result;
        if (!string.IsNullOrWhiteSpace(request.TemplateName))
        {
            result = await _sender.SendTemplateAsync(
                TenantId, request.To, request.TemplateName, request.TemplateLanguage ?? "en",
                request.TemplateParameters, cancellationToken);
        }
        else if (!string.IsNullOrWhiteSpace(request.Text))
        {
            result = await _sender.SendTextAsync(TenantId, request.To, request.Text, cancellationToken);
        }
        else
        {
            return BadRequest(new { Message = "Provide either a template name or a text message." });
        }

        return Ok(new WhatsAppSendTestResult
        {
            Success = result.Success,
            MessageId = result.MessageId,
            Error = result.Error,
            NormalizedPhone = result.NormalizedPhone,
        });
    }

    /// <summary>
    /// The tenant's callback URL on its subdomain or custom domain: the tenant
    /// middleware identifies the tenant from that host, the same way it does
    /// for every other tenant request.
    /// </summary>
    private async Task<string?> BuildTenantWebhookUrlAsync(Tenant? tenant, CancellationToken cancellationToken)
    {
        if (tenant == null) return null;
        var issuer = (await _issuerResolver.GetIssuerForTenantAsync(tenant, cancellationToken)).TrimEnd('/');
        return string.IsNullOrEmpty(issuer) ? null : $"{issuer}/api/webhooks/whatsapp";
    }

    private static string? Keep(string? incoming, string? stored) =>
        string.IsNullOrWhiteSpace(incoming) ? stored : incoming.Trim();

    private static WhatsAppAccountDto ToDto(WhatsAppAccount a) => new()
    {
        PhoneNumberId = a.PhoneNumberId,
        BusinessAccountId = a.BusinessAccountId,
        DisplayPhoneNumber = a.DisplayPhoneNumber,
        VerifiedName = a.VerifiedName,
        QualityRating = a.QualityRating,
        LastVerifiedAt = a.LastVerifiedAt,
    };

    private static WhatsAppTemplateDto ToDto(WhatsAppTemplate t) => new()
    {
        Id = t.Id,
        Name = t.Name,
        Language = t.Language,
        Category = t.Category,
        Status = t.Status,
        BodyText = t.BodyText,
        BodyParameterCount = t.BodyParameterCount,
        HeaderFormat = t.HeaderFormat,
        SyncedAt = t.SyncedAt,
    };

    private static WhatsAppContactDto ToDto(WhatsAppContact c, DateTime now) => new()
    {
        Id = c.Id,
        PhoneNumber = c.PhoneNumber,
        DisplayName = c.DisplayName,
        ConsentStatus = c.ConsentStatus.ToString(),
        ConsentSource = c.ConsentSource,
        ConsentAt = c.ConsentAt,
        LastInboundAt = c.LastInboundAt,
        LastOutboundAt = c.LastOutboundAt,
        SessionOpen = c.ConsentStatus != WhatsAppConsentStatus.OptedOut && c.IsSessionOpen(now),
    };
}
