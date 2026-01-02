namespace Oluso.Core.Services;

/// <summary>
/// Store for external identity provider configurations.
/// Implementations should be tenant-aware and return only providers configured for the current tenant.
/// </summary>
public interface IExternalProviderConfigStore
{
    /// <summary>
    /// Gets all enabled external providers for the current tenant
    /// </summary>
    Task<IReadOnlyList<ExternalProviderDefinition>> GetEnabledProvidersAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// Gets a specific provider by scheme name for the current tenant
    /// </summary>
    Task<ExternalProviderDefinition?> GetBySchemeAsync(string scheme, CancellationToken cancellationToken = default);

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
    /// OIDC discovery/metadata endpoint
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

    /// <summary>
    /// Additional custom properties
    /// </summary>
    public IDictionary<string, string>? Properties { get; init; }
}
