using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Caching.Distributed;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Oluso.Core.Domain.Interfaces;

namespace Oluso.Middleware;

/// <summary>
/// Middleware that resolves the current tenant from the request
/// </summary>
public class TenantResolutionMiddleware
{
    private readonly RequestDelegate _next;
    private readonly ILogger<TenantResolutionMiddleware> _logger;

    public TenantResolutionMiddleware(
        RequestDelegate next,
        ILogger<TenantResolutionMiddleware> logger)
    {
        _next = next;
        _logger = logger;
    }

    public async Task InvokeAsync(HttpContext context)
    {
        var options = context.RequestServices.GetService<IOptions<MultiTenancyOptions>>()?.Value
            ?? new MultiTenancyOptions();

        var tenantAccessor = context.RequestServices.GetService<ITenantContextAccessor>();

        if (tenantAccessor == null)
        {
            // Multi-tenancy not enabled, continue without tenant context
            await _next(context);
            return;
        }

        var tenantIdentifier = ResolveTenantIdentifier(context, options);

        if (!string.IsNullOrEmpty(tenantIdentifier))
        {
            var tenantStore = context.RequestServices.GetService<ITenantStore>();
            if (tenantStore != null)
            {
                var tenant = await tenantStore.GetByIdentifierAsync(tenantIdentifier);
                if (tenant != null && tenant.Enabled)
                {
                    tenantAccessor.SetTenant(tenant);
                    _logger.LogDebug("Resolved tenant: {TenantId} ({TenantIdentifier})", tenant.Id, tenant.Identifier);
                }
                else
                {
                    _logger.LogWarning("Tenant not found or disabled: {TenantIdentifier}", tenantIdentifier);
                }
            }
        }
        else if (!string.IsNullOrEmpty(options.DefaultTenantId))
        {
            // Use default tenant
            var tenantStore = context.RequestServices.GetService<ITenantStore>();
            if (tenantStore != null)
            {
                var defaultTenant = await tenantStore.GetByIdAsync(options.DefaultTenantId);
                if (defaultTenant != null && defaultTenant.Enabled)
                {
                    tenantAccessor.SetTenant(defaultTenant);
                }
            }
        }

        try
        {
            await _next(context);
        }
        finally
        {
            tenantAccessor.ClearTenant();
        }
    }

    private static string? ResolveTenantIdentifier(HttpContext context, MultiTenancyOptions options)
    {
        // Try strategies in order of preference, falling back to next if not found
        // Header and query string are always checked first as they're most explicit
        var tenantId = ResolveFromHeader(context, options.TenantHeaderName);
        if (!string.IsNullOrEmpty(tenantId))
            return tenantId;

        tenantId = ResolveFromQueryString(context);
        if (!string.IsNullOrEmpty(tenantId))
            return tenantId;

        // Then try host-based strategies if configured
        if (options.ResolutionStrategy == TenantResolutionStrategy.Subdomain)
        {
            tenantId = ResolveFromSubdomain(context);
            if (!string.IsNullOrEmpty(tenantId))
                return tenantId;
        }
        else if (options.ResolutionStrategy == TenantResolutionStrategy.Domain)
        {
            tenantId = ResolveFromDomain(context);
            if (!string.IsNullOrEmpty(tenantId))
                return tenantId;
        }

        return null;
    }

    private static string? ResolveFromDomain(HttpContext context)
    {
        // Use the full host (without port) as tenant identifier
        // This allows mapping entire domains to tenants
        var host = context.Request.Host.Host;
        if (string.IsNullOrEmpty(host))
            return null;

        // Skip localhost for development
        if (host.Equals("localhost", StringComparison.OrdinalIgnoreCase))
            return null;

        return host;
    }

    private static string? ResolveFromSubdomain(HttpContext context)
    {
        // Expected format: {tenant-identifier}.domain.com
        var host = context.Request.Host.Host;
        if (string.IsNullOrEmpty(host))
            return null;

        var parts = host.Split('.');
        if (parts.Length < 3) // Need at least subdomain.domain.tld
            return null;

        var subdomain = parts[0];

        // Skip common non-tenant subdomains
        var reservedSubdomains = new[] { "www", "api", "admin", "auth", "login" };
        if (reservedSubdomains.Contains(subdomain, StringComparer.OrdinalIgnoreCase))
            return null;

        return subdomain;
    }

    private static string? ResolveFromHeader(HttpContext context, string headerName)
    {
        if (context.Request.Headers.TryGetValue(headerName, out var value))
        {
            return value.FirstOrDefault();
        }
        return null;
    }

    private static string? ResolveFromQueryString(HttpContext context)
    {
        return context.Request.Query["tenant"].FirstOrDefault();
    }
}

/// <summary>
/// Middleware for handling CORS for OIDC endpoints
/// </summary>
public class OidcCorsMiddleware
{
    private readonly RequestDelegate _next;

    public OidcCorsMiddleware(RequestDelegate next)
    {
        _next = next;
    }

    public async Task InvokeAsync(HttpContext context)
    {
        // For OIDC endpoints that may receive cross-origin requests
        // (e.g., token endpoint from SPAs)
        var path = context.Request.Path.Value ?? "";

        if (IsOidcEndpoint(context, path))
        {
            // Allow CORS for these endpoints
            // The actual CORS policy is configured elsewhere,
            // this just ensures OPTIONS requests are handled
            if (context.Request.Method == "OPTIONS")
            {
                context.Response.StatusCode = 204;
                return;
            }
        }

        await _next(context);
    }

    private static bool IsOidcEndpoint(HttpContext context, string path)
    {
        var config = context.RequestServices
            .GetService<IOptions<Oluso.Core.Protocols.OidcEndpointConfiguration>>()?.Value;

        if (config == null)
        {
            // Fallback to default paths if config not available
            return path.Contains("/connect/") || path.Contains("/.well-known/");
        }

        // Check against all configured OIDC endpoints
        return path.StartsWith(config.AuthorizeEndpoint, StringComparison.OrdinalIgnoreCase)
            || path.StartsWith(config.TokenEndpoint, StringComparison.OrdinalIgnoreCase)
            || path.StartsWith(config.UserInfoEndpoint, StringComparison.OrdinalIgnoreCase)
            || path.StartsWith(config.RevocationEndpoint, StringComparison.OrdinalIgnoreCase)
            || path.StartsWith(config.IntrospectionEndpoint, StringComparison.OrdinalIgnoreCase)
            || path.StartsWith(config.EndSessionEndpoint, StringComparison.OrdinalIgnoreCase)
            || path.StartsWith(config.DeviceAuthorizationEndpoint, StringComparison.OrdinalIgnoreCase)
            || path.StartsWith(config.PushedAuthorizationEndpoint, StringComparison.OrdinalIgnoreCase)
            || path.StartsWith(config.DiscoveryEndpoint, StringComparison.OrdinalIgnoreCase)
            || path.StartsWith(config.JwksEndpoint, StringComparison.OrdinalIgnoreCase);
    }
}

/// <summary>
/// Options for host validation middleware
/// </summary>
public class HostValidationOptions
{
    /// <summary>
    /// Whether to enable host validation. Default is false.
    /// When enabled, requests from unknown hosts will be rejected.
    /// </summary>
    public bool Enabled { get; set; } = false;

    /// <summary>
    /// Additional allowed hosts beyond the server's IssuerUri and tenant custom domains.
    /// Useful for allowing localhost during development or load balancer health checks.
    /// </summary>
    public List<string> AdditionalAllowedHosts { get; set; } = new();

    /// <summary>
    /// Whether to allow localhost in development environments. Default is true.
    /// </summary>
    public bool AllowLocalhostInDevelopment { get; set; } = true;

    /// <summary>
    /// Cache duration for allowed hosts. Default is 5 minutes.
    /// Set to TimeSpan.Zero to disable caching.
    /// </summary>
    public TimeSpan CacheDuration { get; set; } = TimeSpan.FromMinutes(5);
}

/// <summary>
/// Middleware that validates request host against known hosts.
/// Rejects requests from unknown/unexpected hosts for security.
/// Known hosts include:
/// - Server's configured IssuerUri
/// - All tenant custom domains
/// - Additional allowed hosts from configuration
/// Uses distributed cache to support multi-instance deployments.
/// </summary>
public class HostValidationMiddleware
{
    private const string CacheKey = "oluso:hosts:allowed";
    private readonly RequestDelegate _next;
    private readonly ILogger<HostValidationMiddleware> _logger;

    public HostValidationMiddleware(
        RequestDelegate next,
        ILogger<HostValidationMiddleware> logger)
    {
        _next = next;
        _logger = logger;
    }

    public async Task InvokeAsync(HttpContext context)
    {
        var options = context.RequestServices.GetService<IOptions<HostValidationOptions>>()?.Value
            ?? new HostValidationOptions();

        // Skip validation if not enabled
        if (!options.Enabled)
        {
            await _next(context);
            return;
        }

        var requestHost = context.Request.Host.Host;
        if (string.IsNullOrEmpty(requestHost))
        {
            _logger.LogWarning("Request missing Host header, rejecting");
            context.Response.StatusCode = 400;
            await context.Response.WriteAsync("Bad Request: Missing Host header");
            return;
        }

        // Check if localhost is allowed in development
        if (options.AllowLocalhostInDevelopment &&
            (requestHost.Equals("localhost", StringComparison.OrdinalIgnoreCase) ||
             requestHost.Equals("127.0.0.1", StringComparison.OrdinalIgnoreCase)))
        {
            var env = context.RequestServices.GetService<Microsoft.AspNetCore.Hosting.IWebHostEnvironment>();
            if (env?.EnvironmentName == "Development")
            {
                await _next(context);
                return;
            }
        }

        // Get cached allowed hosts or build from sources
        var allowedHosts = await GetAllowedHostsAsync(context, options);

        // Check if request host is in allowed list
        if (allowedHosts.Contains(requestHost.ToLowerInvariant()))
        {
            await _next(context);
            return;
        }

        // Host not recognized
        _logger.LogWarning("Request from unknown host '{Host}', rejecting", requestHost);
        context.Response.StatusCode = 421; // Misdirected Request
        await context.Response.WriteAsync("Misdirected Request: Unknown host");
    }

    private async Task<HashSet<string>> GetAllowedHostsAsync(HttpContext context, HostValidationOptions options)
    {
        var cache = context.RequestServices.GetService<Microsoft.Extensions.Caching.Distributed.IDistributedCache>();

        // Try to get from cache first
        if (cache != null && options.CacheDuration > TimeSpan.Zero)
        {
            var cachedJson = await cache.GetStringAsync(CacheKey);
            if (!string.IsNullOrEmpty(cachedJson))
            {
                try
                {
                    var cachedHosts = System.Text.Json.JsonSerializer.Deserialize<List<string>>(cachedJson);
                    if (cachedHosts != null)
                    {
                        return new HashSet<string>(cachedHosts, StringComparer.OrdinalIgnoreCase);
                    }
                }
                catch
                {
                    // Cache corrupted, will re-fetch
                }
            }
        }

        // Build allowed hosts set
        var allowedHosts = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        // Add additional allowed hosts from options
        foreach (var host in options.AdditionalAllowedHosts)
        {
            allowedHosts.Add(host.ToLowerInvariant());
        }

        // Add server's configured IssuerUri host
        var configuration = context.RequestServices.GetService<Microsoft.Extensions.Configuration.IConfiguration>();
        var issuerUri = configuration?["Oluso:IssuerUri"];
        if (!string.IsNullOrEmpty(issuerUri))
        {
            try
            {
                var issuerHost = new Uri(issuerUri).Host;
                allowedHosts.Add(issuerHost.ToLowerInvariant());
            }
            catch (UriFormatException)
            {
                // Invalid URI, skip
            }
        }

        // Add tenant custom domains
        var tenantStore = context.RequestServices.GetService<ITenantStore>();
        if (tenantStore != null)
        {
            var tenants = await tenantStore.GetAllAsync();
            foreach (var tenant in tenants.Where(t => t.Enabled && !string.IsNullOrEmpty(t.CustomDomain)))
            {
                allowedHosts.Add(tenant.CustomDomain!.ToLowerInvariant());
            }
        }

        // Cache the result
        if (cache != null && options.CacheDuration > TimeSpan.Zero)
        {
            var json = System.Text.Json.JsonSerializer.Serialize(allowedHosts.ToList());
            var cacheOptions = new Microsoft.Extensions.Caching.Distributed.DistributedCacheEntryOptions
            {
                AbsoluteExpirationRelativeToNow = options.CacheDuration
            };
            await cache.SetStringAsync(CacheKey, json, cacheOptions);
            _logger.LogDebug("Cached {Count} allowed hosts for {Duration}",
                allowedHosts.Count, options.CacheDuration);
        }

        return allowedHosts;
    }
}

/// <summary>
/// Default implementation of host validation cache invalidator
/// </summary>
public class HostValidationCacheInvalidator : IHostValidationCacheInvalidator
{
    private const string CacheKey = "oluso:hosts:allowed";
    private readonly Microsoft.Extensions.Caching.Distributed.IDistributedCache? _cache;

    public HostValidationCacheInvalidator(Microsoft.Extensions.Caching.Distributed.IDistributedCache? cache = null)
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
