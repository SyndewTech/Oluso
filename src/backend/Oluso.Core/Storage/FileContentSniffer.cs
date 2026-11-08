namespace Oluso.Core.Storage;

/// <summary>A file format recognised from its leading bytes, with the extensions and media type it is stored under.</summary>
public sealed record SniffedFileType(string Kind, string MediaType, IReadOnlyList<string> Extensions);

/// <summary>
/// Recognises uploaded documents and photos by their magic bytes. The declared content
/// type and the file name are never trusted: a file is what its bytes say it is, or it
/// is refused. Only formats that can't carry script when served are recognised (no SVG,
/// HTML or XML).
/// </summary>
public static class FileContentSniffer
{
    public const string Pdf = "pdf";
    public const string Png = "png";
    public const string Jpeg = "jpeg";
    public const string Gif = "gif";
    public const string Webp = "webp";
    public const string Heic = "heic";

    /// <summary>Bytes needed to recognise every supported format.</summary>
    public const int HeaderLength = 16;

    public static readonly IReadOnlyDictionary<string, SniffedFileType> Types = new Dictionary<string, SniffedFileType>(StringComparer.OrdinalIgnoreCase)
    {
        [Pdf] = new(Pdf, "application/pdf", new[] { ".pdf" }),
        [Png] = new(Png, "image/png", new[] { ".png" }),
        [Jpeg] = new(Jpeg, "image/jpeg", new[] { ".jpg", ".jpeg" }),
        [Gif] = new(Gif, "image/gif", new[] { ".gif" }),
        [Webp] = new(Webp, "image/webp", new[] { ".webp" }),
        [Heic] = new(Heic, "image/heic", new[] { ".heic", ".heif" }),
    };

    public static SniffedFileType? Sniff(ReadOnlySpan<byte> header)
    {
        if (header.StartsWith("%PDF-"u8)) return Types[Pdf];
        if (header.StartsWith(new byte[] { 0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A })) return Types[Png];
        if (header.StartsWith(new byte[] { 0xFF, 0xD8, 0xFF })) return Types[Jpeg];
        if (header.StartsWith("GIF87a"u8) || header.StartsWith("GIF89a"u8)) return Types[Gif];
        if (header.Length >= 12 && header[..4].SequenceEqual("RIFF"u8) && header[8..12].SequenceEqual("WEBP"u8)) return Types[Webp];
        if (header.Length >= 12 && header[4..8].SequenceEqual("ftyp"u8))
        {
            var brand = header[8..12];
            if (brand.SequenceEqual("heic"u8) || brand.SequenceEqual("heix"u8) || brand.SequenceEqual("mif1"u8) || brand.SequenceEqual("msf1"u8))
                return Types[Heic];
        }
        return null;
    }

    /// <summary>Reads the header from a seekable or fresh stream and rewinds it when it can.</summary>
    public static async Task<SniffedFileType?> SniffAsync(Stream content, CancellationToken cancellationToken = default)
    {
        var buffer = new byte[HeaderLength];
        var read = 0;
        while (read < buffer.Length)
        {
            var n = await content.ReadAsync(buffer.AsMemory(read), cancellationToken);
            if (n == 0) break;
            read += n;
        }
        if (content.CanSeek) content.Seek(0, SeekOrigin.Begin);
        return Sniff(buffer.AsSpan(0, read));
    }
}
