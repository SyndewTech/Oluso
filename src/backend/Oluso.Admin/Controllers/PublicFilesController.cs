using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Logging;
using Oluso.Core.Storage;
using Oluso.Imaging;

namespace Oluso.Admin.Controllers;

/// <summary>
/// Anonymous serving of PUBLIC assets (storefront product/content images,
/// profile pictures). This is the local/dev path and a fallback — in production
/// public assets are served as direct blob/CDN URLs and resized at the
/// Cloudflare edge, so this controller is not on the hot path there.
///
/// <para><b>Resize URLs use Cloudflare Image Resizing's native scheme</b>
/// (<c>/cdn-cgi/image/{options}/{source}</c>), not a custom format — so the
/// exact same URL the storefront emits is resized by Cloudflare in production
/// and by the local shim below in development. Cloudflare intercepts
/// <c>/cdn-cgi/</c> at the edge, so this action is never reached in prod.</para>
///
/// <para><b>Security:</b> this endpoint shares the singleton
/// <see cref="IFileUploader"/> with the private layer. A server-side download is
/// NOT gated by SAS, so the path guard (<see cref="IPublicAssetPathRegistry.IsPublic"/>)
/// is mandatory — it hard-refuses any path that is not an allowlisted public
/// prefix, so a private blob can never be read here. On Azure, public
/// reads additionally resolve against the dedicated public container, so the
/// private container is unreachable from this path regardless.</para>
/// </summary>
[ApiController]
[AllowAnonymous]
public sealed class PublicFilesController : ControllerBase
{
    private readonly IFileUploader _fileUploader;
    private readonly IImageResizer _resizer;
    private readonly IPublicAssetPathRegistry _publicAssetPaths;
    private readonly ILogger<PublicFilesController> _logger;

    public PublicFilesController(
        IFileUploader fileUploader,
        IImageResizer resizer,
        IPublicAssetPathRegistry publicAssetPaths,
        ILogger<PublicFilesController> logger)
    {
        _fileUploader = fileUploader;
        _resizer = resizer;
        _publicAssetPaths = publicAssetPaths;
        _logger = logger;
    }

    /// <summary>Serve a public file as-is (no resizing). The stored "original" URL.</summary>
    [HttpGet("/api/files/raw/{**path}")]
    public async Task<IActionResult> GetRaw(string path, CancellationToken cancellationToken)
    {
        if (!_publicAssetPaths.IsPublic(path)) return NotFound();

        var bytes = await _fileUploader.DownloadAsync(path, FileAccessLevel.Public, cancellationToken);
        if (bytes is null) return NotFound();

        SetImmutableCache();
        return File(bytes, GetContentType(path));
    }

    /// <summary>
    /// Local/dev shim for Cloudflare Image Resizing. Handles
    /// <c>/cdn-cgi/image/{options}/{source}</c> where options is Cloudflare's own
    /// comma list (<c>width=400,quality=82,format=auto,fit=scale-down</c>) and
    /// source is one of our public originals. SVGs are served unresized. Results
    /// are cached back to public storage so repeat hits don't reprocess.
    /// </summary>
    [HttpGet("/cdn-cgi/image/{**rest}")]
    public async Task<IActionResult> GetResized(string rest, CancellationToken cancellationToken)
    {
        // Split "<options>/<source>" on the first slash.
        var slash = rest.IndexOf('/');
        if (slash <= 0) return NotFound();

        var options = rest[..slash];
        var source = rest[(slash + 1)..];

        var storagePath = ExtractStoragePath(source);
        if (storagePath is null || !_publicAssetPaths.IsPublic(storagePath)) return NotFound();

        // SVG is vector — never rasterize; serve as-is.
        if (storagePath.EndsWith(".svg", StringComparison.OrdinalIgnoreCase))
            return await GetRaw(storagePath, cancellationToken);

        var (width, quality) = ParseOptions(options);
        if (width <= 0) return await GetRaw(storagePath, cancellationToken);
        width = ImagePresets.ClampWidth(width);

        var cachePath = $"cache/w{width}q{quality}/{storagePath}.webp";

        if (await _fileUploader.ExistsAsync(cachePath, FileAccessLevel.Public, cancellationToken))
        {
            var cached = await _fileUploader.DownloadAsync(cachePath, FileAccessLevel.Public, cancellationToken);
            if (cached is not null)
            {
                SetImmutableCache();
                return File(cached, "image/webp");
            }
        }

        var original = await _fileUploader.DownloadAsync(storagePath, FileAccessLevel.Public, cancellationToken);
        if (original is null) return NotFound();

        ResizedImage resized;
        try
        {
            resized = _resizer.Resize(original, width, quality);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Resize failed for {Path}; serving original", storagePath);
            SetImmutableCache();
            return File(original, GetContentType(storagePath));
        }

        // Best-effort cache write — never fail the response if caching can't run.
        try
        {
            await _fileUploader.UploadAsync(cachePath, resized.Bytes, FileAccessLevel.Public, "image/webp", cancellationToken: cancellationToken);
        }
        catch (Exception ex)
        {
            _logger.LogDebug(ex, "Could not cache resized image {CachePath}", cachePath);
        }

        SetImmutableCache();
        return File(resized.Bytes, "image/webp");
    }

    /// <summary>
    /// Recover the storage path from a Cloudflare image source, which may be an
    /// absolute URL or a relative path pointing at <c>/api/files/raw/{path}</c>.
    /// Returns null if it isn't one of our public originals.
    /// </summary>
    private static string? ExtractStoragePath(string source)
    {
        // Source may be absolute or relative; after route capture the leading
        // slash is gone, so match the marker without one.
        const string marker = "api/files/raw/";
        var idx = source.IndexOf(marker, StringComparison.OrdinalIgnoreCase);
        if (idx < 0) return null;
        var path = source[(idx + marker.Length)..];
        // Drop any querystring/fragment.
        var cut = path.IndexOfAny(new[] { '?', '#' });
        return cut >= 0 ? path[..cut] : path;
    }

    /// <summary>Parse Cloudflare options we honour (width, quality); ignore the rest.</summary>
    private static (int width, int quality) ParseOptions(string options)
    {
        int width = 0, quality = 82;
        foreach (var token in options.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
        {
            var eq = token.IndexOf('=');
            if (eq <= 0) continue;
            var key = token[..eq];
            var value = token[(eq + 1)..];
            if (key.Equals("width", StringComparison.OrdinalIgnoreCase) && int.TryParse(value, out var w)) width = w;
            else if (key.Equals("quality", StringComparison.OrdinalIgnoreCase) && int.TryParse(value, out var q) && q is >= 1 and <= 100) quality = q;
        }
        return (width, quality);
    }

    private void SetImmutableCache()
        => Response.Headers.CacheControl = "public, max-age=31536000, immutable";

    private static string GetContentType(string path) =>
        Path.GetExtension(path).ToLowerInvariant() switch
        {
            ".png" => "image/png",
            ".jpg" or ".jpeg" => "image/jpeg",
            ".gif" => "image/gif",
            ".webp" => "image/webp",
            ".svg" => "image/svg+xml",
            _ => "application/octet-stream",
        };
}
