namespace Oluso.Core.Services;

/// <summary>
/// Store for external provider configurations from database.
/// This is used for tenant-specific provider settings.
/// </summary>
public interface IExternalProviderStore
{
    Task<IReadOnlyList<ExternalProviderStoreInfo>> GetEnabledProvidersAsync(CancellationToken cancellationToken = default);
    Task<ExternalProviderStoreInfo?> GetBySchemeAsync(string scheme, CancellationToken cancellationToken = default);
}

/// <summary>
/// Provider information from database store
/// </summary>
public class ExternalProviderStoreInfo
{
    public required string Scheme { get; init; }
    public string? DisplayName { get; init; }
    public string? IconUrl { get; init; }
    public string? ProviderType { get; init; }
    public bool ProxyMode { get; init; }
    public bool StoreUserLocally { get; init; } = true;
    public bool CacheExternalTokens { get; init; }
    public int TokenCacheDurationSeconds { get; init; } = 3600;
    public IReadOnlyList<string>? ProxyIncludeClaims { get; init; }
    public IReadOnlyList<string>? ProxyExcludeClaims { get; init; }
    public bool IncludeExternalAccessToken { get; init; }
    public bool IncludeExternalIdToken { get; init; }
    public bool AutoProvisionUsers { get; init; } = true;
}
