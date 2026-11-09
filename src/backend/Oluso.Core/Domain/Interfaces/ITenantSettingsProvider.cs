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
    /// Password policy settings for a specific tenant — the policy a user's account is held to,
    /// which can differ from the request's tenant (e.g. sign-in on the platform host).
    /// </summary>
    Task<TenantPasswordSettings> GetPasswordSettingsAsync(string tenantId, CancellationToken cancellationToken = default);

    /// <summary>
    /// How long the links and codes in account emails stay valid for the current tenant.
    /// </summary>
    Task<TenantAccountEmailSettings> GetAccountEmailSettingsAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// Account email lifetimes for a specific tenant, for mail sent on behalf of a tenant
    /// other than the one handling the request (e.g. the welcome email to a new tenant).
    /// </summary>
    Task<TenantAccountEmailSettings> GetAccountEmailSettingsAsync(string tenantId, CancellationToken cancellationToken = default);

    /// <summary>
    /// Gets branding settings for the current tenant
    /// </summary>
    Task<TenantBrandingSettings> GetBrandingSettingsAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// Gets branding settings for a specific tenant. Used by endpoints that
    /// iterate over multiple tenants (e.g. the tenant-switcher listing the
    /// tenants a user belongs to) where the ambient
    /// <see cref="ITenantContext"/> doesn't point at the tenant being read.
    /// </summary>
    Task<TenantBrandingSettings> GetBrandingSettingsAsync(string tenantId, CancellationToken cancellationToken = default);

    /// <summary>
    /// Gets protocol settings for the current tenant (OIDC discovery document configuration)
    /// </summary>
    Task<TenantProtocolSettings> GetProtocolSettingsAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// Gets locale settings for the current tenant (currency, date format, number format, timezone)
    /// </summary>
    Task<LocaleSettings> GetLocaleSettingsAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// Gets workspace apps settings for the current tenant (built-in app toggles and custom apps)
    /// </summary>
    Task<WorkspaceAppsSettings> GetWorkspaceAppsSettingsAsync(CancellationToken cancellationToken = default);
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
/// How long the links and codes in account emails stay valid. The emails state these
/// values, so what a recipient reads always matches what the platform enforces.
/// </summary>
public class TenantAccountEmailSettings
{
    public const string SectionKey = "AccountEmailSettings";

    /// <summary>
    /// Password-reset and email-confirmation links. A password-reset journey step uses the
    /// same token, so keep this at least as long as the step's code lifetime. Default: 1 day.
    /// </summary>
    public int AccountLinkLifetimeMinutes { get; set; } = 24 * 60;

    /// <summary>MFA codes sent by email or SMS, and the code that confirms a new email address. Default: 10 minutes.</summary>
    public int MfaCodeLifetimeMinutes { get; set; } = 10;

    /// <summary>Default lifetime of organisation invitations, also used when one is re-sent. Default: 7 days.</summary>
    public int InvitationLifetimeDays { get; set; } = 7;
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

    /// <summary>
    /// When the breach database can't be reached: false accepts the password, true rejects it.
    /// See <see cref="Entities.TenantPasswordPolicy.RejectWhenBreachCheckUnavailable"/>.
    /// </summary>
    public bool RejectWhenBreachCheckUnavailable { get; set; } = false;

    public string? CustomRegexPattern { get; set; }
    public string? CustomRegexErrorMessage { get; set; }

    /// <summary>
    /// When false (default), users that have at least one linked external
    /// login (federated via SSO) cannot have a local password set, reset,
    /// or used for sign-in. Local credential operations on those users
    /// return a <c>federated_user_local_password_disallowed</c> failure.
    /// Set true only for tenants that explicitly want mixed-mode auth —
    /// e.g. a break-glass local password alongside the SSO link. Default
    /// matches the enterprise expectation that identity federation means
    /// the IdP owns the credential.
    /// </summary>
    public bool AllowLocalPasswordForFederatedUsers { get; set; } = false;

    public static TenantPasswordSettings Default => new();

    /// <summary>The effective settings for a tenant's stored password policy.</summary>
    public static TenantPasswordSettings FromPolicy(Entities.TenantPasswordPolicy policy) => new()
    {
        MinimumLength = policy.MinimumLength,
        MaximumLength = policy.MaximumLength,
        RequireDigit = policy.RequireDigit,
        RequireLowercase = policy.RequireLowercase,
        RequireUppercase = policy.RequireUppercase,
        RequireNonAlphanumeric = policy.RequireNonAlphanumeric,
        RequiredUniqueChars = policy.RequiredUniqueChars,
        PasswordHistoryCount = policy.PasswordHistoryCount,
        PasswordExpirationDays = policy.PasswordExpirationDays,
        MaxFailedAttempts = policy.MaxFailedAttempts,
        LockoutDurationMinutes = policy.LockoutDurationMinutes,
        BlockCommonPasswords = policy.BlockCommonPasswords,
        CheckBreachedPasswords = policy.CheckBreachedPasswords,
        RejectWhenBreachCheckUnavailable = policy.RejectWhenBreachCheckUnavailable,
        CustomRegexPattern = policy.CustomRegexPattern,
        CustomRegexErrorMessage = policy.CustomRegexErrorMessage
    };

    /// <summary>
    /// True when a password last changed at <paramref name="passwordChangedAt"/> is older than
    /// <see cref="PasswordExpirationDays"/> allows. A password with no recorded change date is
    /// as old as the account (<paramref name="accountCreatedAt"/>).
    /// </summary>
    public bool IsPasswordExpired(DateTime? passwordChangedAt, DateTime accountCreatedAt, DateTime utcNow) =>
        PasswordExpirationDays > 0
        && (passwordChangedAt ?? accountCreatedAt).AddDays(PasswordExpirationDays) <= utcNow;
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
    public string? SupportEmail { get; set; }
    public string? FooterText { get; set; }
    public bool HideEmbedBranding { get; set; }

    public static TenantBrandingSettings Default => new();
}

/// <summary>
/// Tenant-wide email settings, stored as their own tenant-config section.
/// </summary>
public class TenantEmailSettings
{
    public const string SectionKey = "EmailSettings";

    /// <summary>
    /// Inbox that receives replies to this tenant's mail when the sender profile
    /// sets no reply-to of its own, including mail sent on the platform's shared
    /// sender. Only used once <see cref="ReplyToVerifiedAt"/> is set.
    /// </summary>
    public string? ReplyToAddress { get; set; }

    /// <summary>When the inbox owner confirmed <see cref="ReplyToAddress"/>.</summary>
    public DateTime? ReplyToVerifiedAt { get; set; }

    /// <summary>An address awaiting confirmation; the current one stays in use until then.</summary>
    public string? PendingReplyToAddress { get; set; }

    public DateTime? PendingReplyToSentAt { get; set; }

    /// <summary>The confirmed reply-to, or null when none is confirmed.</summary>
    public string? VerifiedReplyTo => ReplyToVerifiedAt != null && !string.IsNullOrWhiteSpace(ReplyToAddress) ? ReplyToAddress : null;
}

//Fixme: Expand protocol settings and check it is not duplicate of other settings
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
    /// Whether to enforce OAuth 2.1 security profile.
    /// When true: implicit grant is rejected, PKCE is required, plain PKCE is disallowed,
    /// and Resource Owner Password Credentials (ROPC) grant is rejected.
    /// </summary>
    public bool EnforceOAuth21 { get; set; }

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

    /// <summary>
    /// The longest suffix a prefix scope accepts when it doesn't set its own <c>suffix_max_length</c>.
    /// Null means <see cref="Oluso.Core.Services.ApiScopePrefixes.DefaultSuffixMaxLength"/>.
    /// </summary>
    public int? PrefixScopeSuffixMaxLength { get; set; }

    /// <summary>
    /// Scopes that earn a refresh token without <c>offline_access</c>, for clients allowed the
    /// <c>refresh_token</c> grant. Matrix clients (Matrix spec "OAuth 2.0 API") never ask for
    /// <c>offline_access</c> but must be able to refresh. Null means
    /// <see cref="DefaultRefreshImplyingScopes"/> (the Matrix client API scopes); an empty list turns it off.
    /// </summary>
    public List<string>? RefreshImplyingScopes { get; set; }

    /// <summary>The stable and unstable (MSC2967) Matrix client API scopes.</summary>
    public static readonly IReadOnlyList<string> DefaultRefreshImplyingScopes = new[]
    {
        Oluso.Core.Protocols.MatrixScopes.Api,
        Oluso.Core.Protocols.MatrixScopes.UnstableApi,
    };

    /// <summary>The refresh-implying scopes in effect: the tenant's list, or the default when unset.</summary>
    public IReadOnlyList<string> EffectiveRefreshImplyingScopes =>
        (IReadOnlyList<string>?)RefreshImplyingScopes ?? DefaultRefreshImplyingScopes;

    // ========================================
    // Dynamic Client Registration (RFC 7591)
    // ========================================

    /// <summary>
    /// Whether Dynamic Client Registration is enabled for this tenant.
    /// </summary>
    public bool EnableDynamicClientRegistration { get; set; }

    /// <summary>
    /// Whether unauthenticated (open) registration is allowed.
    /// If false, an initial access token is required.
    /// </summary>
    public bool AllowOpenDynamicRegistration { get; set; }

    /// <summary>
    /// Scopes that dynamically registered clients are allowed to request.
    /// Values must exist in tenant's configured scopes (ApiScope/IdentityResource).
    /// If empty/null, defaults to openid, profile, email.
    /// </summary>
    public List<string>? DynamicRegistrationAllowedScopes { get; set; }

    /// <summary>
    /// Grant types that dynamically registered clients are allowed to use.
    /// Values must be a subset of tenant's AllowedGrantTypes.
    /// If empty/null, defaults to authorization_code and refresh_token.
    /// </summary>
    public List<string>? DynamicRegistrationAllowedGrantTypes { get; set; }

    /// <summary>
    /// Whether PKCE is required for dynamically registered clients.
    /// Recommended: true for security.
    /// </summary>
    public bool DynamicRegistrationRequirePkce { get; set; } = true;

    /// <summary>
    /// Maximum number of redirect URIs allowed per dynamically registered client.
    /// </summary>
    public int DynamicRegistrationMaxRedirectUris { get; set; } = 10;

    /// <summary>
    /// Required scope for initial access tokens when protected registration is enabled.
    /// If null/empty, any valid token is accepted.
    /// Example: "client:register" or "dcr:create"
    /// </summary>
    public string? DynamicRegistrationRequiredScope { get; set; }

    /// <summary>
    /// Required claim name for initial access tokens when protected registration is enabled.
    /// Used in conjunction with DynamicRegistrationRequiredClaimValue.
    /// If null/empty, no claim check is performed.
    /// Example: "role" or "permission"
    /// </summary>
    public string? DynamicRegistrationRequiredClaim { get; set; }

    /// <summary>
    /// Required claim value for initial access tokens when protected registration is enabled.
    /// The token must have the claim specified by DynamicRegistrationRequiredClaim with this value.
    /// If null/empty but DynamicRegistrationRequiredClaim is set, only claim presence is checked.
    /// Example: "admin" or "dcr-allowed"
    /// </summary>
    public string? DynamicRegistrationRequiredClaimValue { get; set; }

    // ========================================
    // Resource Indicators (RFC 8707)
    // ========================================

    /// <summary>
    /// Resource validation mode per RFC 8707.
    /// Controls how the authorization server validates the resource parameter.
    /// </summary>
    public ResourceValidationMode ResourceValidationMode { get; set; } = ResourceValidationMode.Optional;

    /// <summary>
    /// Whether the resource parameter is required on authorization/token requests.
    /// When true, requests without a resource parameter will be rejected.
    /// </summary>
    public bool RequireResourceParameter { get; set; }

    /// <summary>
    /// Default resource URI to use when none is specified in the request.
    /// Only applies when RequireResourceParameter is false.
    /// </summary>
    public string? DefaultResourceUri { get; set; }

    public static TenantProtocolSettings Default => new();
}

/// <summary>
/// Locale settings for a tenant (currency, date format, number format, timezone)
/// </summary>
public class LocaleSettings
{
    /// <summary>The organisation's currency. Empty until the tenant sets it; there is no default.</summary>
    public string CurrencyCode { get; set; } = string.Empty;
    public string CurrencySymbol { get; set; } = string.Empty;
    public string DateFormat { get; set; } = "MM/DD/YYYY";
    public string NumberFormat { get; set; } = "1,234.56";
    public string Locale { get; set; } = "en-US";
    public string? Timezone { get; set; }

    public static LocaleSettings Default => new();
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

    /// <summary>
    /// Gets the issuer URI for an explicit tenant — used when the tenant
    /// hasn't been resolved into <see cref="ITenantContext"/> (e.g. anonymous
    /// resolve endpoints that look the tenant up by identifier).
    /// Priority: Tenant CustomDomain > auto-subdomain > server config.
    /// </summary>
    Task<string> GetIssuerForTenantAsync(Oluso.Core.Domain.Entities.Tenant tenant, CancellationToken cancellationToken = default);

    /// <summary>
    /// Whether <paramref name="issuer"/> is one this server mints: the configured base issuer, a
    /// tenant subdomain of it, or — for <paramref name="tenant"/>, the tenant a token belongs to —
    /// that tenant's custom domain or issuer override. Used to accept bearer tokens on Oluso's own APIs.
    /// </summary>
    Task<bool> IsOwnIssuerAsync(string? issuer, Oluso.Core.Domain.Entities.Tenant? tenant, CancellationToken cancellationToken = default);

    /// <summary>
    /// Resolves both the effective issuer URI AND the canonical auto-subdomain
    /// URL for the current tenant. Use this when the caller needs to display
    /// the auto-subdomain regardless of CustomDomain (e.g. the admin "Automatic
    /// subdomain" panel) — <see cref="ResolvedIssuer.IssuerUri"/> still
    /// follows the same priority as <see cref="GetIssuerAsync"/>.
    /// </summary>
    Task<ResolvedIssuer> GetResolvedIssuerAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// Tenant-explicit variant of <see cref="GetResolvedIssuerAsync"/>.
    /// </summary>
    Task<ResolvedIssuer> GetResolvedIssuerForTenantAsync(Oluso.Core.Domain.Entities.Tenant tenant, CancellationToken cancellationToken = default);

    /// <summary>
    /// Builds the token's <c>iss</c> claim value — path-scoped
    /// <c>{configuredBase}/{tenantId}</c> (Azure / Microsoft pattern) or the
    /// bare base when <paramref name="tenantId"/> is null/_platform. Pure
    /// compute, no DB. Distinct from <see cref="GetIssuerAsync"/> which is
    /// subdomain-based for hosting and discovery.
    /// </summary>
    string BuildTokenIssuer(string? tenantId);
}

/// <summary>
/// Bundle of every issuer-derived URL the resolver knows about. Lets a
/// caller pick the one that fits — <see cref="IssuerUri"/> for IdP / webhook
/// callbacks (CustomDomain wins), <see cref="AutoSubdomain"/> for canonical
/// {identifier}.{base-host} display (independent of CustomDomain).
/// </summary>
public record ResolvedIssuer
{
    /// <summary>Effective issuer URI — same value <see cref="IIssuerResolver.GetIssuerAsync"/> returns.</summary>
    public required string IssuerUri { get; init; }

    /// <summary>
    /// Canonical {identifier}.{base-host} URL, ignoring CustomDomain. Null
    /// for the platform tenant (which lives at the base host), tenants
    /// without an identifier, or when <c>Oluso:IssuerUri</c> isn't configured.
    /// </summary>
    public string? AutoSubdomain { get; init; }
}

/// <summary>
/// Resource validation mode per RFC 8707.
/// Controls how the authorization server validates resource parameters.
/// </summary>
public enum ResourceValidationMode
{
    /// <summary>
    /// Resource parameter is optional and not validated.
    /// Any absolute URI without fragment is accepted.
    /// </summary>
    Optional,

    /// <summary>
    /// Resource parameter is validated against registered resources.
    /// Per RFC 8707: "authorization servers MAY validate resource values against
    /// known/registered resources to ensure proper audience scoping."
    /// Unregistered resources are rejected.
    /// </summary>
    ValidateAgainstRegistered,

    /// <summary>
    /// Strict mode: resource parameter is required and must match a registered resource.
    /// Combines RequireResourceParameter with validation against registered resources.
    /// </summary>
    Strict
}

/// <summary>
/// Workspace apps settings for a tenant.
/// Controls which built-in apps are enabled and defines custom external apps.
/// </summary>
public class WorkspaceAppsSettings
{
    public Dictionary<string, bool> BuiltInApps { get; set; } = new();
    public List<CustomWorkspaceApp> CustomApps { get; set; } = new();
    /// <summary>
    /// URL overrides for built-in apps. Key matches the app id (e.g. "admin", "account").
    /// Defaults: admin → /admin, account → /account.
    /// Set these when apps are served at custom paths or subdomains.
    /// </summary>
    public Dictionary<string, string> AppUrls { get; set; } = new();

    public static WorkspaceAppsSettings Default => new();
}

/// <summary>
/// A custom external workspace app configured by a tenant admin.
/// </summary>
public class CustomWorkspaceApp
{
    public string Id { get; set; } = Guid.NewGuid().ToString("N")[..8];
    public string Name { get; set; } = default!;
    public string? Description { get; set; }
    public string ExternalUrl { get; set; } = default!;
    public string Icon { get; set; } = "link";
    public string Color { get; set; } = "slate";
    public int Order { get; set; } = 100;
}
