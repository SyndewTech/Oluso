using Microsoft.Extensions.Logging;
using Oluso.Core.Storage;

namespace Oluso.Imaging;

/// <summary>
/// SkiaSharp-backed <see cref="IPublicImageVariantGenerator"/>. Resizes once per
/// preset at upload time and stores each variant as a static WebP next to the
/// original, so reads are served directly from blob/CDN with no server or CDN
/// resizing involved.
/// </summary>
public sealed class PublicImageVariantGenerator : IPublicImageVariantGenerator
{
    private readonly IFileUploader _fileUploader;
    private readonly IImageResizer _resizer;
    private readonly ILogger<PublicImageVariantGenerator> _logger;

    public PublicImageVariantGenerator(
        IFileUploader fileUploader,
        IImageResizer resizer,
        ILogger<PublicImageVariantGenerator> logger)
    {
        _fileUploader = fileUploader;
        _resizer = resizer;
        _logger = logger;
    }

    public async Task GenerateAsync(string originalStoragePath, byte[] originalBytes, CancellationToken cancellationToken = default)
    {
        // SVG is vector — no raster variants.
        if (originalStoragePath.EndsWith(".svg", StringComparison.OrdinalIgnoreCase))
            return;

        foreach (var preset in ImagePresets.All)
        {
            try
            {
                var resized = _resizer.Resize(originalBytes, preset);
                var variantPath = ImageVariantPaths.VariantStoragePath(originalStoragePath, preset.Name);
                var result = await _fileUploader.UploadAsync(
                    variantPath, resized.Bytes, FileAccessLevel.Public, "image/webp", cancellationToken: cancellationToken);

                if (!result.Success)
                    _logger.LogWarning("Failed to store image variant {Variant}: {Error}", variantPath, result.Error);
            }
            catch (Exception ex)
            {
                // Best-effort: a failed variant must not fail the upload. The
                // original is still served; storefront falls back to it.
                _logger.LogWarning(ex, "Could not generate '{Preset}' variant for {Path}", preset.Name, originalStoragePath);
            }
        }
    }
}
