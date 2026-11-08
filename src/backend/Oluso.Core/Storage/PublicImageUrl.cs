namespace Oluso.Core.Storage;

/// <summary>
/// Builds image URLs in Cloudflare Image Resizing's native scheme
/// (<c>{base}/cdn-cgi/image/{options}/{source}</c>). In production Cloudflare
/// resizes on demand at the edge; in local/dev the same URL is served by the
/// resize shim. No custom URL format is introduced.
/// </summary>
public static class PublicImageUrl
{
    /// <summary>
    /// Wrap an already-public original image URL in a Cloudflare resize URL for
    /// the given preset. <paramref name="resizeBaseUrl"/> is the Cloudflare zone
    /// origin; when null the original's own origin is used (dev shim resizes).
    /// </summary>
    public static string ForPreset(string originalUrl, ImagePreset preset, string? resizeBaseUrl = null)
        => Build(originalUrl, ImagePresets.ToCloudflareOptions(preset), resizeBaseUrl);

    public static string ForWidth(string originalUrl, int width, int quality = 82, string? resizeBaseUrl = null)
        => Build(originalUrl, ImagePresets.ToCloudflareOptions(width, quality), resizeBaseUrl);

    /// <summary>
    /// Build a <c>srcset</c> value across the given presets, e.g.
    /// <c>{url-card} 400w, {url-detail} 800w</c>, for responsive images.
    /// </summary>
    public static string SrcSet(string originalUrl, IEnumerable<ImagePreset> presets, string? resizeBaseUrl = null)
        => string.Join(", ", presets.Select(p => $"{ForPreset(originalUrl, p, resizeBaseUrl)} {p.MaxWidth}w"));

    private static string Build(string originalUrl, string options, string? resizeBaseUrl)
    {
        if (string.IsNullOrEmpty(originalUrl)) return originalUrl;

        // Same-origin relative source is fine when no separate resize base; when a
        // Cloudflare zone is configured, hand it the absolute source URL.
        if (string.IsNullOrEmpty(resizeBaseUrl))
        {
            var origin = OriginOf(originalUrl);
            var path = originalUrl.Length > origin.Length ? originalUrl[origin.Length..] : originalUrl;
            return string.IsNullOrEmpty(origin)
                ? $"/cdn-cgi/image/{options}{EnsureLeadingSlash(originalUrl)}"
                : $"{origin}/cdn-cgi/image/{options}{EnsureLeadingSlash(path)}";
        }

        return $"{resizeBaseUrl.TrimEnd('/')}/cdn-cgi/image/{options}/{originalUrl}";
    }

    private static string OriginOf(string url)
    {
        if (!url.StartsWith("http", StringComparison.OrdinalIgnoreCase)) return string.Empty;
        var schemeEnd = url.IndexOf("://", StringComparison.Ordinal);
        if (schemeEnd < 0) return string.Empty;
        var hostStart = schemeEnd + 3;
        var pathStart = url.IndexOf('/', hostStart);
        return pathStart < 0 ? url : url[..pathStart];
    }

    private static string EnsureLeadingSlash(string s) => s.StartsWith('/') ? s : "/" + s;
}
