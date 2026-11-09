using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Configuration;
using Oluso.Core.Domain.Interfaces;
using Oluso.Core.Licensing;
using Oluso.Core.UserJourneys;

namespace Oluso.Controllers;

/// <summary>
/// Public, unauthenticated instance metadata so the mobile app can discover
/// and brand itself against any Oluso deployment (SaaS or self-hosted) before
/// the user signs in. Sits alongside /.well-known/openid-configuration.
/// </summary>
[ApiController]
[Route(".well-known/oluso-instance")]
[AllowAnonymous]
public class OlusoInstanceDiscoveryController : ControllerBase
{
    private readonly IConfiguration _configuration;
    private readonly IIssuerResolver _issuerResolver;
    private readonly ITenantContext _tenantContext;
    private readonly ITenantSettingsProvider _tenantSettings;
    private readonly IJourneyPolicyStore _policyStore;

    public OlusoInstanceDiscoveryController(
        IConfiguration configuration,
        IIssuerResolver issuerResolver,
        ITenantContext tenantContext,
        ITenantSettingsProvider tenantSettings,
        IJourneyPolicyStore policyStore)
    {
        _configuration = configuration;
        _issuerResolver = issuerResolver;
        _tenantContext = tenantContext;
        _tenantSettings = tenantSettings;
        _policyStore = policyStore;
    }

    [HttpGet]
    public async Task<IActionResult> Get(CancellationToken cancellationToken)
    {
        var mobile = _configuration.GetSection("Oluso:Mobile");

        // The deployment's own branding (Oluso:Branding, as the rest of Oluṣọ reads it), else the older Oluso:Platform keys.
        var platformName = PlatformSetting("ProductName");
        var instanceName = PlatformSetting("CompanyName") ?? "Oluso";
        var displayName = platformName ?? instanceName;
        var logoUrl = PlatformSetting("LogoUrl");
        var accentColor = PlatformSetting("AccentColor");

        // Same resolution the OIDC discovery document uses — trusts the tenant
        // resolved by TenantResolutionMiddleware (which runs for anonymous
        // .well-known requests) and falls back to config/request host.
        var issuerUri = await _issuerResolver.GetIssuerAsync(cancellationToken);

        // Legal + support contacts the app surfaces in Settings. Platform config is the
        // no-tenant baseline; a tenant's own values override it. Null means "not
        // configured" and the app hides that row.
        var termsOfServiceUrl = PlatformSetting("TermsOfServiceUrl");
        var privacyPolicyUrl = PlatformSetting("PrivacyPolicyUrl");
        var supportEmail = PlatformSetting("SupportEmail");

        var tenant = _tenantContext.Tenant;
        string? defaultIdpScheme = null, defaultSignInPolicyId = null, defaultSsoUiMode = null;
        if (tenant is not null && tenant.Enabled)
        {
            instanceName = tenant.Name;
            displayName = tenant.Name;

            // Surface the tenant's own branding so the mobile app brands itself
            // per deployment; platform config is the no-tenant-override fallback.
            var branding = await _tenantSettings.GetBrandingSettingsAsync(cancellationToken);
            if (!string.IsNullOrEmpty(branding.LogoUrl)) logoUrl = branding.LogoUrl;
            if (!string.IsNullOrEmpty(branding.PrimaryColor)) accentColor = branding.PrimaryColor;
            if (!string.IsNullOrEmpty(branding.SupportEmail)) supportEmail = branding.SupportEmail;
            if (!string.IsNullOrEmpty(tenant.TermsOfServiceUrl)) termsOfServiceUrl = tenant.TermsOfServiceUrl;
            if (!string.IsNullOrEmpty(tenant.PrivacyPolicyUrl)) privacyPolicyUrl = tenant.PrivacyPolicyUrl;

            // SSO defaults so the app can domain_hint straight to the tenant's IdP and
            // launch its sign-in journey — mirrors the web tenant-sso landing flow
            // (defaultIdpScheme → domain_hint, defaultSignInPolicyId → policy,
            // defaultSsoUiMode → ui_mode). Edit-profile/change-password keep their own
            // policies under `policies`, overriding this sign-in default per flow.
            defaultIdpScheme = tenant.DefaultIdpScheme;
            defaultSignInPolicyId = tenant.DefaultSignInPolicyId;
            defaultSsoUiMode = tenant.DefaultSsoUiMode;
        }

        var features = ModuleRegistry.Instance.GetRegisteredModules()
            .Select(m => m.FeatureKey)
            .ToList();

        // Tenant-configured journey policies the app launches via the authorize
        // redirect (AD B2C style). Null when the tenant hasn't configured/enabled
        // that flow — the app hides the corresponding action.
        var profileEditPolicy = await _policyStore.GetByTypeAsync(JourneyType.ProfileEdit, cancellationToken);
        var changePasswordPolicy = await _policyStore.GetByTypeAsync(JourneyType.PasswordChange, cancellationToken);

        return Ok(new
        {
            instanceName,
            displayName,
            // The platform's product name, the same on every tenant host (never the tenant's name): what apps call the
            // sign-in service. Null when the deployment doesn't configure one.
            platformName,
            logoUrl,
            accentColor,
            issuerUri,
            oidcClientId = mobile["ClientId"] ?? "oluso-mobile",
            redirectUri = mobile["RedirectUri"] ?? "oluso://callback",
            supportedFeatures = features,
            minMobileAppVersion = mobile["MinAppVersion"] ?? "1.0.0",
            sso = new
            {
                defaultIdpScheme,
                defaultSignInPolicyId,
                defaultSsoUiMode,
            },
            policies = new
            {
                profileEdit = profileEditPolicy?.Slug,
                changePassword = changePasswordPolicy?.Slug,
            },
            legal = new
            {
                termsOfServiceUrl,
                privacyPolicyUrl,
                supportEmail,
            },
        });
    }

    /// <summary>A platform-level setting: <c>Oluso:Branding:{key}</c>, else <c>Oluso:Platform:{key}</c>; null when blank.</summary>
    private string? PlatformSetting(string key)
        => NonBlank(_configuration[$"Oluso:Branding:{key}"]) ?? NonBlank(_configuration[$"Oluso:Platform:{key}"]);

    private static string? NonBlank(string? value) => string.IsNullOrWhiteSpace(value) ? null : value.Trim();
}
