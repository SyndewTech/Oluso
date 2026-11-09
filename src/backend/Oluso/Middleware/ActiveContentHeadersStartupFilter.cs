using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;

namespace Oluso.Middleware;

/// <summary>
/// Auto-injects <see cref="ActiveContentHeadersMiddleware"/> at the start of the
/// pipeline so every response — including ones short-circuited by later
/// middleware — carries the hardening headers without the host wiring it.
/// </summary>
public sealed class ActiveContentHeadersStartupFilter : IStartupFilter
{
    public Action<IApplicationBuilder> Configure(Action<IApplicationBuilder> next)
    {
        return app =>
        {
            app.UseMiddleware<ActiveContentHeadersMiddleware>();
            next(app);
        };
    }
}
