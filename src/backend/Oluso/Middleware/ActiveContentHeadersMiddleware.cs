using Microsoft.AspNetCore.Http;

namespace Oluso.Middleware;

/// <summary>
/// Response hardening for content the platform serves from its own origin (which
/// is also the sign-in origin). Every response gets <c>X-Content-Type-Options:
/// nosniff</c>; any response that is an active XML document — SVG, XHTML, XML —
/// gets a sandboxing Content-Security-Policy, so an uploaded file opened directly
/// in the browser can never run script on this origin. Every HTML page may be
/// framed only by this origin and the ones it names through <see cref="FramePolicy"/>.
/// Responses that already set their own policy keep it.
/// </summary>
public sealed class ActiveContentHeadersMiddleware
{
    private const string SandboxPolicy = "default-src 'none'; style-src 'unsafe-inline'; sandbox";

    private readonly RequestDelegate _next;

    public ActiveContentHeadersMiddleware(RequestDelegate next) => _next = next;

    public Task InvokeAsync(HttpContext context)
    {
        context.Response.OnStarting(static state =>
        {
            var http = (HttpContext)state;
            var response = http.Response;
            var headers = response.Headers;

            if (string.IsNullOrEmpty(headers.XContentTypeOptions))
                headers.XContentTypeOptions = "nosniff";

            if (IsActiveXml(response.ContentType) && string.IsNullOrEmpty(headers.ContentSecurityPolicy))
                headers.ContentSecurityPolicy = SandboxPolicy;

            if (IsHtml(response.ContentType) && string.IsNullOrEmpty(headers.ContentSecurityPolicy))
            {
                var others = FramePolicy.AllowedOrigins(http);
                headers.ContentSecurityPolicy = others.Count == 0
                    ? "frame-ancestors 'self'"
                    : $"frame-ancestors 'self' {string.Join(' ', others)}";
                // Older browsers read only X-Frame-Options, which can't name other sites; newer ones obey the CSP.
                if (others.Count == 0 && string.IsNullOrEmpty(headers.XFrameOptions))
                    headers.XFrameOptions = "SAMEORIGIN";
            }

            return Task.CompletedTask;
        }, context);

        return _next(context);
    }

    private static bool IsHtml(string? contentType) =>
        !string.IsNullOrEmpty(contentType)
        && contentType.Split(';', 2)[0].Trim().Equals("text/html", StringComparison.OrdinalIgnoreCase);

    private static bool IsActiveXml(string? contentType)
    {
        if (string.IsNullOrEmpty(contentType)) return false;
        var mediaType = contentType.Split(';', 2)[0].Trim();
        return mediaType.Equals("image/svg+xml", StringComparison.OrdinalIgnoreCase)
            || mediaType.Equals("application/xhtml+xml", StringComparison.OrdinalIgnoreCase)
            || mediaType.Equals("application/xml", StringComparison.OrdinalIgnoreCase)
            || mediaType.Equals("text/xml", StringComparison.OrdinalIgnoreCase);
    }
}
