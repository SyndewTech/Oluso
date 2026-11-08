namespace Oluso.Admin.Dto.Settings;

public class ServerSettingsDto
{
    public TokenSettingsDto TokenSettings { get; set; } = new();
    public SecuritySettingsDto SecuritySettings { get; set; } = new();
    public CorsSettingsDto CorsSettings { get; set; } = new();
    public BrandingSettingsDto? BrandingSettings { get; set; }
    public TenantSettingsDto? TenantSettings { get; set; }
    public AccountEmailSettingsDto? AccountEmailSettings { get; set; }
    public ProtocolSettingsDto? ProtocolSettings { get; set; }
}

/// <summary>
/// The tenant's protocol options shown on the Settings page's Protocol tab (stored on the tenant's protocol
/// configuration, the same record as <c>api/admin/tenants/{id}/protocol-configuration</c>).
/// </summary>
public class ProtocolSettingsDto
{
    public bool EnforceOAuth21 { get; set; }
    public bool EnableDynamicClientRegistration { get; set; }
    public bool AllowOpenDynamicRegistration { get; set; }
    public List<string>? DynamicRegistrationAllowedScopes { get; set; }
    public List<string>? DynamicRegistrationAllowedGrantTypes { get; set; }
    public bool DynamicRegistrationRequirePkce { get; set; } = true;
    public int DynamicRegistrationMaxRedirectUris { get; set; } = 10;
    public string? DynamicRegistrationRequiredScope { get; set; }
    public string? DynamicRegistrationRequiredClaim { get; set; }
    public string? DynamicRegistrationRequiredClaimValue { get; set; }

    /// <summary>
    /// The longest suffix a prefix scope accepts when it doesn't set its own maximum (1–2048; default 256).
    /// </summary>
    public int PrefixScopeSuffixMaxLength { get; set; } = 256;
}

/// <summary>How long the links and codes in account emails stay valid for this tenant.</summary>
public class AccountEmailSettingsDto
{
    public const int MinAccountLinkMinutes = 5, MaxAccountLinkMinutes = 30 * 24 * 60;
    public const int MinMfaCodeMinutes = 1, MaxMfaCodeMinutes = 60;
    public const int MinInvitationDays = 1, MaxInvitationDays = 90;

    /// <summary>Password-reset and email-confirmation links.</summary>
    public int AccountLinkLifetimeMinutes { get; set; } = 24 * 60;

    /// <summary>MFA codes sent by email or SMS, and the code that confirms a new email address.</summary>
    public int MfaCodeLifetimeMinutes { get; set; } = 10;

    /// <summary>Default invitation lifetime, also used when one is re-sent.</summary>
    public int InvitationLifetimeDays { get; set; } = 7;

    /// <summary>The first out-of-range value, as a message; null when all are valid.</summary>
    public string? Validate() =>
        AccountLinkLifetimeMinutes is < MinAccountLinkMinutes or > MaxAccountLinkMinutes
            ? $"Account link lifetime must be between {MinAccountLinkMinutes} and {MaxAccountLinkMinutes} minutes."
        : MfaCodeLifetimeMinutes is < MinMfaCodeMinutes or > MaxMfaCodeMinutes
            ? $"MFA code lifetime must be between {MinMfaCodeMinutes} and {MaxMfaCodeMinutes} minutes."
        : InvitationLifetimeDays is < MinInvitationDays or > MaxInvitationDays
            ? $"Invitation lifetime must be between {MinInvitationDays} and {MaxInvitationDays} days."
        : null;
}

public class TenantSettingsDto
{
    public bool UseJourneyFlow { get; set; } = true;
    public bool JourneyFallbackToStandard { get; set; }
    public bool AllowSelfRegistration { get; set; }
    public bool RequireTermsAcceptance { get; set; }
    public bool RequireEmailVerification { get; set; }
    public string? TermsOfServiceUrl { get; set; }
    public string? PrivacyPolicyUrl { get; set; }
    public string? AllowedEmailDomains { get; set; }
    public bool EnableLocalLogin { get; set; } = true;
    public bool EnablePasskey { get; set; } = true;
}

public class TokenSettingsDto
{
    public int DefaultAccessTokenLifetime { get; set; }
    public int DefaultIdentityTokenLifetime { get; set; }
    public int DefaultRefreshTokenLifetime { get; set; }
}

public class SecuritySettingsDto
{
    public bool RequireHttps { get; set; }
    public bool EmitStaticClaims { get; set; }
    public bool EnableBackchannelLogout { get; set; }
}

public class CorsSettingsDto
{
    public bool AllowAllOrigins { get; set; }
    public List<string> AllowedOrigins { get; set; } = new();
}

public class BrandingSettingsDto
{
    public string? PrimaryColor { get; set; }
    public string? SecondaryColor { get; set; }
    public string? BackgroundColor { get; set; }
    public string? LogoUrl { get; set; }
    public string? FaviconUrl { get; set; }
    public string? CompanyName { get; set; }
    public string? SupportEmail { get; set; }
    public string? PrivacyPolicyUrl { get; set; }
    public string? TermsOfServiceUrl { get; set; }
    public string? CustomCss { get; set; }
    public string? FooterText { get; set; }
}

public class SendTestEmailRequest
{
    public string To { get; set; } = null!;
}

public class LicenseStatusDto
{
    public string Tier { get; set; } = "none";
    public bool IsValid { get; set; }
    public DateTime? ExpiresAt { get; set; }
    public bool IsExpired { get; set; }
    public bool IsTrial { get; set; }
    public int DaysUntilExpiration { get; set; }
    public int MaxTenants { get; set; } = -1;
    public List<string> Features { get; set; } = [];
    public List<string> AddOns { get; set; } = [];
    public string? Message { get; set; }
}
