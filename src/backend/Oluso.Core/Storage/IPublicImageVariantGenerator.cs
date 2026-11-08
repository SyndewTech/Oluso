namespace Oluso.Core.Storage;

/// <summary>
/// Pre-generates fixed-size WebP variants for a freshly uploaded public image
/// and stores them next to the original (see <see cref="ImageVariantPaths"/>).
/// Runs once at upload time (an admin action) so reads can be served as static
/// files straight from blob/CDN — the app never resizes on read and no CDN-side
/// resizing is required.
///
/// <para>The interface lives in Core (no image-library dependency) so any module
/// can request variant generation; the implementation (SkiaSharp) lives in
/// <c>Oluso.Imaging</c> and is wired via <c>AddImageResizing()</c>.</para>
/// </summary>
public interface IPublicImageVariantGenerator
{
    Task GenerateAsync(string originalStoragePath, byte[] originalBytes, CancellationToken cancellationToken = default);
}
