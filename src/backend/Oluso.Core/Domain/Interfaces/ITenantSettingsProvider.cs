namespace Oluso.Core.Domain.Interfaces;

/// <summary>
/// Provides access to tenant-specific settings.
/// In multi-tenant deployments, each tenant can have different configuration.
/// </summary>
public interface ITenantSettingsProvider
{
    /// <summary>
    /// Gets a typed settings section for the current tenant
    /// </summary>
    Task<T?> GetSettingsAsync<T>(CancellationToken cancellationToken = default) where T : class;

    /// <summary>
    /// Gets a specific setting value for the current tenant
    /// </summary>
    Task<T?> GetValueAsync<T>(string key, T? defaultValue = default, CancellationToken cancellationToken = default);

    /// <summary>
    /// Gets token settings for the current tenant
    /// </summary>
    Task<TenantTokenSettings> GetTokenSettingsAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// Gets password policy settings for the current tenant
    /// </summary>
    Task<TenantPasswordSettings> GetPasswordSettingsAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// Gets branding settings for the current tenant
    /// </summary>
    Task<TenantBrandingSettings> GetBrandingSettingsAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// Gets protocol settings for the current tenant (OIDC discovery document configuration)
    /// </summary>
    Task<TenantProtocolSettings> GetProtocolSettingsAsync(CancellationToken cancellationToken = default);

}

/// <summary>
/// Token settings for a tenant.
/// These override default configuration when present.
/// </summary>
public class TenantTokenSettings
{
    /// <summary>
    /// Default access token lifetime in seconds
    /// </summary>
    public int DefaultAccessTokenLifetime { get; set; } = 3600;

    /// <summary>
    /// Default ID token lifetime in seconds
    /// </summary>
    public int DefaultIdentityTokenLifetime { get; set; } = 300;

    /// <summary>
    /// Default refresh token lifetime in seconds
    /// </summary>
    public int DefaultRefreshTokenLifetime { get; set; } = 2592000;

    /// <summary>
    /// Tenant-specific issuer URI override. If null, uses default issuer.
    /// </summary>
    public string? IssuerUri { get; set; }
}

/// <summary>
/// Security settings for a tenant
/// </summary>
public class TenantSecuritySettings
{
    public bool RequireHttps { get; set; } = true;
    public bool EmitStaticClaims { get; set; }
    public bool EnableBackchannelLogout { get; set; } = true;
}

/// <summary>
/// Password policy settings for a tenant
/// </summary>
public class TenantPasswordSettings
{
    public int MinimumLength { get; set; } = 8;
    public int MaximumLength { get; set; } = 128;
    public bool RequireDigit { get; set; } = true;
    public bool RequireLowercase { get; set; } = true;
    public bool RequireUppercase { get; set; } = true;
    public bool RequireNonAlphanumeric { get; set; } = true;
    public int RequiredUniqueChars { get; set; } = 4;
    public int PasswordHistoryCount { get; set; } = 0;
    public int PasswordExpirationDays { get; set; } = 0;
    public int MaxFailedAttempts { get; set; } = 5;
    public int LockoutDurationMinutes { get; set; } = 15;
    public bool BlockCommonPasswords { get; set; } = true;

    /// <summary>
    /// Check passwords against the Have I Been Pwned API to block breached passwords.
    /// Uses k-anonymity so the actual password is never transmitted.
    /// </summary>
    public bool CheckBreachedPasswords { get; set; } = false;

    public string? CustomRegexPattern { get; set; }
    public string? CustomRegexErrorMessage { get; set; }

    public static TenantPasswordSettings Default => new();
}

/// <summary>
/// Branding settings for a tenant
/// </summary>
public class TenantBrandingSettings
{
    public string? LogoUrl { get; set; }
    public string? FaviconUrl { get; set; }
    public string? PrimaryColor { get; set; }
    public string? SecondaryColor { get; set; }
    public string? BackgroundColor { get; set; }
    public string? CustomCss { get; set; }

    public static TenantBrandingSettings Default => new();
}

/// <summary>
/// Protocol settings for a tenant (OIDC discovery document configuration).
/// Controls what capabilities are advertised and enabled for this tenant.
/// </summary>
public class TenantProtocolSettings
{
    /// <summary>
    /// Grant types allowed for this tenant.
    /// If empty/null, all registered grant types are allowed.
    /// </summary>
    public List<string>? AllowedGrantTypes { get; set; }

    /// <summary>
    /// Response types allowed for this tenant.
    /// If empty/null, defaults to all standard OIDC response types.
    /// </summary>
    public List<string>? AllowedResponseTypes { get; set; }

    /// <summary>
    /// Token endpoint authentication methods allowed for this tenant.
    /// If empty/null, all methods are allowed.
    /// </summary>
    public List<string>? AllowedTokenEndpointAuthMethods { get; set; }

    /// <summary>
    /// Subject types supported for this tenant.
    /// Defaults to ["public", "pairwise"].
    /// </summary>
    public List<string>? SubjectTypesSupported { get; set; }

    /// <summary>
    /// ID token signing algorithms supported.
    /// Defaults to ["RS256", "ES256"].
    /// </summary>
    public List<string>? IdTokenSigningAlgValuesSupported { get; set; }

    /// <summary>
    /// Code challenge methods supported (PKCE).
    /// Defaults to ["S256", "plain"].
    /// </summary>
    public List<string>? CodeChallengeMethodsSupported { get; set; }

    /// <summary>
    /// Whether to require Pushed Authorization Requests (PAR) for all clients in this tenant.
    /// </summary>
    public bool RequirePushedAuthorizationRequests { get; set; }

    /// <summary>
    /// Whether to require PKCE for all clients in this tenant.
    /// </summary>
    public bool RequirePkce { get; set; }

    /// <summary>
    /// Whether to allow plain PKCE method. Recommended: false for security.
    /// </summary>
    public bool AllowPlainPkce { get; set; }

    /// <summary>
    /// Whether DPoP is required for all clients in this tenant.
    /// </summary>
    public bool RequireDPoP { get; set; }

    /// <summary>
    /// DPoP signing algorithms supported.
    /// Defaults to ["RS256", "ES256"].
    /// </summary>
    public List<string>? DPoPSigningAlgValuesSupported { get; set; }

    /// <summary>
    /// Whether claims parameter is supported (for requesting specific claims).
    /// </summary>
    public bool ClaimsParameterSupported { get; set; }

    /// <summary>
    /// Whether request parameter (JAR) is supported.
    /// </summary>
    public bool RequestParameterSupported { get; set; } = true;

    /// <summary>
    /// Whether request_uri parameter is supported.
    /// </summary>
    public bool RequestUriParameterSupported { get; set; } = true;

    /// <summary>
    /// Whether frontchannel logout is enabled.
    /// </summary>
    public bool FrontchannelLogoutSupported { get; set; } = true;

    /// <summary>
    /// Whether backchannel logout is enabled.
    /// </summary>
    public bool BackchannelLogoutSupported { get; set; } = true;

    public static TenantProtocolSettings Default => new();
}

/// <summary>
/// All tenant settings combined (core settings only)
/// </summary>
public class TenantSettings
{
    public TenantTokenSettings TokenSettings { get; set; } = new();
    public TenantSecuritySettings SecuritySettings { get; set; } = new();
    public TenantPasswordSettings PasswordSettings { get; set; } = new();
    public TenantBrandingSettings BrandingSettings { get; set; } = new();
    public TenantProtocolSettings ProtocolSettings { get; set; } = new();
}

/// <summary>
/// Service for resolving the issuer URI based on tenant context
/// </summary>
public interface IIssuerResolver
{
    /// <summary>
    /// Gets the issuer URI for the current tenant.
    /// Priority: Tenant IssuerUri > Tenant CustomDomain > Server config > Request host
    /// </summary>
    Task<string> GetIssuerAsync(CancellationToken cancellationToken = default);
}
