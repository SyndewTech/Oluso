using System.Net;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Oluso.Admin.Authorization;
using Oluso.Core.Authorization;
using Oluso.Core.Api;
using Oluso.Core.Domain.Interfaces;
using Oluso.Core.Licensing;
using Oluso.Core.Services;
using Oluso.Admin.Dto.Settings;

namespace Oluso.Admin.Controllers;

/// <summary>
/// Admin API for identity server settings.
/// Each settings group is stored as a separate section in TenantConfigurationSections.
/// </summary>
[Route("api/admin/settings")]
public class SettingsController : AdminBaseController
{
    private readonly IConfiguration _configuration;
    private readonly ITenantContext _tenantContext;
    private readonly ITenantStore _tenantStore;
    private readonly ITenantConfigurationService _configService;
    private readonly ITenantSettingsProvider _settingsProvider;
    private readonly IEmailSender? _emailSender;
    private readonly ILicenseValidator? _licenseValidator;
    private readonly ILogger<SettingsController> _logger;

    // Section keys for core settings
    private const string TokenSettingsSection = "TokenSettings";
    private const string SecuritySettingsSection = "SecuritySettings";
    private readonly ICorsOriginsCacheInvalidator? _corsCache;
    private const string CorsSettingsSection = TenantCorsSettings.SectionKey;
    private const string BrandingSettingsSection = "BrandingSettings";

    public SettingsController(
        IConfiguration configuration,
        ITenantContext tenantContext,
        ITenantStore tenantStore,
        ITenantConfigurationService configService,
        ITenantSettingsProvider settingsProvider,
        ILogger<SettingsController> logger,
        IEmailSender? emailSender = null,
        ILicenseValidator? licenseValidator = null,
        ICorsOriginsCacheInvalidator? corsCache = null) : base(tenantContext)
    {
        _corsCache = corsCache;
        _configuration = configuration;
        _tenantContext = tenantContext;
        _tenantStore = tenantStore;
        _configService = configService;
        _settingsProvider = settingsProvider;
        _emailSender = emailSender;
        _licenseValidator = licenseValidator;
        _logger = logger;
    }

    /// <summary>
    /// Get all settings
    /// </summary>
    [HttpGet]
    [RequirePermission(AdminPermissions.SettingsRead)]
    public async Task<ActionResult<ServerSettingsDto>> GetSettings(CancellationToken cancellationToken)
    {
        if (!_tenantContext.HasTenant || _tenantContext.TenantId == null)
        {
            return Ok(GetDefaultSettings(null));
        }

        var tenantId = _tenantContext.TenantId;
        var tenant = await _tenantStore.GetByIdAsync(tenantId, cancellationToken);

        var tokenSettings = await _configService.GetSectionAsync<TokenSettingsDto>(tenantId, TokenSettingsSection, cancellationToken);
        var securitySettings = await _configService.GetSectionAsync<SecuritySettingsDto>(tenantId, SecuritySettingsSection, cancellationToken);
        var corsSettings = await _configService.GetSectionAsync<CorsSettingsDto>(tenantId, CorsSettingsSection, cancellationToken);
        var brandingSettings = await _configService.GetSectionAsync<BrandingSettingsDto>(tenantId, BrandingSettingsSection, cancellationToken);
        var accountEmails = await _settingsProvider.GetAccountEmailSettingsAsync(tenantId, cancellationToken);

        var settings = new ServerSettingsDto
        {
            AccountEmailSettings = new AccountEmailSettingsDto
            {
                AccountLinkLifetimeMinutes = accountEmails.AccountLinkLifetimeMinutes,
                MfaCodeLifetimeMinutes = accountEmails.MfaCodeLifetimeMinutes,
                InvitationLifetimeDays = accountEmails.InvitationLifetimeDays,
            },
            TokenSettings = tokenSettings ?? new TokenSettingsDto
            {
                DefaultAccessTokenLifetime = _configuration.GetValue("Oluso:AccessTokenLifetime", 3600),
                DefaultIdentityTokenLifetime = _configuration.GetValue("Oluso:IdentityTokenLifetime", 300),
                DefaultRefreshTokenLifetime = _configuration.GetValue("Oluso:RefreshTokenLifetime", 2592000)
            },
            SecuritySettings = securitySettings ?? new SecuritySettingsDto
            {
                RequireHttps = _configuration.GetValue("Oluso:RequireHttpsMetadata", true),
                EmitStaticClaims = _configuration.GetValue("Oluso:EmitStaticAudienceClaim", false),
                EnableBackchannelLogout = _configuration.GetValue("Oluso:EnableBackchannelLogout", true)
            },
            CorsSettings = corsSettings ?? GetDefaultCorsSettings(),
            BrandingSettings = brandingSettings,
            TenantSettings = tenant != null ? new TenantSettingsDto
            {
                UseJourneyFlow = tenant.UseJourneyFlow,
                JourneyFallbackToStandard = tenant.JourneyFallbackToStandard,
                AllowSelfRegistration = tenant.AllowSelfRegistration,
                RequireTermsAcceptance = tenant.RequireTermsAcceptance,
                RequireEmailVerification = tenant.RequireEmailVerification,
                TermsOfServiceUrl = tenant.TermsOfServiceUrl,
                PrivacyPolicyUrl = tenant.PrivacyPolicyUrl,
                AllowedEmailDomains = tenant.AllowedEmailDomains,
                EnableLocalLogin = tenant.EnableLocalLogin,
                EnablePasskey = tenant.EnablePasskey
            } : new TenantSettingsDto(),
            ProtocolSettings = MapProtocolSettings(tenant?.ProtocolConfiguration)
        };

        return Ok(settings);
    }

    /// <summary>
    /// Update settings — writes each sub-section independently
    /// </summary>
    [HttpPut]
    [RequirePermission(AdminPermissions.SettingsWrite)]
    public async Task<ActionResult<ServerSettingsDto>> UpdateSettings(
        [FromBody] ServerSettingsDto settings,
        CancellationToken cancellationToken)
    {
        if (!_tenantContext.HasTenant || _tenantContext.TenantId == null)
        {
            return BadRequest(new { error = "No tenant context available" });
        }

        var tenantId = _tenantContext.TenantId;
        var tenant = await _tenantStore.GetByIdAsync(tenantId, cancellationToken);
        if (tenant == null)
        {
            return NotFound(new { error = "Tenant not found" });
        }

        if (settings.AccountEmailSettings?.Validate() is { } invalid)
        {
            return BadRequest(new { error = invalid });
        }

        if (settings.ProtocolSettings != null
            && ProtocolConfigurationRules.Validate(
                settings.ProtocolSettings.PrefixScopeSuffixMaxLength,
                settings.ProtocolSettings.DynamicRegistrationAllowedScopes) is { } invalidProtocol)
        {
            return BadRequest(new { error = invalidProtocol });
        }

        var tenantChanged = false;

        // Update tenant entity fields from TenantSettings
        if (settings.TenantSettings != null)
        {
            tenant.UseJourneyFlow = settings.TenantSettings.UseJourneyFlow;
            tenant.JourneyFallbackToStandard = settings.TenantSettings.JourneyFallbackToStandard;
            tenant.AllowSelfRegistration = settings.TenantSettings.AllowSelfRegistration;
            tenant.RequireTermsAcceptance = settings.TenantSettings.RequireTermsAcceptance;
            tenant.RequireEmailVerification = settings.TenantSettings.RequireEmailVerification;
            tenant.TermsOfServiceUrl = settings.TenantSettings.TermsOfServiceUrl;
            tenant.PrivacyPolicyUrl = settings.TenantSettings.PrivacyPolicyUrl;
            tenant.AllowedEmailDomains = settings.TenantSettings.AllowedEmailDomains;
            tenant.EnableLocalLogin = settings.TenantSettings.EnableLocalLogin;
            tenant.EnablePasskey = settings.TenantSettings.EnablePasskey;
            tenantChanged = true;
        }

        // The Protocol tab writes the tenant's protocol configuration (the same record the tenant
        // protocol-configuration endpoint edits); fields it doesn't show are left as they are.
        if (settings.ProtocolSettings is { } protocol)
        {
            tenant.ProtocolConfiguration ??= new Core.Domain.Entities.TenantProtocolConfiguration { TenantId = tenantId };
            var config = tenant.ProtocolConfiguration;
            config.EnforceOAuth21 = protocol.EnforceOAuth21;
            config.EnableDynamicClientRegistration = protocol.EnableDynamicClientRegistration;
            config.AllowOpenDynamicRegistration = protocol.AllowOpenDynamicRegistration;
            config.DynamicRegistrationAllowedScopesJson = SerializeList(protocol.DynamicRegistrationAllowedScopes);
            config.DynamicRegistrationAllowedGrantTypesJson = SerializeList(protocol.DynamicRegistrationAllowedGrantTypes);
            config.DynamicRegistrationRequirePkce = protocol.DynamicRegistrationRequirePkce;
            config.DynamicRegistrationMaxRedirectUris = protocol.DynamicRegistrationMaxRedirectUris;
            config.DynamicRegistrationRequiredScope = NullIfBlank(protocol.DynamicRegistrationRequiredScope);
            config.DynamicRegistrationRequiredClaim = NullIfBlank(protocol.DynamicRegistrationRequiredClaim);
            config.DynamicRegistrationRequiredClaimValue = NullIfBlank(protocol.DynamicRegistrationRequiredClaimValue);
            config.PrefixScopeSuffixMaxLength =
                ProtocolConfigurationRules.StoredPrefixScopeSuffixMaxLength(protocol.PrefixScopeSuffixMaxLength);
            config.Updated = DateTime.UtcNow;
            tenantChanged = true;
        }

        if (tenantChanged)
        {
            await _tenantStore.UpdateAsync(tenant, cancellationToken);
        }

        // Write each section independently — no risk of overwriting other modules' data
        await _configService.SetSectionAsync(tenantId, TokenSettingsSection, settings.TokenSettings, cancellationToken);
        await _configService.SetSectionAsync(tenantId, SecuritySettingsSection, settings.SecuritySettings, cancellationToken);
        await _configService.SetSectionAsync(tenantId, CorsSettingsSection, settings.CorsSettings, cancellationToken);
        if (_corsCache != null) await _corsCache.InvalidateTenantAsync(tenantId, cancellationToken);
        if (settings.BrandingSettings != null)
        {
            await _configService.SetSectionAsync(tenantId, BrandingSettingsSection, settings.BrandingSettings, cancellationToken);
        }
        if (settings.AccountEmailSettings != null)
        {
            await _configService.SetSectionAsync(tenantId, TenantAccountEmailSettings.SectionKey, new TenantAccountEmailSettings
            {
                AccountLinkLifetimeMinutes = settings.AccountEmailSettings.AccountLinkLifetimeMinutes,
                MfaCodeLifetimeMinutes = settings.AccountEmailSettings.MfaCodeLifetimeMinutes,
                InvitationLifetimeDays = settings.AccountEmailSettings.InvitationLifetimeDays,
            }, cancellationToken);
        }

        _logger.LogInformation("Updated settings for tenant {TenantId}", tenantId);

        return Ok(settings);
    }

    /// <summary>
    /// Get token settings
    /// </summary>
    [HttpGet("tokens")]
    [RequirePermission(AdminPermissions.SettingsRead)]
    public async Task<ActionResult<TokenSettingsDto>> GetTokenSettings(CancellationToken cancellationToken)
    {
        if (!_tenantContext.HasTenant || _tenantContext.TenantId == null)
            return Ok(GetDefaultTokenSettings());

        var settings = await _configService.GetSectionAsync<TokenSettingsDto>(_tenantContext.TenantId, TokenSettingsSection, cancellationToken);
        return Ok(settings ?? GetDefaultTokenSettings());
    }

    /// <summary>
    /// Get security settings
    /// </summary>
    [HttpGet("security")]
    [RequirePermission(AdminPermissions.SettingsRead)]
    public async Task<ActionResult<SecuritySettingsDto>> GetSecuritySettings(CancellationToken cancellationToken)
    {
        if (!_tenantContext.HasTenant || _tenantContext.TenantId == null)
            return Ok(GetDefaultSecuritySettings());

        var settings = await _configService.GetSectionAsync<SecuritySettingsDto>(_tenantContext.TenantId, SecuritySettingsSection, cancellationToken);
        return Ok(settings ?? GetDefaultSecuritySettings());
    }

    /// <summary>
    /// Get CORS settings
    /// </summary>
    [HttpGet("cors")]
    [RequirePermission(AdminPermissions.SettingsRead)]
    public async Task<ActionResult<CorsSettingsDto>> GetCorsSettings(CancellationToken cancellationToken)
    {
        if (!_tenantContext.HasTenant || _tenantContext.TenantId == null)
            return Ok(GetDefaultCorsSettings());

        var settings = await _configService.GetSectionAsync<CorsSettingsDto>(_tenantContext.TenantId, CorsSettingsSection, cancellationToken);
        return Ok(settings ?? GetDefaultCorsSettings());
    }

    /// <summary>
    /// Get branding settings
    /// </summary>
    [HttpGet("branding")]
    [RequirePermission(AdminPermissions.SettingsRead)]
    public async Task<ActionResult<BrandingSettingsDto>> GetBrandingSettings(CancellationToken cancellationToken)
    {
        if (!_tenantContext.HasTenant || _tenantContext.TenantId == null)
            return Ok(new BrandingSettingsDto());

        var settings = await _configService.GetSectionAsync<BrandingSettingsDto>(_tenantContext.TenantId, BrandingSettingsSection, cancellationToken);
        return Ok(settings ?? new BrandingSettingsDto());
    }

    /// <summary>
    /// Update branding settings. Writes to the canonical
    /// <c>BrandingSettings</c> tenant-config section — the only home for
    /// visual branding. <c>HideEmbedBranding</c> stays on the entity and is
    /// updated through a separate SuperAdmin endpoint.
    /// </summary>
    [HttpPut("branding")]
    [RequirePermission(AdminPermissions.SettingsWrite)]
    public async Task<ActionResult<BrandingSettingsDto>> UpdateBrandingSettings(
        [FromBody] BrandingSettingsDto settings,
        CancellationToken cancellationToken)
    {
        if (!_tenantContext.HasTenant || _tenantContext.TenantId == null)
        {
            return BadRequest(new { error = "No tenant context available" });
        }

        await _configService.SetSectionAsync(_tenantContext.TenantId, BrandingSettingsSection, settings, cancellationToken);

        _logger.LogInformation("Updated branding settings for tenant {TenantId}", _tenantContext.TenantId);
        return Ok(settings);
    }

    /// <summary>
    /// Send a test email using the tenant's default SenderProfile (or the
    /// platform-global EmailOptions fallback when no profile is configured).
    /// Per-profile test sends live on the SenderProfilesController.
    /// </summary>
    [HttpPost("email/test")]
    [RequirePermission(AdminPermissions.SettingsWrite)]
    public async Task<ActionResult> SendTestEmail(
        [FromBody] SendTestEmailRequest request,
        CancellationToken cancellationToken)
    {
        if (_emailSender == null)
            return BadRequest(new { error = "Email sending is not configured" });

        if (string.IsNullOrEmpty(request.To))
            return BadRequest(new { error = "Recipient email is required" });

        var (subject, html) = await BuildTestEmailAsync(cancellationToken);

        var result = await _emailSender.SendAsync(
            request.To,
            subject,
            html,
            cancellationToken);

        if (result.Success)
        {
            _logger.LogInformation("Test email sent successfully to {To} for tenant {TenantId}",
                request.To, _tenantContext.TenantId);
            return Ok(new { message = $"Test email sent to {request.To}", messageId = result.MessageId });
        }

        return BadRequest(new { error = $"Failed to send test email: {result.Error}" });
    }

    private async Task<(string Subject, string Html)> BuildTestEmailAsync(CancellationToken cancellationToken)
    {
        var tenantId = _tenantContext.HasTenant ? _tenantContext.TenantId : null;
        var tenant = !string.IsNullOrEmpty(tenantId)
            ? await _tenantStore.GetByIdAsync(tenantId, cancellationToken)
            : null;
        var branding = !string.IsNullOrEmpty(tenantId)
            ? await _settingsProvider.GetBrandingSettingsAsync(tenantId, cancellationToken)
            : TenantBrandingSettings.Default;

        var platform = _configuration.GetSection("Oluso:Platform");
        var productName = !string.IsNullOrWhiteSpace(tenant?.DisplayName) ? tenant!.DisplayName!
            : !string.IsNullOrWhiteSpace(tenant?.Name) ? tenant!.Name!
            : platform["ProductName"] ?? "Oluso";
        var logoUrl = !string.IsNullOrWhiteSpace(branding.LogoUrl) ? branding.LogoUrl : platform["LogoUrl"];
        var primaryColor = !string.IsNullOrWhiteSpace(branding.PrimaryColor) ? branding.PrimaryColor! : "#0F766E";
        var supportEmail = branding.SupportEmail;
        var footerText = !string.IsNullOrWhiteSpace(branding.FooterText)
            ? branding.FooterText!
            : $"This is an automated message from {productName}.";

        var safeProduct = WebUtility.HtmlEncode(productName);
        var safeFooter = WebUtility.HtmlEncode(footerText);
        var safePrimary = WebUtility.HtmlEncode(primaryColor);

        var header = !string.IsNullOrWhiteSpace(logoUrl)
            ? $"<div style=\"text-align:center;margin-bottom:24px;\"><img src=\"{WebUtility.HtmlEncode(logoUrl)}\" alt=\"{safeProduct}\" style=\"max-height:48px;\" /></div>"
            : $"<div style=\"text-align:center;margin-bottom:24px;font-size:20px;font-weight:600;color:{safePrimary};\">{safeProduct}</div>";

        var support = !string.IsNullOrWhiteSpace(supportEmail)
            ? $"<p style=\"color:#6b7280;font-size:12px;margin-top:16px;\">Questions? Contact <a href=\"mailto:{WebUtility.HtmlEncode(supportEmail)}\" style=\"color:{safePrimary};\">{WebUtility.HtmlEncode(supportEmail)}</a>.</p>"
            : string.Empty;

        var subject = $"Test email from {productName}";
        var html = $@"<div style=""font-family:-apple-system,Segoe UI,Roboto,Helvetica,Arial,sans-serif;max-width:600px;margin:0 auto;padding:24px;color:#111827;"">
{header}
<h2 style=""color:{safePrimary};margin:0 0 12px 0;font-size:20px;"">Test Email</h2>
<p style=""margin:0 0 12px 0;line-height:1.5;"">This is a test email from <strong>{safeProduct}</strong> to verify your email settings are configured correctly.</p>
<p style=""color:#6b7280;font-size:12px;margin-top:24px;"">If you received this email, your email configuration is working.</p>
{support}
<hr style=""border:none;border-top:1px solid #e5e7eb;margin:24px 0;"" />
<p style=""color:#9ca3af;font-size:11px;text-align:center;"">{safeFooter}</p>
</div>";

        return (subject, html);
    }

    /// <summary>
    /// Get platform license status — tier, expiry, licensed features.
    /// </summary>
    [HttpGet("license")]
    [Authorize(Policy = "SuperAdmin")]
    public ActionResult<LicenseStatusDto> GetLicenseStatus()
    {
        if (_licenseValidator == null)
        {
            return Ok(new LicenseStatusDto
            {
                Tier = "none",
                IsValid = false,
                Message = "No license validator configured"
            });
        }

        var info = _licenseValidator.GetLicenseInfo();

        return Ok(new LicenseStatusDto
        {
            Tier = info.Tier.ToString().ToLowerInvariant(),
            IsValid = !info.IsExpired,
            ExpiresAt = info.ExpiresAt,
            IsExpired = info.IsExpired,
            IsTrial = info.IsTrial,
            DaysUntilExpiration = info.DaysUntilExpiration,
            MaxTenants = info.Limits?.MaxTenants ?? -1,
            Features = info.Features,
            AddOns = info.AddOns,
        });
    }

    private static ProtocolSettingsDto MapProtocolSettings(Core.Domain.Entities.TenantProtocolConfiguration? config)
    {
        if (config == null)
            return new ProtocolSettingsDto();

        return new ProtocolSettingsDto
        {
            EnforceOAuth21 = config.EnforceOAuth21,
            EnableDynamicClientRegistration = config.EnableDynamicClientRegistration,
            AllowOpenDynamicRegistration = config.AllowOpenDynamicRegistration,
            DynamicRegistrationAllowedScopes = DeserializeList(config.DynamicRegistrationAllowedScopesJson),
            DynamicRegistrationAllowedGrantTypes = DeserializeList(config.DynamicRegistrationAllowedGrantTypesJson),
            DynamicRegistrationRequirePkce = config.DynamicRegistrationRequirePkce,
            DynamicRegistrationMaxRedirectUris = config.DynamicRegistrationMaxRedirectUris,
            DynamicRegistrationRequiredScope = config.DynamicRegistrationRequiredScope,
            DynamicRegistrationRequiredClaim = config.DynamicRegistrationRequiredClaim,
            DynamicRegistrationRequiredClaimValue = config.DynamicRegistrationRequiredClaimValue,
            PrefixScopeSuffixMaxLength = ApiScopePrefixes.EffectiveDefaultSuffixMaxLength(config.PrefixScopeSuffixMaxLength),
        };
    }

    private static string? SerializeList(List<string>? values) =>
        values is { Count: > 0 } ? System.Text.Json.JsonSerializer.Serialize(values) : null;

    private static List<string>? DeserializeList(string? json)
    {
        if (string.IsNullOrEmpty(json)) return null;
        try
        {
            return System.Text.Json.JsonSerializer.Deserialize<List<string>>(json);
        }
        catch (System.Text.Json.JsonException)
        {
            return null;
        }
    }

    private static string? NullIfBlank(string? value) => string.IsNullOrWhiteSpace(value) ? null : value.Trim();

    private ServerSettingsDto GetDefaultSettings(Core.Domain.Entities.Tenant? tenant)
    {
        return new ServerSettingsDto
        {
            TokenSettings = GetDefaultTokenSettings(),
            SecuritySettings = GetDefaultSecuritySettings(),
            CorsSettings = GetDefaultCorsSettings(),
            AccountEmailSettings = new AccountEmailSettingsDto(),
            TenantSettings = tenant != null ? new TenantSettingsDto
            {
                UseJourneyFlow = tenant.UseJourneyFlow,
                JourneyFallbackToStandard = tenant.JourneyFallbackToStandard,
                AllowSelfRegistration = tenant.AllowSelfRegistration,
                RequireTermsAcceptance = tenant.RequireTermsAcceptance,
                RequireEmailVerification = tenant.RequireEmailVerification,
                TermsOfServiceUrl = tenant.TermsOfServiceUrl,
                PrivacyPolicyUrl = tenant.PrivacyPolicyUrl,
                AllowedEmailDomains = tenant.AllowedEmailDomains,
                EnableLocalLogin = tenant.EnableLocalLogin,
                EnablePasskey = tenant.EnablePasskey
            } : new TenantSettingsDto(),
            ProtocolSettings = MapProtocolSettings(tenant?.ProtocolConfiguration)
        };
    }

    private TokenSettingsDto GetDefaultTokenSettings() => new()
    {
        DefaultAccessTokenLifetime = _configuration.GetValue("Oluso:AccessTokenLifetime", 3600),
        DefaultIdentityTokenLifetime = _configuration.GetValue("Oluso:IdentityTokenLifetime", 300),
        DefaultRefreshTokenLifetime = _configuration.GetValue("Oluso:RefreshTokenLifetime", 2592000)
    };

    private SecuritySettingsDto GetDefaultSecuritySettings() => new()
    {
        RequireHttps = _configuration.GetValue("Oluso:RequireHttpsMetadata", true),
        EmitStaticClaims = _configuration.GetValue("Oluso:EmitStaticAudienceClaim", false),
        EnableBackchannelLogout = _configuration.GetValue("Oluso:EnableBackchannelLogout", true)
    };

    // A tenant's own list starts empty; the platform's configured origins (Oluso:Cors:Origins)
    // apply everywhere already and aren't the tenant's to edit.
    private static CorsSettingsDto GetDefaultCorsSettings() => new()
    {
        AllowAllOrigins = false,
        AllowedOrigins = new List<string>()
    };
}
