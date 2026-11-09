namespace Oluso.Core.Services;

/// <summary>
/// Store for external identity provider configurations.
/// Implementations should be tenant-aware and return only providers configured for the current tenant.
/// </summary>
public interface IExternalProviderConfigStore
{
    /// <summary>
    /// Gets all enabled external providers visible to the current tenant.
    /// When <paramref name="includeTenantOverrides"/> is false, only providers
    /// with a null TenantId (global) are returned.
    /// </summary>
    Task<IReadOnlyList<ExternalProviderDefinition>> GetEnabledProvidersAsync(
        bool includeTenantOverrides = true,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Gets a specific provider by scheme name. When
    /// <paramref name="includeTenantOverrides"/> is true (the default),
    /// tenant-specific rows take precedence over global rows for the same
    /// scheme; when false, only the global row is returned.
    /// </summary>
    Task<ExternalProviderDefinition?> GetBySchemeAsync(
        string scheme,
        bool includeTenantOverrides = true,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Gets a provider by its unique ID
    /// </summary>
    Task<ExternalProviderDefinition?> GetByIdAsync(string id, CancellationToken cancellationToken = default);
}

/// <summary>
/// Complete definition of an external identity provider including credentials.
/// This is used for dynamic provider configuration from database.
/// </summary>
public class ExternalProviderDefinition
{
    /// <summary>
    /// Unique identifier for this provider configuration
    /// </summary>
    public required string Id { get; init; }

    /// <summary>
    /// Tenant ID this provider belongs to (null for global providers)
    /// </summary>
    public string? TenantId { get; init; }

    /// <summary>
    /// Authentication scheme name (e.g., "Google", "TenantA-Google")
    /// </summary>
    public required string Scheme { get; init; }

    /// <summary>
    /// Display name shown to users
    /// </summary>
    public string? DisplayName { get; init; }

    /// <summary>
    /// Icon URL for UI display
    /// </summary>
    public string? IconUrl { get; init; }

    /// <summary>
    /// Provider type: "Google", "Microsoft", "Facebook", "GitHub", "LinkedIn", "Apple", "Oidc", "OAuth"
    /// </summary>
    public required string ProviderType { get; init; }

    /// <summary>
    /// Whether this provider is enabled
    /// </summary>
    public bool Enabled { get; init; } = true;

    /// <summary>
    /// Whether this is a direct login provider (LDAP, RADIUS) that has its own login page
    /// rather than using OAuth/SAML redirect flow.
    /// </summary>
    public bool IsDirectLogin { get; init; } = false;

    /// <summary>
    /// For direct login providers, the path to the login page (e.g., "/account/ldap-login")
    /// </summary>
    public string? DirectLoginPath { get; init; }

    /// <summary>
    /// OAuth/OIDC Client ID
    /// </summary>
    public required string ClientId { get; init; }

    /// <summary>
    /// OAuth/OIDC Client Secret (encrypted in storage)
    /// </summary>
    public required string ClientSecret { get; init; }

    /// <summary>
    /// Authorization endpoint (for generic OAuth/OIDC)
    /// </summary>
    public string? AuthorizationEndpoint { get; init; }

    /// <summary>
    /// Token endpoint (for generic OAuth/OIDC)
    /// </summary>
    public string? TokenEndpoint { get; init; }

    /// <summary>
    /// UserInfo endpoint (for generic OAuth/OIDC)
    /// </summary>
    public string? UserInfoEndpoint { get; init; }

    /// <summary>
    /// OIDC issuer / authority root (e.g. "https://tenant.auth0.com").
    /// The OIDC handler appends "/.well-known/openid-configuration" automatically.
    /// </summary>
    public string? Authority { get; init; }

    /// <summary>
    /// Full OIDC discovery/metadata URL (e.g.
    /// "https://tenant.auth0.com/.well-known/openid-configuration"). Set this only
    /// when the IdP exposes the discovery doc at a non-standard path; for normal
    /// providers, set <see cref="Authority"/> instead.
    /// </summary>
    public string? MetadataAddress { get; init; }

    /// <summary>
    /// Scopes to request
    /// </summary>
    public IReadOnlyList<string> Scopes { get; init; } = Array.Empty<string>();

    /// <summary>
    /// Apple-specific: Team ID
    /// </summary>
    public string? AppleTeamId { get; init; }

    /// <summary>
    /// Apple-specific: Key ID
    /// </summary>
    public string? AppleKeyId { get; init; }

    /// <summary>
    /// Apple-specific: Private key (encrypted in storage)
    /// </summary>
    public string? ApplePrivateKey { get; init; }

    #region Proxy Mode Settings

    /// <summary>
    /// Enable proxy/pass-through mode - user is authenticated via external IdP but not stored locally
    /// </summary>
    public bool ProxyMode { get; init; } = false;

    /// <summary>
    /// Whether to store user details locally when authenticating
    /// </summary>
    public bool StoreUserLocally { get; init; } = true;

    /// <summary>
    /// Cache external access/refresh tokens for subsequent userinfo calls
    /// </summary>
    public bool CacheExternalTokens { get; init; } = false;

    /// <summary>
    /// Token cache duration in seconds
    /// </summary>
    public int TokenCacheDurationSeconds { get; init; } = 3600;

    /// <summary>
    /// Claims to include in tokens when in proxy mode
    /// </summary>
    public IReadOnlyList<string> ProxyIncludeClaims { get; init; } = Array.Empty<string>();

    /// <summary>
    /// Claims to exclude from tokens when in proxy mode
    /// </summary>
    public IReadOnlyList<string> ProxyExcludeClaims { get; init; } = Array.Empty<string>();

    /// <summary>
    /// Whether to include the external access token in the issued token
    /// </summary>
    public bool IncludeExternalAccessToken { get; init; } = false;

    /// <summary>
    /// Whether to include the external ID token in the issued token
    /// </summary>
    public bool IncludeExternalIdToken { get; init; } = false;

    /// <summary>
    /// Whether to auto-provision users on first login
    /// </summary>
    public bool AutoProvisionUsers { get; init; } = true;

    #endregion

    #region Access Control & Role Assignment

    /// <summary>
    /// Oluso role names assigned to users provisioned via this IdP. Applied
    /// on first provision only — additive, never removed on subsequent
    /// logins so admin-granted local roles aren't stripped if the IdP's
    /// defaultRoles list is later edited.
    /// </summary>
    public IReadOnlyList<string> DefaultRoles { get; init; } = Array.Empty<string>();

    /// <summary>
    /// Access-control gate evaluated against the external principal before
    /// provisioning or sign-in completes. <see cref="ExternalAccessControl.Mode"/>
    /// = "open" (default) imposes no gate.
    /// </summary>
    public ExternalAccessControl? AccessControl { get; init; }

    #endregion

    /// <summary>
    /// Additional custom properties
    /// </summary>
    public IDictionary<string, string>? Properties { get; init; }
}

/// <summary>
/// Pre-sign-in gate applied to the external principal returned by the IdP.
/// Mirrors the structure persisted in <c>IdentityProvider.Properties</c>
/// JSON so the admin UI, store, and runtime gate share one shape.
/// </summary>
public class ExternalAccessControl
{
    /// <summary>
    /// "open" (default — no gate), "emailAllowlist", or "claimGate".
    /// </summary>
    public string Mode { get; init; } = "open";

    /// <summary>
    /// External email addresses allowed when <see cref="Mode"/> = "emailAllowlist".
    /// Comparison is case-insensitive on the entire address.
    /// </summary>
    public IReadOnlyList<string> AllowedEmails { get; init; } = Array.Empty<string>();

    /// <summary>
    /// Claim-based gate applied when <see cref="Mode"/> = "claimGate".
    /// User passes if the claim at <see cref="ClaimGate.ClaimPath"/> contains
    /// any value in <see cref="ClaimGate.AnyOf"/>.
    /// </summary>
    public ClaimGate? ClaimGate { get; init; }
}

/// <summary>
/// "User must have <c>claimPath</c> claim with at least one value in <c>anyOf</c>."
/// Matches a single string claim or any element of a multi-valued claim.
/// </summary>
public class ClaimGate
{
    /// <summary>
    /// Claim type to match. Accepts both short JWT names ("groups", "roles")
    /// and long ClaimTypes URIs — the gate checks both.
    /// </summary>
    public string ClaimPath { get; init; } = "";

    /// <summary>
    /// Acceptable values. User passes if the claim contains any of these.
    /// </summary>
    public IReadOnlyList<string> AnyOf { get; init; } = Array.Empty<string>();
}
