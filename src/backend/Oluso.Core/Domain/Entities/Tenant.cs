namespace Oluso.Core.Domain.Entities;

/// <summary>
/// Represents a tenant in a multi-tenant deployment.
/// Tenants provide data isolation for end users.
/// Tenants can optionally belong to an Organization for multi-environment management.
/// </summary>
public class Tenant
{
    public string Id { get; set; } = default!;
    public string Name { get; set; } = default!;
    public string? DisplayName { get; set; }
    public string Identifier { get; set; } = default!;
    public bool Enabled { get; set; } = true;
    public string? Description { get; set; }
    public DateTime Created { get; set; } = DateTime.UtcNow;
    public DateTime? Updated { get; set; }

    /// <summary>
    /// The organization this tenant belongs to.
    /// Every tenant must belong to an organization.
    /// </summary>
    public string OrganizationId { get; set; } = default!;

    /// <summary>
    /// Environment type for this tenant (Development, Staging, Production, etc.)
    /// </summary>
    public TenantEnvironment Environment { get; set; } = TenantEnvironment.Production;

    /// <summary>
    /// Navigation property to the parent organization
    /// </summary>
    public Organization Organization { get; set; } = default!;

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
    /// Whether this tenant routes authentication through the user-journey
    /// engine (custom policies + interactive UI) or the standalone classic
    /// Razor pages. Defaults to <c>false</c> so a freshly provisioned tenant
    /// works out of the box on standalone auth; flip to <c>true</c> via the
    /// admin UI once journey policies have been configured. Can be
    /// overridden per-client (Client.UseJourneyFlow).
    /// </summary>
    public bool UseJourneyFlow { get; set; } = false;

    /// <summary>
    /// With journey-based flow on, what happens when a sign-in names no policy (no
    /// <c>?policy=</c>) and the tenant has no enabled policy for it: true sends the user to
    /// the standard pages; false (the default) stops with an error, so a tenant that relies on
    /// its journeys can't have them bypassed. A policy that is named never falls back.
    /// </summary>
    public bool JourneyFallbackToStandard { get; set; }

    /// <summary>
    /// Whether local username/password login is enabled for this tenant.
    /// If false, users must use external identity providers.
    /// Can be overridden per-client.
    /// </summary>
    public bool EnableLocalLogin { get; set; } = true;

    /// <summary>
    /// Whether passkey / WebAuthn login is offered on the login page for
    /// this tenant. When false, the passkey provider is filtered out of
    /// the available authentication methods even if Fido2 is configured at
    /// the platform level — lets a tenant disable passkeys for compliance
    /// or UX reasons without affecting other tenants.
    /// </summary>
    public bool EnablePasskey { get; set; } = true;

    /// <summary>
    /// Scheme of the IdentityProvider that should be auto-selected when a
    /// "Login with SSO" flow targets this tenant. Returned by the public
    /// /api/tenant/resolve endpoint and used by host apps as the
    /// <c>domain_hint</c> on the OIDC authorize call so the picker is skipped.
    /// Null = no default; the picker is shown.
    /// </summary>
    public string? DefaultIdpScheme { get; set; }

    /// <summary>
    /// Optional journey policy to run when "Login with SSO" targets this
    /// tenant, by the policy's slug (not its ID): the value is handed to clients
    /// as-is. Returned by /api/tenant/resolve so host
    /// apps can forward it as the <c>policy</c> query parameter on the OIDC
    /// authorize call, which ResolvePolicyAsync picks up via its
    /// ExplicitPolicyId branch. Null = fall through to the platform's
    /// standard SignIn policy.
    /// </summary>
    public string? DefaultSignInPolicyId { get; set; }

    /// <summary>
    /// UI mode this tenant prefers for SSO-driven sign-ins (the flow kicked
    /// off from a "Login with SSO" landing). Forwarded as <c>ui_mode</c> on
    /// the OIDC authorize call so the AuthenticationCoordinator can route
    /// past the journey for SSO when the tenant wants a lean redirect-only
    /// experience. Other journeys (self-service signup, password reset)
    /// are unaffected — they don't go through the SSO landing. Null = follow
    /// the tenant/client's general UseJourneyFlow setting.
    /// Allowed values: "Journey" or "Standalone".
    /// </summary>
    public string? DefaultSsoUiMode { get; set; }

    /// <summary>
    /// Whether users of this tenant may start sign-in from the platform login
    /// page by entering the tenant identifier ("Sign in with your organisation").
    /// When true, the platform hands the browser to this tenant's own login
    /// (its identity providers, journey and MFA) and receives the signed-in user
    /// back, so a client registered only against the platform issuer can still
    /// reach the tenant's identity providers. When false, the platform answers an
    /// attempt with the same generic error it gives for an unknown identifier.
    /// </summary>
    public bool AllowPlatformOrgSignIn { get; set; } = true;

    /// <summary>
    /// Feature entitlements for this tenant.
    /// Controls which ERP modules this tenant can access (within org-level limits).
    /// </summary>
    public ICollection<TenantFeature> Features { get; set; } = new List<TenantFeature>();
}

/// <summary>
/// Platform-licensing branding flag for a tenant. Visual branding (logo,
/// favicon, colours, CSS, support email, footer text, terms / privacy URLs)
/// lives in the <c>BrandingSettings</c> tenant-config section instead, with
/// reads/writes through <see cref="Oluso.Core.Domain.Interfaces.ITenantSettingsProvider"/>
/// and <c>/api/admin/settings/branding</c> respectively.
///
/// This entity exists only to hold <see cref="HideEmbedBranding"/>, a
/// SuperAdmin-only toggle that controls whether tenant logins can hide the
/// "Powered by Oluso" mark on embedded auth modals. It has its own write
/// path with a separate access policy from tenant-admin branding edits,
/// which is why it stays on the entity instead of moving into the config
/// section.
/// </summary>
public class TenantBranding
{
    public int Id { get; set; }
    public string TenantId { get; set; } = default!;
    public Tenant Tenant { get; set; } = default!;

    /// <summary>
    /// When true, hides the "Powered by Oluso" branding on embedded auth modals.
    /// SuperAdmin-only — platform licensing concern, not a tenant-admin choice.
    /// </summary>
    public bool HideEmbedBranding { get; set; }
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
    /// What the breached-password check does when the breach database can't be reached
    /// (timeout, outage). False (default) accepts the password so an outage doesn't block
    /// sign-ups and password changes; true rejects it and asks the user to try again.
    /// Only applies when <see cref="CheckBreachedPasswords"/> is on.
    /// </summary>
    public bool RejectWhenBreachCheckUnavailable { get; set; } = false;

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
    /// Whether to enforce OAuth 2.1 security profile.
    /// When true: implicit grant is rejected, PKCE is required, plain PKCE is disallowed,
    /// and Resource Owner Password Credentials (ROPC) grant is rejected.
    /// </summary>
    public bool EnforceOAuth21 { get; set; }

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

    // ========================================
    // Prefix scopes
    // ========================================

    /// <summary>
    /// The longest suffix a prefix scope accepts when it doesn't set its own <c>suffix_max_length</c>
    /// (<see cref="Oluso.Core.Services.ApiScopePrefixes"/>). Null means
    /// <see cref="Oluso.Core.Services.ApiScopePrefixes.DefaultSuffixMaxLength"/>.
    /// </summary>
    public int? PrefixScopeSuffixMaxLength { get; set; }

    /// <summary>
    /// Scopes that earn a refresh token without <c>offline_access</c> (JSON array). Null means the Matrix client API
    /// scopes (<see cref="Oluso.Core.Domain.Interfaces.TenantProtocolSettings.DefaultRefreshImplyingScopes"/>);
    /// <c>[]</c> turns it off.
    /// </summary>
    public string? RefreshImplyingScopesJson { get; set; }

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
    /// Scopes that dynamically registered clients are allowed to request (JSON array).
    /// Values must exist in tenant's configured scopes.
    /// If empty/null, defaults to openid, profile, email.
    /// </summary>
    public string? DynamicRegistrationAllowedScopesJson { get; set; }

    /// <summary>
    /// Grant types that dynamically registered clients are allowed to use (JSON array).
    /// Values must be a subset of tenant's AllowedGrantTypes.
    /// If empty/null, defaults to authorization_code and refresh_token.
    /// </summary>
    public string? DynamicRegistrationAllowedGrantTypesJson { get; set; }

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
    /// </summary>
    public string? DynamicRegistrationRequiredScope { get; set; }

    /// <summary>
    /// Required claim name for initial access tokens when protected registration is enabled.
    /// If null/empty, no claim check is performed.
    /// </summary>
    public string? DynamicRegistrationRequiredClaim { get; set; }

    /// <summary>
    /// Required claim value for initial access tokens when protected registration is enabled.
    /// If null/empty but DynamicRegistrationRequiredClaim is set, only claim presence is checked.
    /// </summary>
    public string? DynamicRegistrationRequiredClaimValue { get; set; }

    // Note: Package-specific settings (e.g., SAML IdP, SCIM, etc.) are stored in TenantConfigurationSections.
    // Each package uses its own section key (e.g., "SamlIdp", "Scim") via ITenantConfigurationService.

    public DateTime Created { get; set; } = DateTime.UtcNow;
    public DateTime? Updated { get; set; }
}
