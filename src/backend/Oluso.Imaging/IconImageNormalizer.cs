using System.Buffers.Binary;
using SkiaSharp;

namespace Oluso.Imaging;

/// <summary>
/// Turns untrusted bytes claimed to be an icon into a small PNG we made
/// ourselves, or rejects them. The input is never stored or served: it is
/// sniffed by magic bytes (the declared content type and file extension are
/// ignored), decoded, redrawn and re-encoded, so anything that is not a
/// plain raster image is discarded. SVG is rejected outright because it is
/// a document format that can carry script and external references.
/// </summary>
public interface IIconImageNormalizer
{
    /// <summary>
    /// Returns a normalised PNG no larger than <paramref name="maxEdge"/> on
    /// either side, or null with a reason when the bytes are not an
    /// acceptable raster image.
    /// </summary>
    NormalizedIcon? Normalize(ReadOnlySpan<byte> source, int maxEdge, out string? rejection);
}

/// <summary>A PNG we encoded, plus the dimensions it was encoded at and the format it was decoded from.</summary>
public sealed record NormalizedIcon(byte[] Png, int Width, int Height, string SourceFormat)
{
    public string ToDataUrl() => "data:image/png;base64," + Convert.ToBase64String(Png);
}

public sealed class IconImageNormalizer : IIconImageNormalizer
{
    /// <summary>Hard cap on input bytes regardless of what the caller asks for.</summary>
    public const int MaxInputBytes = 1024 * 1024;

    public const string DataUrlPrefix = "data:image/png;base64,";

    public NormalizedIcon? Normalize(ReadOnlySpan<byte> source, int maxEdge, out string? rejection)
    {
        rejection = null;
        if (source.Length == 0)
        {
            rejection = "empty";
            return null;
        }

        if (source.Length > MaxInputBytes)
        {
            rejection = $"larger than {MaxInputBytes / 1024} KB";
            return null;
        }

        var format = Sniff(source);
        if (format == null)
        {
            rejection = "not a PNG, JPEG, GIF, WebP, BMP or ICO image";
            return null;
        }

        byte[] decodable = source.ToArray();
        if (format == "ico")
        {
            decodable = ExtractLargestIcoEntry(source, out var icoRejection) ?? Array.Empty<byte>();
            if (decodable.Length == 0)
            {
                rejection = icoRejection ?? "ICO contains no decodable image";
                return null;
            }
        }

        // SKCodec.Create returns null for bytes no decoder recognises, whereas
        // SKBitmap.Decode(byte[]) throws on the same input.
        using var data = SKData.CreateCopy(decodable);
        using var codec = SKCodec.Create(data);
        using var bitmap = codec == null ? null : SKBitmap.Decode(codec);
        if (bitmap == null || bitmap.Width <= 0 || bitmap.Height <= 0)
        {
            rejection = $"declared {format} but could not be decoded";
            return null;
        }

        if (bitmap.Width > 4096 || bitmap.Height > 4096)
        {
            rejection = "image dimensions too large";
            return null;
        }

        var scale = Math.Min(1.0, (double)maxEdge / Math.Max(bitmap.Width, bitmap.Height));
        var width = Math.Max(1, (int)Math.Round(bitmap.Width * scale));
        var height = Math.Max(1, (int)Math.Round(bitmap.Height * scale));

        // Redraw onto a fresh surface: the output carries only pixels we
        // painted, never chunks, metadata or trailing data from the input.
        var info = new SKImageInfo(width, height, SKColorType.Rgba8888, SKAlphaType.Premul);
        using var surface = SKSurface.Create(info);
        if (surface == null)
        {
            rejection = "could not allocate image surface";
            return null;
        }

        surface.Canvas.Clear(SKColors.Transparent);
        using (var paint = new SKPaint { FilterQuality = SKFilterQuality.High, IsAntialias = true })
        using (var image = SKImage.FromBitmap(bitmap))
        {
            surface.Canvas.DrawImage(image, new SKRect(0, 0, width, height), paint);
        }

        using var snapshot = surface.Snapshot();
        using var encoded = snapshot.Encode(SKEncodedImageFormat.Png, 100);
        if (encoded == null || encoded.Size == 0)
        {
            rejection = "could not encode PNG";
            return null;
        }

        return new NormalizedIcon(encoded.ToArray(), width, height, format);
    }

    /// <summary>Identifies the container by its leading bytes. Returns null for anything unrecognised.</summary>
    public static string? Sniff(ReadOnlySpan<byte> bytes)
    {
        if (bytes.Length >= 8 && bytes[0] == 0x89 && bytes[1] == 0x50 && bytes[2] == 0x4E && bytes[3] == 0x47
            && bytes[4] == 0x0D && bytes[5] == 0x0A && bytes[6] == 0x1A && bytes[7] == 0x0A)
            return "png";

        if (bytes.Length >= 3 && bytes[0] == 0xFF && bytes[1] == 0xD8 && bytes[2] == 0xFF)
            return "jpeg";

        if (bytes.Length >= 6 && bytes[0] == 'G' && bytes[1] == 'I' && bytes[2] == 'F' && bytes[3] == '8'
            && (bytes[4] == '7' || bytes[4] == '9') && bytes[5] == 'a')
            return "gif";

        if (bytes.Length >= 12 && bytes[0] == 'R' && bytes[1] == 'I' && bytes[2] == 'F' && bytes[3] == 'F'
            && bytes[8] == 'W' && bytes[9] == 'E' && bytes[10] == 'B' && bytes[11] == 'P')
            return "webp";

        if (bytes.Length >= 2 && bytes[0] == 'B' && bytes[1] == 'M')
            return "bmp";

        if (bytes.Length >= 6 && bytes[0] == 0 && bytes[1] == 0 && bytes[2] == 1 && bytes[3] == 0
            && BinaryPrimitives.ReadUInt16LittleEndian(bytes[4..6]) is > 0 and <= 64)
            return "ico";

        return null;
    }

    /// <summary>
    /// Picks the largest entry of an ICO directory. PNG-compressed entries are
    /// returned as-is; DIB entries are wrapped in a BMP header (with the
    /// doubled XOR/AND height corrected) so the standard BMP decoder can read them.
    /// </summary>
    internal static byte[]? ExtractLargestIcoEntry(ReadOnlySpan<byte> ico, out string? rejection)
    {
        rejection = null;
        var count = BinaryPrimitives.ReadUInt16LittleEndian(ico[4..6]);
        if (ico.Length < 6 + count * 16)
        {
            rejection = "ICO directory is truncated";
            return null;
        }

        var bestArea = -1;
        var bestOffset = 0;
        var bestSize = 0;
        var bestWidth = 0;
        var bestHeight = 0;
        for (var i = 0; i < count; i++)
        {
            var entry = ico.Slice(6 + i * 16, 16);
            int width = entry[0] == 0 ? 256 : entry[0];
            int height = entry[1] == 0 ? 256 : entry[1];
            var size = (int)BinaryPrimitives.ReadUInt32LittleEndian(entry[8..12]);
            var offset = (int)BinaryPrimitives.ReadUInt32LittleEndian(entry[12..16]);
            if (size <= 0 || offset <= 0 || offset + size > ico.Length)
                continue;

            var area = width * height;
            if (area > bestArea)
            {
                bestArea = area;
                bestOffset = offset;
                bestSize = size;
                bestWidth = width;
                bestHeight = height;
            }
        }

        if (bestArea < 0)
        {
            rejection = "ICO has no usable entries";
            return null;
        }

        var payload = ico.Slice(bestOffset, bestSize);
        if (Sniff(payload) == "png")
            return payload.ToArray();

        // DIB: BITMAPINFOHEADER with biHeight = 2 * height (XOR + AND masks).
        if (payload.Length < 40)
        {
            rejection = "ICO entry is neither PNG nor a bitmap";
            return null;
        }

        var headerSize = (int)BinaryPrimitives.ReadUInt32LittleEndian(payload[0..4]);
        if (headerSize != 40)
        {
            rejection = "ICO entry uses an unsupported bitmap header";
            return null;
        }

        var bitCount = BinaryPrimitives.ReadUInt16LittleEndian(payload[14..16]);
        var colorsUsed = (int)BinaryPrimitives.ReadUInt32LittleEndian(payload[32..36]);
        var paletteEntries = bitCount <= 8 ? (colorsUsed == 0 ? 1 << bitCount : colorsUsed) : 0;
        var pixelOffset = 14 + 40 + paletteEntries * 4;

        var bmp = new byte[14 + payload.Length];
        bmp[0] = (byte)'B';
        bmp[1] = (byte)'M';
        BinaryPrimitives.WriteUInt32LittleEndian(bmp.AsSpan(2, 4), (uint)bmp.Length);
        BinaryPrimitives.WriteUInt32LittleEndian(bmp.AsSpan(10, 4), (uint)pixelOffset);
        payload.CopyTo(bmp.AsSpan(14));

        // Correct the doubled height; the AND mask that follows the XOR
        // bitmap is simply ignored by the BMP decoder.
        BinaryPrimitives.WriteInt32LittleEndian(bmp.AsSpan(14 + 8, 4), bestHeight);
        BinaryPrimitives.WriteInt32LittleEndian(bmp.AsSpan(14 + 4, 4), bestWidth);
        return bmp;
    }
}
