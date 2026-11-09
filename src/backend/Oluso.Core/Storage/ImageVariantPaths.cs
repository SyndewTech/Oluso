namespace Oluso.Core.Storage;

/// <summary>
/// Convention for pre-generated image variants. A variant lives in a folder
/// named after the original (extension stripped), as <c>{preset}.webp</c>:
/// original <c>…/{imageId}.jpg</c> → variant <c>…/{imageId}/card.webp</c>.
///
/// These are static files served DIRECTLY from blob/CDN (or the anonymous raw
/// endpoint in dev) — the app is never in the image byte path on read, and no
/// CDN-side resizing is required.
/// </summary>
public static class ImageVariantPaths
{
    /// <summary>Storage key for a variant of the given original storage path.</summary>
    public static string VariantStoragePath(string originalPath, string presetName)
        => $"{Stem(originalPath)}/{presetName}.webp";

    /// <summary>The variant URL derived from an original public URL (same transform on the path).</summary>
    public static string VariantUrl(string originalUrl, string presetName)
    {
        // Split off any query/fragment, transform the path, re-attach.
        var cut = originalUrl.IndexOfAny(new[] { '?', '#' });
        var pathPart = cut >= 0 ? originalUrl[..cut] : originalUrl;
        var tail = cut >= 0 ? originalUrl[cut..] : string.Empty;
        return $"{Stem(pathPart)}/{presetName}.webp{tail}";
    }

    /// <summary>The original string minus its file extension (last dot after the last slash).</summary>
    private static string Stem(string s)
    {
        var lastSlash = s.LastIndexOf('/');
        var lastDot = s.LastIndexOf('.');
        return lastDot > lastSlash ? s[..lastDot] : s;
    }
}
