using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Configuration;

namespace Oluso.Middleware;

/// <summary>
/// Stamps <c>x-min-app-version</c> on every API response when <c>Oluso:Mobile:MinAppVersion</c>
/// is configured. The mobile app compares it to its own version on each call and forces an
/// update mid-session; the same value is published in the instance discovery document for
/// the pre-sign-in check. Nothing is emitted when the setting is absent.
/// </summary>
public sealed class MobileMinVersionMiddleware
{
    public const string HeaderName = "x-min-app-version";

    private readonly RequestDelegate _next;
    private readonly string? _minVersion;

    public MobileMinVersionMiddleware(RequestDelegate next, IConfiguration configuration)
    {
        _next = next;
        var raw = configuration["Oluso:Mobile:MinAppVersion"];
        _minVersion = !string.IsNullOrWhiteSpace(raw) && Version.TryParse(raw, out var parsed) ? parsed.ToString() : null;
    }

    public Task InvokeAsync(HttpContext context)
    {
        if (_minVersion is not null && context.Request.Path.StartsWithSegments("/api"))
        {
            context.Response.OnStarting(static state =>
            {
                var (response, version) = ((HttpResponse, string))state;
                response.Headers[HeaderName] = version;
                return Task.CompletedTask;
            }, (context.Response, _minVersion));
        }

        return _next(context);
    }
}
