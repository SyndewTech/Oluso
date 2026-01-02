namespace Oluso.AdminUI;

/// <summary>
/// Configuration options for the Admin UI middleware
/// </summary>
public class AdminUIOptions
{
    /// <summary>
    /// Whether the Admin UI is enabled. Default is true.
    /// When disabled, all admin UI routes will return 404.
    /// </summary>
    public bool Enabled { get; set; } = true;

    /// <summary>
    /// The base path for the admin UI. Default is "/admin".
    /// </summary>
    public string BasePath { get; set; } = "/admin";

    /// <summary>
    /// The file system path to the admin UI static files.
    /// Default is "wwwroot/admin".
    /// </summary>
    public string StaticFilesPath { get; set; } = "wwwroot/admin";

    /// <summary>
    /// The default document to serve for SPA routes. Default is "index.html".
    /// </summary>
    public string DefaultDocument { get; set; } = "index.html";

    /// <summary>
    /// Whether to require authentication to access the admin UI. Default is true.
    /// </summary>
    public bool RequireAuthentication { get; set; } = true;

    /// <summary>
    /// The authentication scheme to use. Default is null (uses default scheme).
    /// </summary>
    public string? AuthenticationScheme { get; set; }

    /// <summary>
    /// Required roles to access the admin UI. Default is empty (any authenticated user).
    /// </summary>
    public string[] RequiredRoles { get; set; } = Array.Empty<string>();

    /// <summary>
    /// Required claims to access the admin UI.
    /// </summary>
    public Dictionary<string, string> RequiredClaims { get; set; } = new();

    /// <summary>
    /// Cache control header for static files. Default is "public, max-age=3600".
    /// </summary>
    public string CacheControl { get; set; } = "public, max-age=3600";

    /// <summary>
    /// Whether to enable response compression for static files. Default is true.
    /// </summary>
    public bool EnableCompression { get; set; } = true;

    /// <summary>
    /// Custom configuration to inject into the admin UI (accessible via window.__ADMIN_CONFIG__).
    /// </summary>
    public Dictionary<string, object?> ClientConfiguration { get; set; } = new();
}
