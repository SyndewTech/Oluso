using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Caching.Distributed;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Oluso.Core.Domain.Entities;
using Oluso.Core.Domain.Interfaces;
using System.Security.Claims;

namespace Oluso.Middleware;

/// <summary>
/// Middleware that resolves the current tenant from the request and validates access.
/// For Admin API requests, validates that the authenticated user has access to the tenant
/// based on their organization membership.
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
        Tenant? resolvedTenant = null;

        if (!string.IsNullOrEmpty(tenantIdentifier))
        {
            var tenantStore = context.RequestServices.GetService<ITenantStore>();
            if (tenantStore != null)
            {
                resolvedTenant = await tenantStore.GetByIdentifierAsync(tenantIdentifier);
                if (resolvedTenant != null && resolvedTenant.Enabled)
                {
                    // Validate tenant access for Admin API requests
                    if (IsAdminApiRequest(context) && context.User.Identity?.IsAuthenticated == true)
                    {
                        var accessResult = await ValidateTenantAccessAsync(context, resolvedTenant);
                        if (!accessResult.HasAccess)
                        {
                            _logger.LogWarning(
                                "User {UserId} denied access to tenant {TenantId}: {Reason}",
                                context.User.FindFirst("sub")?.Value ?? "unknown",
                                resolvedTenant.Id,
                                accessResult.Reason);

                            context.Response.StatusCode = 403;
                            context.Response.ContentType = "application/json";
                            await context.Response.WriteAsync(
                                $"{{\"error\": \"access_denied\", \"error_description\": \"{accessResult.Reason}\"}}");
                            return;
                        }

                        // Add organization context claims for this tenant's organization
                        if (accessResult.Membership != null)
                        {
                            AddOrganizationContextClaims(context, accessResult.Membership, resolvedTenant);
                        }
                    }

                    tenantAccessor.SetTenant(resolvedTenant);
                    _logger.LogDebug("Resolved tenant: {TenantId} ({TenantIdentifier})", resolvedTenant.Id, resolvedTenant.Identifier);
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

    /// <summary>
    /// Determines if the request is for the Admin API
    /// </summary>
    private static bool IsAdminApiRequest(HttpContext context)
    {
        var path = context.Request.Path.Value ?? "";
        return path.StartsWith("/api/admin", StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>
    /// Validates that the authenticated user has access to the specified tenant
    /// based on their organization membership.
    /// </summary>
    private async Task<TenantAccessResult> ValidateTenantAccessAsync(HttpContext context, Tenant tenant)
    {
        var userId = context.User.FindFirst("sub")?.Value
            ?? context.User.FindFirst(ClaimTypes.NameIdentifier)?.Value;

        if (string.IsNullOrEmpty(userId))
        {
            return TenantAccessResult.Denied("User ID not found in token");
        }

        // Super admins bypass organization checks
        if (IsSuperAdmin(context.User))
        {
            _logger.LogDebug("Super admin {UserId} granted access to tenant {TenantId}", userId, tenant.Id);
            return TenantAccessResult.Granted(null);
        }

        // Get the organization membership store
        var membershipStore = context.RequestServices.GetService<IOrganizationMembershipStore>();
        if (membershipStore == null)
        {
            // Organization management not enabled, allow access (backward compatibility)
            _logger.LogDebug("Organization membership store not available, allowing access");
            return TenantAccessResult.Granted(null);
        }

        // Check if tenant has an organization
        if (string.IsNullOrEmpty(tenant.OrganizationId))
        {
            // Tenant not associated with an organization - could be a legacy tenant
            // Log warning but allow access for backward compatibility
            _logger.LogWarning("Tenant {TenantId} has no organization association", tenant.Id);
            return TenantAccessResult.Granted(null);
        }

        // Get user's membership in the tenant's organization
        var membership = await membershipStore.GetByUserAndOrganizationAsync(userId, tenant.OrganizationId);

        if (membership == null)
        {
            return TenantAccessResult.Denied($"User is not a member of organization {tenant.OrganizationId}");
        }

        // Check if user has access to this specific tenant
        var allowedTenantIds = await membershipStore.GetAllowedTenantIdsAsync(userId, tenant.OrganizationId);
        if (!allowedTenantIds.Contains(tenant.Id))
        {
            return TenantAccessResult.Denied($"User does not have access to tenant {tenant.Id} in organization {tenant.OrganizationId}");
        }

        _logger.LogDebug(
            "User {UserId} granted access to tenant {TenantId} with role {Role} in organization {OrganizationId}",
            userId, tenant.Id, membership.Role, tenant.OrganizationId);

        return TenantAccessResult.Granted(membership);
    }

    /// <summary>
    /// Adds claims to the current request context that indicate the user's role
    /// in the current tenant's organization. This allows downstream code to
    /// check permissions scoped to the current context.
    /// </summary>
    private static void AddOrganizationContextClaims(
        HttpContext context,
        OrganizationMembership membership,
        Tenant tenant)
    {
        // Add items to HttpContext.Items for use in current request
        // These are NOT added to the JWT - they're request-scoped context
        context.Items["CurrentOrgId"] = tenant.OrganizationId;
        context.Items["CurrentOrgRole"] = membership.Role;
        context.Items["CurrentOrgMembership"] = membership;

        // Also add as claims to the current identity for easier access in controllers
        if (context.User.Identity is ClaimsIdentity identity)
        {
            // Remove any existing current_org claims to avoid duplicates
            var existingOrgIdClaim = identity.FindFirst("current_org_id");
            var existingOrgRoleClaim = identity.FindFirst("current_org_role");
            if (existingOrgIdClaim != null) identity.RemoveClaim(existingOrgIdClaim);
            if (existingOrgRoleClaim != null) identity.RemoveClaim(existingOrgRoleClaim);

            // Add current context claims
            identity.AddClaim(new Claim("current_org_id", tenant.OrganizationId));
            identity.AddClaim(new Claim("current_org_role", membership.Role.ToString().ToLowerInvariant()));
        }
    }

    /// <summary>
    /// Checks if the user is a super admin (platform-wide access)
    /// </summary>
    private static bool IsSuperAdmin(ClaimsPrincipal user)
    {
        // Check for super_admin claim
        var superAdminClaim = user.FindFirst("super_admin")?.Value;
        if (superAdminClaim is "true" or "1")
            return true;

        // Check for SuperAdmin or SystemAdmin role (with null tenant_id)
        var userTenantId = user.FindFirst("tenant_id")?.Value
            ?? user.FindFirst("tid")?.Value;

        // Only users with no tenant scope can be SuperAdmin
        if (!string.IsNullOrEmpty(userTenantId))
            return false;

        return user.IsInRole("SuperAdmin") || user.IsInRole("SystemAdmin") ||
               user.IsInRole("super_admin") || user.IsInRole("platform_admin");
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

        // For Admin API requests from authenticated users, use tenant_id from JWT token
        // This allows tenant admins to access their tenant without explicit header
        if (IsAdminApiRequest(context) && context.User.Identity?.IsAuthenticated == true)
        {
            tenantId = ResolveFromToken(context);
            if (!string.IsNullOrEmpty(tenantId))
                return tenantId;
        }

        return null;
    }

    private static string? ResolveFromToken(HttpContext context)
    {
        // Get tenant_id claim from JWT token
        return context.User.FindFirst("tenant_id")?.Value
            ?? context.User.FindFirst("tid")?.Value
            ?? context.User.FindFirst("http://schemas.oluso.io/claims/tenant")?.Value;
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

/// <summary>
/// Result of tenant access validation
/// </summary>
internal class TenantAccessResult
{
    public bool HasAccess { get; private init; }
    public string? Reason { get; private init; }
    public OrganizationMembership? Membership { get; private init; }

    private TenantAccessResult() { }

    public static TenantAccessResult Granted(OrganizationMembership? membership) => new()
    {
        HasAccess = true,
        Membership = membership
    };

    public static TenantAccessResult Denied(string reason) => new()
    {
        HasAccess = false,
        Reason = reason
    };
}
