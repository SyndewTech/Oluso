namespace Oluso.Core.Services;

/// <summary>
/// Service for external authentication operations (OAuth, OIDC, social login).
/// Implement this interface to customize external authentication behavior.
/// </summary>
/// <remarks>
/// The default implementation uses ASP.NET Core Identity with Entity Framework.
/// All operations are tenant-aware when multi-tenancy is enabled.
/// </remarks>
public interface IExternalAuthService
{
    /// <summary>
    /// Gets available external authentication providers for the current tenant
    /// </summary>
    Task<IReadOnlyList<ExternalProviderInfo>> GetAvailableProvidersAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// Gets available external authentication providers filtered by client IdP restrictions.
    /// If the client has no restrictions, returns all available providers.
    /// </summary>
    /// <param name="clientId">The OAuth client ID to filter providers for</param>
    /// <param name="cancellationToken">Cancellation token</param>
    /// <returns>List of providers allowed for this client</returns>
    Task<IReadOnlyList<ExternalProviderInfo>> GetAvailableProvidersAsync(string clientId, CancellationToken cancellationToken = default);

    /// <summary>
    /// Gets configuration for a specific provider
    /// </summary>
    Task<ExternalProviderConfig?> GetProviderConfigAsync(string provider, CancellationToken cancellationToken = default);

    /// <summary>
    /// Initiates external authentication challenge
    /// </summary>
    Task<ExternalChallengeResult> ChallengeAsync(string provider, string returnUrl, CancellationToken cancellationToken = default);

    /// <summary>
    /// Gets the result of external authentication (from callback)
    /// </summary>
    Task<ExternalLoginResult?> GetExternalLoginResultAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// Signs out from external authentication
    /// </summary>
    Task SignOutExternalAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// Gets all external logins linked to a user
    /// </summary>
    Task<IList<ExternalLoginInfo>> GetUserLoginsAsync(string userId, CancellationToken cancellationToken = default);

    /// <summary>
    /// Finds a user ID by external login (provider and key)
    /// </summary>
    Task<string?> FindUserByLoginAsync(string provider, string providerKey, CancellationToken cancellationToken = default);

    /// <summary>
    /// Links an external login to a user
    /// </summary>
    Task<ExternalLoginOperationResult> LinkLoginAsync(
        string userId,
        string provider,
        string providerKey,
        string? displayName,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Unlinks an external login from a user
    /// </summary>
    Task<ExternalLoginOperationResult> UnlinkLoginAsync(
        string userId,
        string provider,
        string providerKey,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Caches external tokens for later use (e.g., userinfo proxy)
    /// </summary>
    Task CacheExternalTokensAsync(
        string sessionKey,
        ExternalTokenData tokens,
        TimeSpan? expiry = null,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Gets cached external tokens
    /// </summary>
    Task<ExternalTokenData?> GetCachedTokensAsync(string sessionKey, CancellationToken cancellationToken = default);
}

/// <summary>
/// Information about an external authentication provider
/// </summary>
public class ExternalProviderInfo
{
    public required string Scheme { get; init; }
    public string? DisplayName { get; init; }
    public string? IconUrl { get; init; }
    public string? ProviderType { get; init; }
    public bool Enabled { get; init; } = true;
}

/// <summary>
/// Configuration for an external provider including proxy mode settings
/// </summary>
public class ExternalProviderConfig
{
    public required string Scheme { get; init; }
    public string? DisplayName { get; init; }
    public string? ProviderType { get; init; }

    #region Proxy Mode Settings

    /// <summary>
    /// Enable proxy/pass-through mode - user is authenticated via external IdP but not stored locally.
    /// In this mode, the server acts as a federation broker/proxy.
    /// </summary>
    public bool ProxyMode { get; init; } = false;

    /// <summary>
    /// Whether to store user details locally when authenticating.
    /// If false, no local user record is created.
    /// </summary>
    public bool StoreUserLocally { get; init; } = true;

    /// <summary>
    /// Cache external access/refresh tokens for subsequent userinfo calls.
    /// </summary>
    public bool CacheExternalTokens { get; init; } = false;

    /// <summary>
    /// Token cache duration in seconds (default: 1 hour)
    /// </summary>
    public int TokenCacheDurationSeconds { get; init; } = 3600;

    /// <summary>
    /// Claims to include in tokens when in proxy mode.
    /// If empty, all external claims are passed through.
    /// </summary>
    public IReadOnlyList<string> ProxyIncludeClaims { get; init; } = Array.Empty<string>();

    /// <summary>
    /// Claims to exclude from tokens when in proxy mode.
    /// </summary>
    public IReadOnlyList<string> ProxyExcludeClaims { get; init; } = Array.Empty<string>();

    /// <summary>
    /// Whether to include the external access token in the issued token.
    /// </summary>
    public bool IncludeExternalAccessToken { get; init; } = false;

    /// <summary>
    /// Whether to include the external ID token in the issued token.
    /// </summary>
    public bool IncludeExternalIdToken { get; init; } = false;

    /// <summary>
    /// Allow proxying userinfo requests to the external IdP.
    /// When enabled, a /userinfo-proxy/{scheme} endpoint becomes available.
    /// </summary>
    public bool EnableUserInfoProxy { get; init; } = false;

    /// <summary>
    /// Whether to auto-provision users on first login
    /// </summary>
    public bool AutoProvisionUsers { get; init; } = true;

    #endregion
}

/// <summary>
/// Result of initiating external authentication
/// </summary>
public class ExternalChallengeResult
{
    public bool Succeeded { get; init; }
    public string? RedirectUrl { get; init; }
    public string? Error { get; init; }

    public static ExternalChallengeResult Success(string redirectUrl) =>
        new() { Succeeded = true, RedirectUrl = redirectUrl };

    public static ExternalChallengeResult Failed(string error) =>
        new() { Succeeded = false, Error = error };
}

/// <summary>
/// Result of external authentication callback
/// </summary>
public class ExternalLoginResult
{
    public bool Succeeded { get; init; }
    public string Provider { get; init; } = null!;
    public string ProviderKey { get; init; } = null!;
    public string? Email { get; init; }
    public string? Name { get; init; }
    public string? FirstName { get; init; }
    public string? LastName { get; init; }
    public string? ProfilePictureUrl { get; init; }
    public string? Error { get; init; }

    /// <summary>
    /// All claims from the external provider
    /// </summary>
    public IDictionary<string, string>? Claims { get; init; }

    /// <summary>
    /// External access token (if available)
    /// </summary>
    public string? AccessToken { get; init; }

    /// <summary>
    /// External ID token (if available, for OIDC)
    /// </summary>
    public string? IdToken { get; init; }

    /// <summary>
    /// External refresh token (if available)
    /// </summary>
    public string? RefreshToken { get; init; }

    /// <summary>
    /// Token expiration time
    /// </summary>
    public DateTime? TokenExpiresAt { get; init; }

    /// <summary>
    /// Provider configuration (for proxy mode handling)
    /// </summary>
    public ExternalProviderConfig? ProviderConfig { get; init; }

    public static ExternalLoginResult Success(
        string provider,
        string providerKey,
        string? email = null,
        string? name = null) =>
        new()
        {
            Succeeded = true,
            Provider = provider,
            ProviderKey = providerKey,
            Email = email,
            Name = name
        };

    public static ExternalLoginResult Failed(string error) =>
        new() { Succeeded = false, Error = error, Provider = "", ProviderKey = "" };
}

/// <summary>
/// Information about a linked external login
/// </summary>
public class ExternalLoginInfo
{
    public string Provider { get; init; } = null!;
    public string ProviderKey { get; init; } = null!;
    public string? DisplayName { get; init; }
}

/// <summary>
/// Result of an external login operation (link/unlink)
/// </summary>
public class ExternalLoginOperationResult
{
    public bool Succeeded { get; init; }
    public string? Error { get; init; }

    public static ExternalLoginOperationResult Success() =>
        new() { Succeeded = true };

    public static ExternalLoginOperationResult Failed(string error) =>
        new() { Succeeded = false, Error = error };
}

/// <summary>
/// Cached external token data for proxy mode
/// </summary>
public class ExternalTokenData
{
    public required string Provider { get; init; }
    public required string ExternalSubject { get; init; }
    public string? AccessToken { get; init; }
    public string? RefreshToken { get; init; }
    public string? IdToken { get; init; }
    public string TokenType { get; init; } = "Bearer";
    public DateTime? ExpiresAt { get; init; }
}
