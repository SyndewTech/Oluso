using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;

namespace Oluso.Middleware;

/// <summary>
/// Auto-injects <see cref="BearerTokenTenantResolverMiddleware"/> at the
/// start of the request pipeline, immediately after
/// <see cref="EarlyTenantHostStartupFilter"/>.
///
/// <para>Order matters here:
/// <list type="bullet">
///   <item><b>Layer 1 wins when both signals are present.</b> A bearer
///     call to <c>foo.localhost</c> still resolves to <c>foo</c>, never
///     to whatever tenant the token claims — Layer 2 short-circuits via
///     <c>accessor.HasTenant</c>.</item>
///   <item><b>Layer 2 fills the gap on hosts without a tenant
///     subdomain.</b> The token-derived tenant takes effect before
///     <c>UseAuthentication</c> so the JWT bearer's signing-key lookup
///     and issuer validation see the right tenant context.</item>
/// </list></para>
/// </summary>
public class BearerTokenTenantResolverStartupFilter : IStartupFilter
{
    public Action<IApplicationBuilder> Configure(Action<IApplicationBuilder> next)
    {
        return app =>
        {
            app.UseMiddleware<BearerTokenTenantResolverMiddleware>();
            next(app);
        };
    }
}
