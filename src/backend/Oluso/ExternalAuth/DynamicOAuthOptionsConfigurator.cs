using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.OAuth;
using Microsoft.AspNetCore.Authentication.OpenIdConnect;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Oluso.Core.Domain.Interfaces;
using Oluso.Core.Services;

namespace Oluso.ExternalAuth;

/// <summary>
/// Configures OAuth options dynamically based on tenant-specific provider configurations.
/// This is called when authentication handlers need their options.
/// Uses IHttpContextAccessor to resolve scoped services at runtime since this is a singleton.
/// </summary>
public class DynamicOAuthPostConfigureOptions :
    IPostConfigureOptions<Microsoft.AspNetCore.Authentication.Google.GoogleOptions>,
    IPostConfigureOptions<Microsoft.AspNetCore.Authentication.MicrosoftAccount.MicrosoftAccountOptions>,
    IPostConfigureOptions<Microsoft.AspNetCore.Authentication.Facebook.FacebookOptions>,
    IPostConfigureOptions<Microsoft.AspNetCore.Authentication.Twitter.TwitterOptions>,
    IPostConfigureOptions<AspNet.Security.OAuth.GitHub.GitHubAuthenticationOptions>,
    IPostConfigureOptions<AspNet.Security.OAuth.LinkedIn.LinkedInAuthenticationOptions>,
    IPostConfigureOptions<AspNet.Security.OAuth.Apple.AppleAuthenticationOptions>,
    IPostConfigureOptions<OpenIdConnectOptions>,
    IPostConfigureOptions<OAuthOptions>
{
    private readonly IHttpContextAccessor _httpContextAccessor;
    private readonly DynamicOptionsCache _optionsCache;
    private readonly ILogger<DynamicOAuthPostConfigureOptions>? _logger;

    public DynamicOAuthPostConfigureOptions(
        IHttpContextAccessor httpContextAccessor,
        DynamicOptionsCache optionsCache,
        ILogger<DynamicOAuthPostConfigureOptions>? logger = null)
    {
        _httpContextAccessor = httpContextAccessor;
        _optionsCache = optionsCache;
        _logger = logger;
    }

    private ITenantContext? GetTenantContext() =>
        _httpContextAccessor.HttpContext?.RequestServices.GetService<ITenantContext>();

    private IExternalProviderConfigStore? GetProviderStore() =>
        _httpContextAccessor.HttpContext?.RequestServices.GetService<IExternalProviderConfigStore>();

    public void PostConfigure(string? name, Microsoft.AspNetCore.Authentication.Google.GoogleOptions options)
    {
        if (string.IsNullOrEmpty(name)) return;
        ConfigureOAuthOptionsAsync(name, options, "google").GetAwaiter().GetResult();
    }

    public void PostConfigure(string? name, Microsoft.AspNetCore.Authentication.MicrosoftAccount.MicrosoftAccountOptions options)
    {
        if (string.IsNullOrEmpty(name)) return;
        ConfigureOAuthOptionsAsync(name, options, "microsoft").GetAwaiter().GetResult();
    }

    public void PostConfigure(string? name, Microsoft.AspNetCore.Authentication.Facebook.FacebookOptions options)
    {
        if (string.IsNullOrEmpty(name)) return;
        ConfigureFacebookOptionsAsync(name, options).GetAwaiter().GetResult();
    }

    public void PostConfigure(string? name, Microsoft.AspNetCore.Authentication.Twitter.TwitterOptions options)
    {
        if (string.IsNullOrEmpty(name)) return;
        ConfigureTwitterOptionsAsync(name, options).GetAwaiter().GetResult();
    }

    public void PostConfigure(string? name, AspNet.Security.OAuth.GitHub.GitHubAuthenticationOptions options)
    {
        if (string.IsNullOrEmpty(name)) return;
        ConfigureOAuthOptionsAsync(name, options, "github").GetAwaiter().GetResult();
    }

    public void PostConfigure(string? name, AspNet.Security.OAuth.LinkedIn.LinkedInAuthenticationOptions options)
    {
        if (string.IsNullOrEmpty(name)) return;
        ConfigureOAuthOptionsAsync(name, options, "linkedin").GetAwaiter().GetResult();
    }

    public void PostConfigure(string? name, AspNet.Security.OAuth.Apple.AppleAuthenticationOptions options)
    {
        if (string.IsNullOrEmpty(name)) return;
        ConfigureAppleOptionsAsync(name, options).GetAwaiter().GetResult();
    }

    public void PostConfigure(string? name, OpenIdConnectOptions options)
    {
        if (string.IsNullOrEmpty(name)) return;
        ConfigureOidcOptionsAsync(name, options).GetAwaiter().GetResult();
    }

    public void PostConfigure(string? name, OAuthOptions options)
    {
        if (string.IsNullOrEmpty(name)) return;
        ConfigureGenericOAuthOptionsAsync(name, options).GetAwaiter().GetResult();
    }

    private const string PlaceholderValue = "__dynamic_placeholder__";

    private async Task ConfigureOAuthOptionsAsync<TOptions>(string name, TOptions options, string expectedType)
        where TOptions : OAuthOptions
    {
        var provider = await GetProviderAsync(name);
        if (provider == null)
        {
            _logger?.LogDebug("No provider configuration found for scheme {Scheme}", name);
            return;
        }

        if (!provider.ProviderType.Equals(expectedType, StringComparison.OrdinalIgnoreCase))
        {
            _logger?.LogDebug(
                "Provider type mismatch for scheme {Scheme}: expected {ExpectedType}, got {ActualType}",
                name, expectedType, provider.ProviderType);
            return;
        }

        _logger?.LogDebug(
            "Configuring OAuth options for scheme {Scheme} with provider type {ProviderType}",
            name, provider.ProviderType);

        // Apply database config if current value is empty or a placeholder
        // Database configuration always takes precedence over placeholder values
        if (IsPlaceholderOrEmpty(options.ClientId) && !string.IsNullOrEmpty(provider.ClientId))
        {
            options.ClientId = provider.ClientId;
            _logger?.LogDebug("Set ClientId for scheme {Scheme}", name);
        }

        if (IsPlaceholderOrEmpty(options.ClientSecret) && !string.IsNullOrEmpty(provider.ClientSecret))
        {
            options.ClientSecret = provider.ClientSecret;
        }

        options.SaveTokens = provider.CacheExternalTokens || provider.IncludeExternalAccessToken;

        if (provider.Scopes.Any())
        {
            options.Scope.Clear();
            foreach (var scope in provider.Scopes)
            {
                options.Scope.Add(scope);
            }
        }
    }

    private static bool IsPlaceholderOrEmpty(string? value) =>
        string.IsNullOrEmpty(value) || value == PlaceholderValue;

    private async Task ConfigureFacebookOptionsAsync(string name, Microsoft.AspNetCore.Authentication.Facebook.FacebookOptions options)
    {
        var provider = await GetProviderAsync(name);
        if (provider == null) return;

        if (!provider.ProviderType.Equals("facebook", StringComparison.OrdinalIgnoreCase)) return;

        if (IsPlaceholderOrEmpty(options.AppId) && !string.IsNullOrEmpty(provider.ClientId))
        {
            options.AppId = provider.ClientId;
        }

        if (IsPlaceholderOrEmpty(options.AppSecret) && !string.IsNullOrEmpty(provider.ClientSecret))
        {
            options.AppSecret = provider.ClientSecret;
        }

        options.SaveTokens = provider.CacheExternalTokens || provider.IncludeExternalAccessToken;

        if (provider.Scopes.Any())
        {
            options.Scope.Clear();
            foreach (var scope in provider.Scopes)
            {
                options.Scope.Add(scope);
            }
        }
    }

    private async Task ConfigureTwitterOptionsAsync(string name, Microsoft.AspNetCore.Authentication.Twitter.TwitterOptions options)
    {
        var provider = await GetProviderAsync(name);
        if (provider == null) return;

        if (!provider.ProviderType.Equals("twitter", StringComparison.OrdinalIgnoreCase)) return;

        if (IsPlaceholderOrEmpty(options.ConsumerKey) && !string.IsNullOrEmpty(provider.ClientId))
        {
            options.ConsumerKey = provider.ClientId;
        }

        if (IsPlaceholderOrEmpty(options.ConsumerSecret) && !string.IsNullOrEmpty(provider.ClientSecret))
        {
            options.ConsumerSecret = provider.ClientSecret;
        }

        options.SaveTokens = provider.CacheExternalTokens || provider.IncludeExternalAccessToken;
        options.RetrieveUserDetails = true;
    }

    private async Task ConfigureAppleOptionsAsync(string name, AspNet.Security.OAuth.Apple.AppleAuthenticationOptions options)
    {
        var provider = await GetProviderAsync(name);
        if (provider == null) return;

        if (!provider.ProviderType.Equals("apple", StringComparison.OrdinalIgnoreCase)) return;

        if (IsPlaceholderOrEmpty(options.ClientId) && !string.IsNullOrEmpty(provider.ClientId))
        {
            options.ClientId = provider.ClientId;
        }

        if (IsPlaceholderOrEmpty(options.TeamId) && !string.IsNullOrEmpty(provider.AppleTeamId))
        {
            options.TeamId = provider.AppleTeamId;
        }

        if (IsPlaceholderOrEmpty(options.KeyId) && !string.IsNullOrEmpty(provider.AppleKeyId))
        {
            options.KeyId = provider.AppleKeyId;
        }

        if (!string.IsNullOrEmpty(provider.ApplePrivateKey))
        {
            options.GenerateClientSecret = true;
            var privateKey = provider.ApplePrivateKey;
            options.PrivateKey = (keyId, ct) => Task.FromResult(privateKey.AsMemory());
        }

        options.SaveTokens = provider.CacheExternalTokens || provider.IncludeExternalAccessToken;

        if (provider.Scopes.Any())
        {
            options.Scope.Clear();
            foreach (var scope in provider.Scopes)
            {
                options.Scope.Add(scope);
            }
        }
    }

    private async Task ConfigureOidcOptionsAsync(string name, OpenIdConnectOptions options)
    {
        var provider = await GetProviderAsync(name);
        if (provider == null) return;

        if (!provider.ProviderType.Equals("oidc", StringComparison.OrdinalIgnoreCase)) return;

        if (IsPlaceholderOrEmpty(options.ClientId) && !string.IsNullOrEmpty(provider.ClientId))
        {
            options.ClientId = provider.ClientId;
        }

        if (IsPlaceholderOrEmpty(options.ClientSecret) && !string.IsNullOrEmpty(provider.ClientSecret))
        {
            options.ClientSecret = provider.ClientSecret;
        }

        if (string.IsNullOrEmpty(options.MetadataAddress) && !string.IsNullOrEmpty(provider.MetadataAddress))
        {
            options.MetadataAddress = provider.MetadataAddress;
        }

        if (string.IsNullOrEmpty(options.Authority) && !string.IsNullOrEmpty(provider.MetadataAddress))
        {
            // Extract authority from metadata address if possible
            var uri = new Uri(provider.MetadataAddress);
            options.Authority = $"{uri.Scheme}://{uri.Host}";
        }

        options.SaveTokens = provider.CacheExternalTokens || provider.IncludeExternalAccessToken;

        if (provider.Scopes.Any())
        {
            options.Scope.Clear();
            foreach (var scope in provider.Scopes)
            {
                options.Scope.Add(scope);
            }
        }
    }

    private async Task ConfigureGenericOAuthOptionsAsync(string name, OAuthOptions options)
    {
        var provider = await GetProviderAsync(name);
        if (provider == null) return;

        if (!provider.ProviderType.Equals("oauth", StringComparison.OrdinalIgnoreCase)) return;

        if (IsPlaceholderOrEmpty(options.ClientId) && !string.IsNullOrEmpty(provider.ClientId))
        {
            options.ClientId = provider.ClientId;
        }

        if (IsPlaceholderOrEmpty(options.ClientSecret) && !string.IsNullOrEmpty(provider.ClientSecret))
        {
            options.ClientSecret = provider.ClientSecret;
        }

        if (options.AuthorizationEndpoint == null && !string.IsNullOrEmpty(provider.AuthorizationEndpoint))
        {
            options.AuthorizationEndpoint = provider.AuthorizationEndpoint;
        }

        if (options.TokenEndpoint == null && !string.IsNullOrEmpty(provider.TokenEndpoint))
        {
            options.TokenEndpoint = provider.TokenEndpoint;
        }

        if (options.UserInformationEndpoint == null && !string.IsNullOrEmpty(provider.UserInfoEndpoint))
        {
            options.UserInformationEndpoint = provider.UserInfoEndpoint;
        }

        options.SaveTokens = provider.CacheExternalTokens || provider.IncludeExternalAccessToken;

        if (provider.Scopes.Any())
        {
            options.Scope.Clear();
            foreach (var scope in provider.Scopes)
            {
                options.Scope.Add(scope);
            }
        }
    }

    private async Task<ExternalProviderDefinition?> GetProviderAsync(string scheme)
    {
        var providerStore = GetProviderStore();
        if (providerStore == null)
        {
            _logger?.LogDebug(
                "No provider store available (HttpContext may be null). Scheme: {Scheme}",
                scheme);
            return null;
        }

        var tenantContext = GetTenantContext();
        var tenantId = tenantContext?.TenantId ?? "global";
        var cacheKey = $"{tenantId}:{scheme}";

        // Check cache first
        if (_optionsCache.TryGetProvider(cacheKey, out var cached))
        {
            _logger?.LogDebug("Found cached provider for {CacheKey}", cacheKey);
            return cached;
        }

        // Load from database
        _logger?.LogDebug("Loading provider from database for scheme {Scheme}, tenant {TenantId}", scheme, tenantId);
        var provider = await providerStore.GetBySchemeAsync(scheme);

        if (provider != null)
        {
            _logger?.LogDebug(
                "Found provider in database: Scheme={Scheme}, ProviderType={ProviderType}, ClientId={HasClientId}",
                provider.Scheme, provider.ProviderType, !string.IsNullOrEmpty(provider.ClientId));
        }
        else
        {
            _logger?.LogDebug("No provider found in database for scheme {Scheme}", scheme);
        }

        // Cache the result (even if null, to avoid repeated lookups)
        _optionsCache.SetProvider(cacheKey, provider);

        return provider;
    }
}

/// <summary>
/// Thread-safe cache for dynamic provider configurations
/// </summary>
public class DynamicOptionsCache
{
    private readonly Dictionary<string, (ExternalProviderDefinition? Provider, DateTime Expiry)> _cache = new();
    private readonly object _lock = new();
    private readonly TimeSpan _defaultExpiry = TimeSpan.FromMinutes(5);

    public bool TryGetProvider(string key, out ExternalProviderDefinition? provider)
    {
        lock (_lock)
        {
            if (_cache.TryGetValue(key, out var entry))
            {
                if (entry.Expiry > DateTime.UtcNow)
                {
                    provider = entry.Provider;
                    return true;
                }

                _cache.Remove(key);
            }

            provider = null;
            return false;
        }
    }

    public void SetProvider(string key, ExternalProviderDefinition? provider, TimeSpan? expiry = null)
    {
        lock (_lock)
        {
            _cache[key] = (provider, DateTime.UtcNow.Add(expiry ?? _defaultExpiry));
        }
    }

    public void InvalidateForTenant(string tenantId)
    {
        lock (_lock)
        {
            var keysToRemove = _cache.Keys
                .Where(k => k.StartsWith($"{tenantId}:"))
                .ToList();

            foreach (var key in keysToRemove)
            {
                _cache.Remove(key);
            }
        }
    }

    public void InvalidateAll()
    {
        lock (_lock)
        {
            _cache.Clear();
        }
    }
}
