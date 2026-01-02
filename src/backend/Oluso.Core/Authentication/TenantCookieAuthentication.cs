using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using Oluso.Core.Domain.Interfaces;
using System.Security.Claims;

namespace Oluso.Core.Authentication;

/// <summary>
/// Cookie options that scope authentication to the current tenant.
/// Ensures users authenticated in one tenant cannot access another tenant's resources.
/// Also configures server-side session support when ITicketStore is available.
/// </summary>
public class TenantCookieAuthenticationOptions : IPostConfigureOptions<CookieAuthenticationOptions>
{
    private readonly IServiceScopeFactory _scopeFactory;

    public TenantCookieAuthenticationOptions(IServiceScopeFactory scopeFactory)
    {
        _scopeFactory = scopeFactory;
    }

    public void PostConfigure(string? name, CookieAuthenticationOptions options)
    {
        // Configure server-side sessions if ticket store is available
        // We need to check if ITicketStore is registered by creating a temporary scope
        using var scope = _scopeFactory.CreateScope();
        var ticketStore = scope.ServiceProvider.GetService<ITicketStore>();
        if (ticketStore != null)
        {
            // Use a wrapper that resolves ITicketStore per-request from HttpContext
            options.SessionStore = new ScopedTicketStoreAdapter(_scopeFactory);
        }

        // Wrap the existing events
        var existingOnValidatePrincipal = options.Events.OnValidatePrincipal;

        options.Events.OnValidatePrincipal = async context =>
        {
            // Call existing handler first
            if (existingOnValidatePrincipal != null)
            {
                await existingOnValidatePrincipal(context);
                if (context.Principal == null) return;
            }

            // Validate tenant claim matches current tenant
            var tenantContext = context.HttpContext.RequestServices.GetService<ITenantContext>();
            if (tenantContext?.HasTenant == true)
            {
                var sessionTenantId = context.Principal?.FindFirstValue("tenant_id");

                // If session has a tenant but it doesn't match current tenant, reject
                if (!string.IsNullOrEmpty(sessionTenantId) &&
                    sessionTenantId != tenantContext.TenantId)
                {
                    context.RejectPrincipal();
                    await context.HttpContext.SignOutAsync();
                    return;
                }

                // If session has no tenant but we're in a tenant context, reject
                if (string.IsNullOrEmpty(sessionTenantId))
                {
                    context.RejectPrincipal();
                    await context.HttpContext.SignOutAsync();
                    return;
                }
            }
        };

        // Dynamic cookie name based on tenant (for path-based isolation)
        var existingCookieManager = options.CookieManager;
        options.CookieManager = new TenantAwareCookieManager(existingCookieManager);
    }
}

/// <summary>
/// Cookie manager that includes tenant context in cookie operations.
/// Scopes cookies by tenant to prevent cross-tenant cookie leakage.
/// For the default tenant, no suffix is added to maintain backward compatibility.
/// </summary>
public class TenantAwareCookieManager : ICookieManager
{
    private readonly ICookieManager _inner;
    private readonly string? _defaultTenantId;

    public TenantAwareCookieManager(ICookieManager? inner = null, string? defaultTenantId = "default")
    {
        _inner = inner ?? new ChunkingCookieManager();
        _defaultTenantId = defaultTenantId;
    }

    public string? GetRequestCookie(HttpContext context, string key)
    {
        var tenantContext = context.RequestServices.GetService<ITenantContext>();

        // For default tenant or no tenant, use base key first
        if (tenantContext?.HasTenant != true || IsDefaultTenant(tenantContext))
        {
            return _inner.GetRequestCookie(context, key);
        }

        // For non-default tenants, try tenant-scoped key first, then base key for migration
        var tenantKey = $"{key}.{tenantContext.TenantId}";
        return _inner.GetRequestCookie(context, tenantKey)
            ?? _inner.GetRequestCookie(context, key);
    }

    public void AppendResponseCookie(HttpContext context, string key, string? value, CookieOptions options)
    {
        var tenantKey = GetTenantScopedKey(context, key);
        var tenantOptions = GetTenantScopedOptions(context, options);
        _inner.AppendResponseCookie(context, tenantKey, value, tenantOptions);
    }

    public void DeleteCookie(HttpContext context, string key, CookieOptions options)
    {
        var tenantKey = GetTenantScopedKey(context, key);
        var tenantOptions = GetTenantScopedOptions(context, options);
        _inner.DeleteCookie(context, tenantKey, tenantOptions);

        // Also delete non-tenant cookie for migration/cleanup (only if we used a tenant key)
        if (tenantKey != key)
        {
            _inner.DeleteCookie(context, key, options);
        }
    }

    private string GetTenantScopedKey(HttpContext context, string key)
    {
        var tenantContext = context.RequestServices.GetService<ITenantContext>();

        // Don't add suffix for default tenant - keeps cookies clean and backward compatible
        if (tenantContext?.HasTenant == true && !IsDefaultTenant(tenantContext))
        {
            return $"{key}.{tenantContext.TenantId}";
        }
        return key;
    }

    private bool IsDefaultTenant(ITenantContext? tenantContext)
    {
        if (tenantContext?.TenantId == null) return false;

        // Check if tenant ID or identifier matches default
        return string.Equals(tenantContext.TenantId, _defaultTenantId, StringComparison.OrdinalIgnoreCase)
            || string.Equals(tenantContext.Tenant?.Identifier, _defaultTenantId, StringComparison.OrdinalIgnoreCase);
    }

    private static CookieOptions GetTenantScopedOptions(HttpContext context, CookieOptions options)
    {
        var tenantContext = context.RequestServices.GetService<ITenantContext>();

        // Clone options
        var newOptions = new CookieOptions
        {
            Domain = options.Domain,
            Expires = options.Expires,
            HttpOnly = options.HttpOnly,
            IsEssential = options.IsEssential,
            MaxAge = options.MaxAge,
            Path = options.Path,
            SameSite = options.SameSite,
            Secure = options.Secure
        };

        // If using subdomain strategy with a tenant, scope cookie to exact host
        // This provides natural isolation per subdomain
        var host = context.Request.Host.Host;
        var parts = host.Split('.');
        if (parts.Length >= 3 && tenantContext?.HasTenant == true)
        {
            // For tenant.auth.example.com, don't set domain (cookie scoped to exact host)
            // Clear any domain setting to ensure cookie is host-only
            newOptions.Domain = null;
        }

        // For path-based tenants, scope cookie path to tenant
        if (tenantContext?.HasTenant == true &&
            context.Request.RouteValues.TryGetValue("tenant", out var routeTenant) &&
            routeTenant != null)
        {
            newOptions.Path = $"/{routeTenant}";
        }

        return newOptions;
    }
}

/// <summary>
/// Adapter that resolves ITicketStore per-request from a scope.
/// This allows the scoped ITicketStore to be used with the singleton cookie options.
/// </summary>
public class ScopedTicketStoreAdapter : ITicketStore
{
    private readonly IServiceScopeFactory _scopeFactory;

    public ScopedTicketStoreAdapter(IServiceScopeFactory scopeFactory)
    {
        _scopeFactory = scopeFactory;
    }

    public async Task<string> StoreAsync(AuthenticationTicket ticket)
    {
        using var scope = _scopeFactory.CreateScope();
        var store = scope.ServiceProvider.GetRequiredService<ITicketStore>();
        return await store.StoreAsync(ticket);
    }

    public async Task RenewAsync(string key, AuthenticationTicket ticket)
    {
        using var scope = _scopeFactory.CreateScope();
        var store = scope.ServiceProvider.GetRequiredService<ITicketStore>();
        await store.RenewAsync(key, ticket);
    }

    public async Task<AuthenticationTicket?> RetrieveAsync(string key)
    {
        using var scope = _scopeFactory.CreateScope();
        var store = scope.ServiceProvider.GetRequiredService<ITicketStore>();
        return await store.RetrieveAsync(key);
    }

    public async Task RemoveAsync(string key)
    {
        using var scope = _scopeFactory.CreateScope();
        var store = scope.ServiceProvider.GetRequiredService<ITicketStore>();
        await store.RemoveAsync(key);
    }
}
