using Oluso.Core.Storage;

namespace Oluso.Imaging;

/// <summary>
/// Resizes raster images. Used by the local/dev public-image shim that mirrors
/// Cloudflare Image Resizing — in production Cloudflare does this at the edge and
/// this service is never hit. Vector images (SVG) are not handled here; callers
/// pass those through unresized. The default implementation is
/// <see cref="SkiaImageResizer"/> (MIT-licensed); ImageSharp is avoided for
/// licensing reasons.
/// </summary>
public interface IImageResizer
{
    ResizedImage Resize(byte[] source, ImagePreset preset);
    ResizedImage Resize(byte[] source, int maxWidth, int quality);
}

/// <summary>A resized image: the encoded bytes, content type, and final dimensions.</summary>
public sealed record ResizedImage(byte[] Bytes, string ContentType, int Width, int Height);
