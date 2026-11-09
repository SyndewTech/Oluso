using System.Text.Json;
using Microsoft.AspNetCore.Cors.Infrastructure;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Caching.Distributed;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.EntityFrameworkCore;
using Oluso.Core.Domain.Interfaces;
using Oluso.Core.Services;
using Oluso.EntityFramework;
using Oluso.Middleware;

namespace Oluso.Services;

/// <summary>
/// Dynamic CORS policy provider that checks, in order:
/// 1. App config origins - For admin UI, dev servers (Oluso:Cors:Origins in appsettings)
/// 2. Public metadata (/.well-known/*: discovery, JWKS) - any origin, without credentials
/// 3. OAuth Client origins - SPAs configured as clients get CORS automatically
/// 4. The tenant's own CORS settings - on the tenant's host only
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
    internal const string ClientOriginsCacheKey = "oluso:cors:origins";
    internal static string TenantCacheKey(string tenantId) => $"oluso:cors:tenant:{tenantId}";
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
        var origins = configuration.GetSection("Oluso:Cors:Origins").Get<string[]>() ?? [];
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

        // 2. Public metadata: discovery documents and keys are meant for any client to read,
        //    and carry nothing a cookie could unlock.
        if (context.Request.Path.StartsWithSegments("/.well-known"))
        {
            return BuildAnyOriginPolicy();
        }

        // 3. Check OAuth client origins (cached from DB)
        var clientOrigins = await GetCachedClientOriginsAsync(context);
        if (clientOrigins.Contains(origin))
        {
            _logger.LogDebug("CORS allowed for {Origin} (OAuth client)", origin);
            return BuildPolicyForOrigin(origin);
        }

        // 4. The tenant's own CORS settings, on the tenant's host only
        var tenantCors = await GetTenantCorsAsync(context);
        if (tenantCors != null)
        {
            if (tenantCors.AllowedOrigins.Contains(origin, StringComparer.OrdinalIgnoreCase))
            {
                _logger.LogDebug("CORS allowed for {Origin} (tenant settings)", origin);
                return BuildPolicyForOrigin(origin);
            }
            if (tenantCors.AllowAllOrigins)
            {
                _logger.LogDebug("CORS allowed for {Origin} (tenant allows all origins, no credentials)", origin);
                return BuildAnyOriginPolicy();
            }
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
            var cachedJson = await cache.GetStringAsync(ClientOriginsCacheKey);
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
            await cache.SetStringAsync(ClientOriginsCacheKey, json, options);
        }

        return origins;
    }

    /// <summary>
    /// The CORS settings of the tenant the request's host belongs to (cached), or null when the
    /// host names no tenant. Runs before tenant resolution, so it resolves the host itself.
    /// </summary>
    private async Task<TenantCorsSettings?> GetTenantCorsAsync(HttpContext context)
    {
        try
        {
            var identifier = await TenantResolutionMiddleware.ResolveHostTenantIdentifierAsync(
                context, TenantResolutionMiddleware.EffectiveOptions(context));
            if (string.IsNullOrEmpty(identifier)) return null;

            var tenantStore = context.RequestServices.GetService<ITenantStore>();
            if (tenantStore == null) return null;
            var tenant = await tenantStore.GetByIdentifierAsync(identifier) ?? await tenantStore.GetByIdAsync(identifier);
            if (tenant == null) return null;

            var cache = context.RequestServices.GetService<IDistributedCache>();
            var key = TenantCacheKey(tenant.Id);
            if (cache != null)
            {
                var cached = await cache.GetStringAsync(key);
                if (!string.IsNullOrEmpty(cached))
                {
                    return JsonSerializer.Deserialize<TenantCorsSettings>(cached);
                }
            }

            var config = context.RequestServices.GetService<ITenantConfigurationService>();
            var settings = (config == null ? null : await config.GetSectionAsync<TenantCorsSettings>(tenant.Id, TenantCorsSettings.SectionKey))
                ?? new TenantCorsSettings();

            if (cache != null)
            {
                await cache.SetStringAsync(key, JsonSerializer.Serialize(settings),
                    new DistributedCacheEntryOptions { AbsoluteExpirationRelativeToNow = CacheDuration });
            }
            return settings;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error loading tenant CORS settings");
            return null;
        }
    }

    /// <summary>
    /// Response headers a browser client must read across origins: the auth challenge, and the DPoP nonce it
    /// has to put in its next proof (RFC 9449 §8).
    /// </summary>
    private static readonly string[] ExposedHeaders = ["WWW-Authenticate", "DPoP-Nonce"];

    /// <summary>Any origin, without credentials (cookies are never sent or accepted).</summary>
    private static CorsPolicy BuildAnyOriginPolicy()
    {
        return new CorsPolicyBuilder()
            .AllowAnyOrigin()
            .AllowAnyHeader()
            .AllowAnyMethod()
            .WithExposedHeaders(ExposedHeaders)
            .Build();
    }

    private static CorsPolicy BuildPolicyForOrigin(string origin)
    {
        return new CorsPolicyBuilder()
            .WithOrigins(origin)
            .AllowAnyHeader()
            .AllowAnyMethod()
            .AllowCredentials()
            .WithExposedHeaders(ExposedHeaders)
            .Build();
    }

    private CorsPolicy BuildDefaultPolicy()
    {
        var builder = new CorsPolicyBuilder()
            .AllowAnyHeader()
            .AllowAnyMethod()
            .WithExposedHeaders(ExposedHeaders);

        if (_configuredOrigins.Count > 0)
        {
            builder.WithOrigins(_configuredOrigins.ToArray()).AllowCredentials();
        }

        return builder.Build();
    }
}

/// <summary>Clears the distributed-cache entries the CORS policy provider reads.</summary>
public class CorsOriginsCacheInvalidator : ICorsOriginsCacheInvalidator
{
    private readonly IDistributedCache? _cache;

    public CorsOriginsCacheInvalidator(IDistributedCache? cache = null)
    {
        _cache = cache;
    }

    public async Task InvalidateCacheAsync(CancellationToken cancellationToken = default)
    {
        if (_cache != null) await _cache.RemoveAsync(OlusoCorsPolicyProvider.ClientOriginsCacheKey, cancellationToken);
    }

    public async Task InvalidateTenantAsync(string tenantId, CancellationToken cancellationToken = default)
    {
        if (_cache != null) await _cache.RemoveAsync(OlusoCorsPolicyProvider.TenantCacheKey(tenantId), cancellationToken);
    }
}
