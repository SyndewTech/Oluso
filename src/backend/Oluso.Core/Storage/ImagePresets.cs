namespace Oluso.Core.Storage;

/// <summary>
/// A named image size preset for a display context (thumbnail, card, detail,
/// hero). Plain data — no image library dependency — so both the storefront
/// (URL building) and the resizer (actual work) share one source of truth.
/// </summary>
public sealed record ImagePreset(string Name, int MaxWidth, int Quality);

/// <summary>
/// The catalog of image presets and the helpers that turn a preset (or an
/// ad-hoc width) into Cloudflare Image Resizing's native option string. We
/// deliberately use Cloudflare's scheme rather than a custom format so that in
/// production Cloudflare resizes on demand at the edge; a local dev shim parses
/// the same options.
/// </summary>
public static class ImagePresets
{
    public static readonly ImagePreset Thumb = new("thumb", 96, 80);
    public static readonly ImagePreset Card = new("card", 400, 82);
    public static readonly ImagePreset Detail = new("detail", 800, 85);
    public static readonly ImagePreset Hero = new("hero", 1600, 80);

    public static readonly IReadOnlyList<ImagePreset> All = new[] { Thumb, Card, Detail, Hero };

    /// <summary>
    /// Ad-hoc widths the resize endpoint will honour outside the named presets.
    /// Fixed list so the cache cardinality is bounded and arbitrary widths can't
    /// be used to hammer the resizer.
    /// </summary>
    public static readonly IReadOnlyList<int> AllowedAdHocWidths = new[] { 48, 96, 200, 400, 800, 1200, 1600 };

    public static bool TryGet(string? name, out ImagePreset preset)
    {
        preset = Card;
        if (string.IsNullOrWhiteSpace(name)) return false;
        foreach (var p in All)
        {
            if (string.Equals(p.Name, name, StringComparison.OrdinalIgnoreCase))
            {
                preset = p;
                return true;
            }
        }
        return false;
    }

    /// <summary>Clamp an ad-hoc width up to the nearest allowed width (never larger than the max).</summary>
    public static int ClampWidth(int width)
    {
        foreach (var w in AllowedAdHocWidths)
            if (width <= w) return w;
        return AllowedAdHocWidths[^1];
    }

    /// <summary>
    /// Cloudflare Image Resizing options for a preset, e.g.
    /// <c>width=400,quality=82,format=auto,fit=scale-down</c>. <c>format=auto</c>
    /// lets Cloudflare pick WebP/AVIF per client; <c>fit=scale-down</c> never
    /// upscales.
    /// </summary>
    public static string ToCloudflareOptions(ImagePreset preset)
        => $"width={preset.MaxWidth},quality={preset.Quality},format=auto,fit=scale-down";

    public static string ToCloudflareOptions(int width, int quality = 82)
        => $"width={width},quality={quality},format=auto,fit=scale-down";
}
