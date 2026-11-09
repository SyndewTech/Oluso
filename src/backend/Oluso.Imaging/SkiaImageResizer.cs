using Oluso.Core.Storage;
using SkiaSharp;

namespace Oluso.Imaging;

/// <summary>
/// <see cref="IImageResizer"/> backed by SkiaSharp (MIT-licensed). Preserves
/// aspect ratio, never upscales, and encodes WebP (matching Cloudflare's
/// <c>format=auto</c> on modern clients). Stateless / thread-safe → singleton.
///
/// <para>ImageSharp is deliberately avoided: its v3 Six Labors Split License is
/// not compatible with commercial redistribution. The <see cref="IImageResizer"/>
/// seam lets a licensed ImageSharp implementation be substituted if desired.</para>
/// </summary>
public sealed class SkiaImageResizer : IImageResizer
{
    public ResizedImage Resize(byte[] source, ImagePreset preset)
        => Resize(source, preset.MaxWidth, preset.Quality);

    public ResizedImage Resize(byte[] source, int maxWidth, int quality)
    {
        using var original = SKBitmap.Decode(source)
            ?? throw new InvalidOperationException("Unsupported or corrupt image.");

        var targetWidth = Math.Min(maxWidth, original.Width);
        SKBitmap resized = original;
        try
        {
            if (targetWidth < original.Width)
            {
                var scale = (double)targetWidth / original.Width;
                var targetHeight = Math.Max(1, (int)Math.Round(original.Height * scale));
                resized = original.Resize(new SKImageInfo(targetWidth, targetHeight), SKFilterQuality.High)
                    ?? throw new InvalidOperationException("Image resize failed.");
            }

            using var image = SKImage.FromBitmap(resized);
            using var data = image.Encode(SKEncodedImageFormat.Webp, quality);
            return new ResizedImage(data.ToArray(), "image/webp", resized.Width, resized.Height);
        }
        finally
        {
            if (!ReferenceEquals(resized, original)) resized.Dispose();
        }
    }
}
