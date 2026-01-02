using System.Text.Json;
using Microsoft.AspNetCore.Cors.Infrastructure;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Caching.Distributed;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.EntityFrameworkCore;
using Oluso.EntityFramework;

namespace Oluso.Services;

/// <summary>
/// Dynamic CORS policy provider that checks:
/// 1. App config origins - For admin UI, dev servers (Cors:Origins in appsettings)
/// 2. OAuth Client origins - SPAs configured as clients get CORS automatically
///
/// Why Clients specify CORS origins:
/// - OAuth clients (especially SPAs) need to call token, userinfo, and other endpoints
/// - The client already knows its origin (redirect URIs), so CORS is configured there
/// - This is the standard pattern used by IdentityServer, Auth0, Okta, etc.
///
/// Multi-tenancy note:
/// - This runs BEFORE tenant resolution middleware (CORS must be early in pipeline)
/// - We intentionally query ALL clients across ALL tenants for CORS
/// - A SPA at https://app.com should work regardless of which tenant URL it calls
/// - The DbContext query filter allows all entities when tenant context is null
/// </summary>
public class OlusoCorsPolicyProvider : ICorsPolicyProvider
{
    private const string CacheKey = "oluso:cors:origins";
    private static readonly TimeSpan CacheDuration = TimeSpan.FromMinutes(5);

    private readonly IServiceProvider _serviceProvider;
    private readonly ILogger<OlusoCorsPolicyProvider> _logger;
    private readonly HashSet<string> _configuredOrigins;

    public OlusoCorsPolicyProvider(
        IServiceProvider serviceProvider,
        IConfiguration configuration,
        ILogger<OlusoCorsPolicyProvider> logger)
    {
        _serviceProvider = serviceProvider;
        _logger = logger;

        // Cache configured origins at startup (these don't change)
        var origins = configuration.GetSection("Cors:Origins").Get<string[]>() ?? [];
        _configuredOrigins = new HashSet<string>(origins, StringComparer.OrdinalIgnoreCase);
    }

    public async Task<CorsPolicy?> GetPolicyAsync(HttpContext context, string? policyName)
    {
        // Only handle "Oluso" policy - other policies use default handling
        if (policyName != "Oluso")
        {
            return null;
        }

        var origin = context.Request.Headers.Origin.FirstOrDefault();
        if (string.IsNullOrEmpty(origin))
        {
            // No origin header = same-origin request, allow with default policy
            return BuildDefaultPolicy();
        }

        // 1. Check app config origins (fast, no DB)
        if (_configuredOrigins.Contains(origin))
        {
            _logger.LogDebug("CORS allowed for {Origin} (app config)", origin);
            return BuildPolicyForOrigin(origin);
        }

        // 2. Check OAuth client origins (cached from DB)
        var clientOrigins = await GetCachedClientOriginsAsync(context);
        if (clientOrigins.Contains(origin))
        {
            _logger.LogDebug("CORS allowed for {Origin} (OAuth client)", origin);
            return BuildPolicyForOrigin(origin);
        }

        _logger.LogDebug("CORS denied for {Origin}", origin);
        return BuildDefaultPolicy();
    }

    private async Task<HashSet<string>> GetCachedClientOriginsAsync(HttpContext context)
    {
        var cache = context.RequestServices.GetService<IDistributedCache>();

        // Try cache first
        if (cache != null)
        {
            var cachedJson = await cache.GetStringAsync(CacheKey);
            if (!string.IsNullOrEmpty(cachedJson))
            {
                try
                {
                    var cachedOrigins = JsonSerializer.Deserialize<List<string>>(cachedJson);
                    if (cachedOrigins != null)
                    {
                        return new HashSet<string>(cachedOrigins, StringComparer.OrdinalIgnoreCase);
                    }
                }
                catch
                {
                    // Cache corrupted, will re-fetch
                }
            }
        }

        // Query all client CORS origins (across all tenants)
        var origins = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        try
        {
            using var scope = _serviceProvider.CreateScope();

            // Use DbContext directly for efficient query
            // Note: tenant context is null here (CORS runs before tenant resolution),
            // so the query filter allows ALL clients across ALL tenants - this is correct
            var dbContext = scope.ServiceProvider.GetService<IOlusoDbContext>();
            if (dbContext != null)
            {
                // Query just the origins, not full clients - much more efficient
                var clientOrigins = await dbContext.Clients
                    .SelectMany(c => c.AllowedCorsOrigins)
                    .Select(o => o.Origin)
                    .Distinct()
                    .ToListAsync();

                foreach (var origin in clientOrigins)
                {
                    origins.Add(origin);
                }
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error loading client CORS origins");
        }

        // Cache the result
        if (cache != null)
        {
            var json = JsonSerializer.Serialize(origins.ToList());
            var options = new DistributedCacheEntryOptions
            {
                AbsoluteExpirationRelativeToNow = CacheDuration
            };
            await cache.SetStringAsync(CacheKey, json, options);
        }

        return origins;
    }

    private static CorsPolicy BuildPolicyForOrigin(string origin)
    {
        return new CorsPolicyBuilder()
            .WithOrigins(origin)
            .AllowAnyHeader()
            .AllowAnyMethod()
            .AllowCredentials()
            .Build();
    }

    private CorsPolicy BuildDefaultPolicy()
    {
        var builder = new CorsPolicyBuilder()
            .AllowAnyHeader()
            .AllowAnyMethod();

        if (_configuredOrigins.Count > 0)
        {
            builder.WithOrigins(_configuredOrigins.ToArray()).AllowCredentials();
        }

        return builder.Build();
    }
}

/// <summary>
/// Invalidates the CORS origins cache. Call this when client CORS origins change.
/// </summary>
public interface ICorsOriginsCacheInvalidator
{
    Task InvalidateCacheAsync(CancellationToken cancellationToken = default);
}

/// <summary>
/// Default implementation that clears the distributed cache
/// </summary>
public class CorsOriginsCacheInvalidator : ICorsOriginsCacheInvalidator
{
    private const string CacheKey = "oluso:cors:origins";
    private readonly IDistributedCache? _cache;

    public CorsOriginsCacheInvalidator(IDistributedCache? cache = null)
    {
        _cache = cache;
    }

    public async Task InvalidateCacheAsync(CancellationToken cancellationToken = default)
    {
        if (_cache != null)
        {
            await _cache.RemoveAsync(CacheKey, cancellationToken);
        }
    }
}
