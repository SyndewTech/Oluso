using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;

namespace Oluso.Middleware;

/// <summary>
/// Auto-injects <see cref="EarlyTenantHostMiddleware"/> at the very start
/// of the request pipeline, before any consumer-registered middleware
/// runs. Implemented as an <see cref="IStartupFilter"/> so application
/// authors don't have to remember to call a specific <c>Use…()</c>
/// extension method in the right place — the filter is pulled from DI
/// by the host and wraps the consumer's <c>Configure</c> delegate.
///
/// <para>Order in the pipeline:
/// <list type="number">
///   <item>This filter runs first → Layer 1 (host-based) sets context
///     for tenant-subdomain / custom-domain requests.</item>
///   <item><see cref="BearerTokenTenantResolverStartupFilter"/> runs
///     next → Layer 2 (token-based) sets context for bearer-API calls
///     on hosts without a tenant subdomain.</item>
///   <item><c>UseAuthentication</c> / <c>UseAuthorization</c>.</item>
///   <item><see cref="TenantResolutionMiddleware"/> → Layer 3
///     (post-auth) finalises context and enforces allowed_tenants.</item>
/// </list></para>
/// </summary>
public class EarlyTenantHostStartupFilter : IStartupFilter
{
    public Action<IApplicationBuilder> Configure(Action<IApplicationBuilder> next)
    {
        return app =>
        {
            app.UseMiddleware<EarlyTenantHostMiddleware>();
            next(app);
        };
    }
}
