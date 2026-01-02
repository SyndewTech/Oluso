using System.Security.Claims;
using System.Text.Json;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.FileProviders;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Oluso.AdminUI;

/// <summary>
/// Middleware for serving the Admin UI static files and handling SPA routing.
/// </summary>
public class AdminUIMiddleware
{
    private readonly RequestDelegate _next;
    private readonly AdminUIOptions _options;
    private readonly ILogger<AdminUIMiddleware> _logger;
    private readonly IFileProvider? _fileProvider;

    public AdminUIMiddleware(
        RequestDelegate next,
        IOptions<AdminUIOptions> options,
        ILogger<AdminUIMiddleware> logger)
    {
        _next = next;
        _options = options.Value;
        _logger = logger;

        // Initialize file provider if static files path exists
        var fullPath = Path.GetFullPath(_options.StaticFilesPath);
        if (Directory.Exists(fullPath))
        {
            _fileProvider = new PhysicalFileProvider(fullPath);
            _logger.LogInformation("Admin UI serving files from: {Path}", fullPath);
        }
        else
        {
            _logger.LogWarning("Admin UI static files path not found: {Path}", fullPath);
        }
    }

    public async Task InvokeAsync(HttpContext context)
    {
        var path = context.Request.Path.Value ?? "";

        // Check if this request is for the admin UI
        if (!path.StartsWith(_options.BasePath, StringComparison.OrdinalIgnoreCase))
        {
            await _next(context);
            return;
        }

        // If admin UI is disabled, return 404
        if (!_options.Enabled)
        {
            _logger.LogDebug("Admin UI is disabled, returning 404");
            context.Response.StatusCode = StatusCodes.Status404NotFound;
            return;
        }

        // If no file provider, return 404
        if (_fileProvider == null)
        {
            _logger.LogWarning("Admin UI file provider not available");
            context.Response.StatusCode = StatusCodes.Status404NotFound;
            return;
        }

        // Check authentication if required
        if (_options.RequireAuthentication)
        {
            var authResult = await AuthenticateAsync(context);
            if (!authResult.Succeeded)
            {
                _logger.LogDebug("Admin UI authentication failed: {FailureMessage}",
                    authResult.Failure?.Message ?? "Unknown reason");

                // Redirect to login or return 401
                context.Response.StatusCode = StatusCodes.Status401Unauthorized;
                return;
            }

            // Check authorization (roles and claims)
            if (!await AuthorizeAsync(context, authResult.Principal))
            {
                _logger.LogWarning("Admin UI authorization failed for user: {User}",
                    authResult.Principal?.Identity?.Name);
                context.Response.StatusCode = StatusCodes.Status403Forbidden;
                return;
            }
        }

        // Remove the base path to get the relative path
        var relativePath = path.Substring(_options.BasePath.Length);
        if (string.IsNullOrEmpty(relativePath))
        {
            relativePath = "/";
        }

        // Try to serve the static file
        if (await TryServeStaticFileAsync(context, relativePath))
        {
            return;
        }

        // If no static file found, serve index.html for SPA routing
        // (unless the path looks like an API request or has a file extension)
        if (!relativePath.StartsWith("/api/") && !HasFileExtension(relativePath))
        {
            if (await TryServeStaticFileAsync(context, "/" + _options.DefaultDocument, injectConfig: true))
            {
                return;
            }
        }

        // Fall through to next middleware
        await _next(context);
    }

    private async Task<AuthenticateResult> AuthenticateAsync(HttpContext context)
    {
        if (!string.IsNullOrEmpty(_options.AuthenticationScheme))
        {
            return await context.AuthenticateAsync(_options.AuthenticationScheme);
        }

        return await context.AuthenticateAsync();
    }

    private Task<bool> AuthorizeAsync(HttpContext context, ClaimsPrincipal? principal)
    {
        if (principal == null || !principal.Identity?.IsAuthenticated == true)
        {
            return Task.FromResult(false);
        }

        // Check required roles
        if (_options.RequiredRoles.Length > 0)
        {
            var hasAnyRole = _options.RequiredRoles.Any(role => principal.IsInRole(role));
            if (!hasAnyRole)
            {
                return Task.FromResult(false);
            }
        }

        // Check required claims
        foreach (var (claimType, claimValue) in _options.RequiredClaims)
        {
            if (!principal.HasClaim(claimType, claimValue))
            {
                return Task.FromResult(false);
            }
        }

        return Task.FromResult(true);
    }

    private async Task<bool> TryServeStaticFileAsync(HttpContext context, string relativePath, bool injectConfig = false)
    {
        var fileInfo = _fileProvider!.GetFileInfo(relativePath.TrimStart('/'));
        if (!fileInfo.Exists || fileInfo.IsDirectory)
        {
            return false;
        }

        // Set content type based on file extension
        var contentType = GetContentType(fileInfo.Name);
        context.Response.ContentType = contentType;

        // Set cache control header
        context.Response.Headers["Cache-Control"] = _options.CacheControl;

        // For HTML files, potentially inject configuration
        if (injectConfig && _options.ClientConfiguration.Count > 0 && contentType == "text/html")
        {
            var content = await ReadFileAsync(fileInfo);
            var configScript = $"<script>window.__ADMIN_CONFIG__ = {JsonSerializer.Serialize(_options.ClientConfiguration)};</script>";

            // Insert before </head> or </body>
            content = InjectScript(content, configScript);
            await context.Response.WriteAsync(content);
        }
        else
        {
            await using var stream = fileInfo.CreateReadStream();
            await stream.CopyToAsync(context.Response.Body);
        }

        return true;
    }

    private static async Task<string> ReadFileAsync(IFileInfo fileInfo)
    {
        await using var stream = fileInfo.CreateReadStream();
        using var reader = new StreamReader(stream);
        return await reader.ReadToEndAsync();
    }

    private static string InjectScript(string html, string script)
    {
        // Try to inject before </head>
        var headIndex = html.IndexOf("</head>", StringComparison.OrdinalIgnoreCase);
        if (headIndex >= 0)
        {
            return html.Insert(headIndex, script);
        }

        // Fallback: inject before </body>
        var bodyIndex = html.IndexOf("</body>", StringComparison.OrdinalIgnoreCase);
        if (bodyIndex >= 0)
        {
            return html.Insert(bodyIndex, script);
        }

        // Last resort: append to end
        return html + script;
    }

    private static bool HasFileExtension(string path)
    {
        var lastSlash = path.LastIndexOf('/');
        var afterSlash = lastSlash >= 0 ? path.Substring(lastSlash + 1) : path;
        return afterSlash.Contains('.');
    }

    private static string GetContentType(string fileName)
    {
        var extension = Path.GetExtension(fileName).ToLowerInvariant();
        return extension switch
        {
            ".html" => "text/html",
            ".htm" => "text/html",
            ".css" => "text/css",
            ".js" => "application/javascript",
            ".mjs" => "application/javascript",
            ".json" => "application/json",
            ".png" => "image/png",
            ".jpg" => "image/jpeg",
            ".jpeg" => "image/jpeg",
            ".gif" => "image/gif",
            ".svg" => "image/svg+xml",
            ".ico" => "image/x-icon",
            ".woff" => "font/woff",
            ".woff2" => "font/woff2",
            ".ttf" => "font/ttf",
            ".eot" => "application/vnd.ms-fontobject",
            ".otf" => "font/otf",
            ".map" => "application/json",
            ".webp" => "image/webp",
            ".webm" => "video/webm",
            ".mp4" => "video/mp4",
            ".txt" => "text/plain",
            ".xml" => "application/xml",
            _ => "application/octet-stream"
        };
    }
}
