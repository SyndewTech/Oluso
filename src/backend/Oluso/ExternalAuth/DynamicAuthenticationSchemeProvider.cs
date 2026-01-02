using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using Oluso.Core.Domain.Interfaces;
using Oluso.Core.Services;

namespace Oluso.ExternalAuth;

/// <summary>
/// Authentication scheme provider that dynamically resolves schemes based on tenant context.
/// Supports per-tenant OAuth/OIDC provider configurations stored in database.
/// </summary>
public class DynamicAuthenticationSchemeProvider : AuthenticationSchemeProvider
{
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly IHttpContextAccessor _httpContextAccessor;
    private readonly DynamicSchemeCache _schemeCache;

    public DynamicAuthenticationSchemeProvider(
        IOptions<AuthenticationOptions> options,
        IHttpContextAccessor httpContextAccessor,
        DynamicSchemeCache schemeCache,
        IServiceScopeFactory scopeFactory)
        : base(options)
    {
        _httpContextAccessor = httpContextAccessor;
        _schemeCache = schemeCache;
        _scopeFactory = scopeFactory;
    }

    private ITenantContext? GetTenantContext()
    {
        return _httpContextAccessor.HttpContext?.RequestServices.GetService<ITenantContext>();
    }

    public override async Task<AuthenticationScheme?> GetSchemeAsync(string name)
    {
        // First check static schemes (registered at startup)
        var scheme = await base.GetSchemeAsync(name);
        if (scheme != null)
        {
            return scheme;
        }

        var tenantContext = GetTenantContext();
        var tenantId = tenantContext?.TenantId ?? "global";
        var cacheKey = $"{tenantId}:{name}";

        // Check cache first
        if (_schemeCache.TryGetScheme(cacheKey, out var cachedScheme))
        {
            return cachedScheme;
        }

        // Load from database using a new scope to avoid DbContext concurrency issues
        using var scope = _scopeFactory.CreateScope();
        var providerStore = scope.ServiceProvider.GetService<IExternalProviderConfigStore>();
        if (providerStore == null)
        {
            return null;
        }

        var provider = await providerStore.GetBySchemeAsync(name);
        if (provider == null || !provider.Enabled)
        {
            return null;
        }

        // Create scheme based on provider type
        var handlerType = GetHandlerType(provider.ProviderType);
        if (handlerType == null)
        {
            return null;
        }

        var dynamicScheme = new AuthenticationScheme(
            name,
            provider.DisplayName ?? name,
            handlerType);

        // Cache the scheme
        _schemeCache.SetScheme(cacheKey, dynamicScheme);

        return dynamicScheme;
    }

    public override async Task<IEnumerable<AuthenticationScheme>> GetAllSchemesAsync()
    {
        var schemes = (await base.GetAllSchemesAsync()).ToList();

        // Load from database using a new scope to avoid DbContext concurrency issues
        using var scope = _scopeFactory.CreateScope();
        var providerStore = scope.ServiceProvider.GetService<IExternalProviderConfigStore>();
        if (providerStore == null)
        {
            return schemes;
        }

        // Add dynamic schemes from database
        var providers = await providerStore.GetEnabledProvidersAsync();
        foreach (var provider in providers)
        {
            if (schemes.Any(s => s.Name.Equals(provider.Scheme, StringComparison.OrdinalIgnoreCase)))
            {
                continue; // Skip if already registered statically
            }

            var handlerType = GetHandlerType(provider.ProviderType);
            if (handlerType != null)
            {
                schemes.Add(new AuthenticationScheme(
                    provider.Scheme,
                    provider.DisplayName ?? provider.Scheme,
                    handlerType));
            }
        }

        return schemes;
    }

    public override async Task<IEnumerable<AuthenticationScheme>> GetRequestHandlerSchemesAsync()
    {
        // Include dynamic schemes that can handle requests
        return await GetAllSchemesAsync();
    }

    private static Type? GetHandlerType(string providerType)
    {
        return providerType.ToLowerInvariant() switch
        {
            "google" => typeof(Microsoft.AspNetCore.Authentication.Google.GoogleHandler),
            "microsoft" => typeof(Microsoft.AspNetCore.Authentication.MicrosoftAccount.MicrosoftAccountHandler),
            "facebook" => typeof(Microsoft.AspNetCore.Authentication.Facebook.FacebookHandler),
            "twitter" => typeof(Microsoft.AspNetCore.Authentication.Twitter.TwitterHandler),
            "github" => typeof(AspNet.Security.OAuth.GitHub.GitHubAuthenticationHandler),
            "linkedin" => typeof(AspNet.Security.OAuth.LinkedIn.LinkedInAuthenticationHandler),
            "apple" => typeof(AspNet.Security.OAuth.Apple.AppleAuthenticationHandler),
            "oidc" => typeof(Microsoft.AspNetCore.Authentication.OpenIdConnect.OpenIdConnectHandler),
            "oauth" => typeof(Microsoft.AspNetCore.Authentication.OAuth.OAuthHandler<Microsoft.AspNetCore.Authentication.OAuth.OAuthOptions>),
            _ => null
        };
    }
}

/// <summary>
/// Thread-safe cache for dynamic authentication schemes
/// </summary>
public class DynamicSchemeCache
{
    private readonly Dictionary<string, (AuthenticationScheme Scheme, DateTime Expiry)> _cache = new();
    private readonly object _lock = new();
    private readonly TimeSpan _defaultExpiry = TimeSpan.FromMinutes(5);

    public bool TryGetScheme(string key, out AuthenticationScheme? scheme)
    {
        lock (_lock)
        {
            if (_cache.TryGetValue(key, out var entry))
            {
                if (entry.Expiry > DateTime.UtcNow)
                {
                    scheme = entry.Scheme;
                    return true;
                }

                _cache.Remove(key);
            }

            scheme = null;
            return false;
        }
    }

    public void SetScheme(string key, AuthenticationScheme scheme, TimeSpan? expiry = null)
    {
        lock (_lock)
        {
            _cache[key] = (scheme, DateTime.UtcNow.Add(expiry ?? _defaultExpiry));
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
