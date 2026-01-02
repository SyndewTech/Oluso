namespace Oluso.Core.Domain.Entities;

/// <summary>
/// Represents a tenant in a multi-tenant deployment
/// </summary>
public class Tenant
{
    public string Id { get; set; } = default!;
    public string Name { get; set; } = default!;
    public string? DisplayName { get; set; }
    public string Identifier { get; set; } = default!;
    public bool Enabled { get; set; } = true;
    public string? Description { get; set; }
    public string? Configuration { get; set; }
    public DateTime Created { get; set; } = DateTime.UtcNow;
    public DateTime? Updated { get; set; }

    /// <summary>
    /// Connection string override for tenant-specific database (optional)
    /// </summary>
    public string? ConnectionString { get; set; }

    /// <summary>
    /// Custom domain for this tenant (e.g., auth.customer.com)
    /// </summary>
    public string? CustomDomain { get; set; }

    /// <summary>
    /// Branding settings (logo URL, colors, etc.)
    /// </summary>
    public TenantBranding? Branding { get; set; }

    /// <summary>
    /// Password policy settings for this tenant
    /// </summary>
    public TenantPasswordPolicy? PasswordPolicy { get; set; }

    /// <summary>
    /// OIDC protocol configuration for this tenant
    /// </summary>
    public TenantProtocolConfiguration? ProtocolConfiguration { get; set; }

    /// <summary>
    /// Subscription/plan tier for feature gating
    /// </summary>
    public string? PlanId { get; set; }
    public DateTime? PlanExpiresAt { get; set; }

    // Registration settings

    /// <summary>
    /// Whether users can self-register for this tenant
    /// </summary>
    public bool AllowSelfRegistration { get; set; } = true;

    /// <summary>
    /// Whether terms acceptance is required during registration
    /// </summary>
    public bool RequireTermsAcceptance { get; set; }

    /// <summary>
    /// URL to terms of service
    /// </summary>
    public string? TermsOfServiceUrl { get; set; }

    /// <summary>
    /// URL to privacy policy
    /// </summary>
    public string? PrivacyPolicyUrl { get; set; }

    /// <summary>
    /// Whether email verification is required after registration
    /// </summary>
    public bool RequireEmailVerification { get; set; } = true;

    /// <summary>
    /// Allowed email domains for registration (null = all allowed)
    /// Comma-separated list of domains
    /// </summary>
    public string? AllowedEmailDomains { get; set; }

    /// <summary>
    /// Whether to use journey-based authentication flow or standalone pages.
    /// Defaults to true (use journeys). Can be overridden per-client.
    /// </summary>
    public bool UseJourneyFlow { get; set; } = true;

    /// <summary>
    /// Whether local username/password login is enabled for this tenant.
    /// If false, users must use external identity providers.
    /// Can be overridden per-client.
    /// </summary>
    public bool EnableLocalLogin { get; set; } = true;
}

/// <summary>
/// Branding settings for a tenant (logo, colors, custom CSS)
/// </summary>
public class TenantBranding
{
    public int Id { get; set; }
    public string TenantId { get; set; } = default!;
    public Tenant Tenant { get; set; } = default!;
    public string? LogoUrl { get; set; }
    public string? FaviconUrl { get; set; }
    public string? PrimaryColor { get; set; }
    public string? SecondaryColor { get; set; }
    public string? BackgroundColor { get; set; }
    public string? CustomCss { get; set; }
}

/// <summary>
/// Password policy settings for a tenant
/// </summary>
public class TenantPasswordPolicy
{
    public int Id { get; set; }
    public string TenantId { get; set; } = default!;
    public Tenant Tenant { get; set; } = default!;

    /// <summary>
    /// Minimum password length (default: 8)
    /// </summary>
    public int MinimumLength { get; set; } = 8;

    /// <summary>
    /// Maximum password length (default: 128, 0 = no limit)
    /// </summary>
    public int MaximumLength { get; set; } = 128;

    /// <summary>
    /// Require at least one digit (0-9)
    /// </summary>
    public bool RequireDigit { get; set; } = true;

    /// <summary>
    /// Require at least one lowercase letter (a-z)
    /// </summary>
    public bool RequireLowercase { get; set; } = true;

    /// <summary>
    /// Require at least one uppercase letter (A-Z)
    /// </summary>
    public bool RequireUppercase { get; set; } = true;

    /// <summary>
    /// Require at least one non-alphanumeric character (!@#$%^&* etc.)
    /// </summary>
    public bool RequireNonAlphanumeric { get; set; } = true;

    /// <summary>
    /// Minimum number of unique characters required
    /// </summary>
    public int RequiredUniqueChars { get; set; } = 4;

    /// <summary>
    /// Number of previous passwords to check against (0 = don't check history)
    /// </summary>
    public int PasswordHistoryCount { get; set; } = 0;

    /// <summary>
    /// Password expiration in days (0 = never expires)
    /// </summary>
    public int PasswordExpirationDays { get; set; } = 0;

    /// <summary>
    /// Maximum failed attempts before lockout
    /// </summary>
    public int MaxFailedAttempts { get; set; } = 5;

    /// <summary>
    /// Lockout duration in minutes
    /// </summary>
    public int LockoutDurationMinutes { get; set; } = 15;

    /// <summary>
    /// Block common/weak passwords
    /// </summary>
    public bool BlockCommonPasswords { get; set; } = true;

    /// <summary>
    /// Check passwords against the Have I Been Pwned API to block breached passwords.
    /// Uses k-anonymity so the actual password is never transmitted.
    /// </summary>
    public bool CheckBreachedPasswords { get; set; } = false;

    /// <summary>
    /// Custom regex pattern for password validation (optional)
    /// </summary>
    public string? CustomRegexPattern { get; set; }

    /// <summary>
    /// Custom error message for regex validation
    /// </summary>
    public string? CustomRegexErrorMessage { get; set; }

    /// <summary>
    /// Returns a default password policy
    /// </summary>
    public static TenantPasswordPolicy Default => new();
}

/// <summary>
/// OIDC protocol configuration for a tenant.
/// Controls what protocol features are enabled/advertised for this tenant.
/// </summary>
public class TenantProtocolConfiguration
{
    public int Id { get; set; }
    public string TenantId { get; set; } = default!;
    public Tenant Tenant { get; set; } = default!;

    /// <summary>
    /// Grant types allowed for this tenant (JSON array).
    /// If empty/null, all registered grant types are allowed.
    /// </summary>
    public string? AllowedGrantTypesJson { get; set; }

    /// <summary>
    /// Response types allowed for this tenant (JSON array).
    /// If empty/null, defaults to all standard OIDC response types.
    /// </summary>
    public string? AllowedResponseTypesJson { get; set; }

    /// <summary>
    /// Token endpoint authentication methods allowed (JSON array).
    /// If empty/null, all methods are allowed.
    /// </summary>
    public string? AllowedTokenEndpointAuthMethodsJson { get; set; }

    /// <summary>
    /// Subject types supported (JSON array).
    /// Defaults to ["public", "pairwise"].
    /// </summary>
    public string? SubjectTypesSupportedJson { get; set; }

    /// <summary>
    /// ID token signing algorithms supported (JSON array).
    /// Defaults to ["RS256", "ES256"].
    /// </summary>
    public string? IdTokenSigningAlgValuesSupportedJson { get; set; }

    /// <summary>
    /// Code challenge methods supported (JSON array).
    /// Defaults to ["S256", "plain"].
    /// </summary>
    public string? CodeChallengeMethodsSupportedJson { get; set; }

    /// <summary>
    /// DPoP signing algorithms supported (JSON array).
    /// Defaults to ["RS256", "ES256"].
    /// </summary>
    public string? DPoPSigningAlgValuesSupportedJson { get; set; }

    /// <summary>
    /// Whether to require Pushed Authorization Requests (PAR) for all clients.
    /// </summary>
    public bool RequirePushedAuthorizationRequests { get; set; }

    /// <summary>
    /// Whether to require PKCE for all clients.
    /// </summary>
    public bool RequirePkce { get; set; }

    /// <summary>
    /// Whether to allow plain PKCE method. Recommended: false for security.
    /// </summary>
    public bool AllowPlainPkce { get; set; }

    /// <summary>
    /// Whether DPoP is required for all clients.
    /// </summary>
    public bool RequireDPoP { get; set; }

    /// <summary>
    /// Whether claims parameter is supported.
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

    // Note: Package-specific settings (e.g., SAML IdP, SCIM, etc.) are stored in Tenant.Configuration JSON.
    // Each package uses its own section key (e.g., "SamlIdp", "Scim") to avoid coupling to Core.

    public DateTime Created { get; set; } = DateTime.UtcNow;
    public DateTime? Updated { get; set; }
}
