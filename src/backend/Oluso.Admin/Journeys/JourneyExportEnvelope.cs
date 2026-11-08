using System.IO.Compression;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using Oluso.Admin.Dto.Journeys;

namespace Oluso.Admin.Journeys;

/// <summary>
/// The on-disk form of an exported journey: a header line, a checksum line and
/// the gzipped, base64-encoded document. Not encryption, just opaque to a casual
/// reader and tamper-evident on import. Plain JSON documents are accepted too,
/// so a file kept in a repo or pasted into a ticket still imports.
/// </summary>
public static class JourneyExportEnvelope
{
    public const string Header = "OLUSO-JOURNEY/1";
    public const string FileExtension = ".journey";
    public const string ContentType = "application/octet-stream";

    private const string ChecksumPrefix = "sha256:";

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        PropertyNameCaseInsensitive = true,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
        WriteIndented = false
    };

    public static string Wrap(JourneyExportDocument document)
    {
        var json = JsonSerializer.SerializeToUtf8Bytes(document, JsonOptions);
        var checksum = Convert.ToHexString(SHA256.HashData(json)).ToLowerInvariant();

        using var buffer = new MemoryStream();
        using (var gzip = new GZipStream(buffer, CompressionLevel.SmallestSize, leaveOpen: true))
        {
            gzip.Write(json);
        }

        return string.Join('\n', Header, ChecksumPrefix + checksum, Convert.ToBase64String(buffer.ToArray())) + '\n';
    }

    public static bool TryUnwrap(string content, out JourneyExportDocument? document, out string? error)
    {
        document = null;
        error = null;

        if (string.IsNullOrWhiteSpace(content))
        {
            error = "The file is empty.";
            return false;
        }

        var trimmed = content.Trim();
        byte[] json;

        if (trimmed.StartsWith('{'))
        {
            json = Encoding.UTF8.GetBytes(trimmed);
        }
        else
        {
            var lines = trimmed.Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
            if (lines.Length != 3 || lines[0] != Header)
            {
                error = "This is not a journey export file.";
                return false;
            }
            if (!lines[1].StartsWith(ChecksumPrefix, StringComparison.Ordinal))
            {
                error = "The journey export file is missing its checksum.";
                return false;
            }

            try
            {
                using var input = new MemoryStream(Convert.FromBase64String(lines[2]));
                using var gzip = new GZipStream(input, CompressionMode.Decompress);
                using var output = new MemoryStream();
                gzip.CopyTo(output);
                json = output.ToArray();
            }
            catch (Exception ex) when (ex is FormatException or InvalidDataException)
            {
                error = "The journey export file is corrupted.";
                return false;
            }

            var expected = lines[1][ChecksumPrefix.Length..];
            var actual = Convert.ToHexString(SHA256.HashData(json)).ToLowerInvariant();
            if (!string.Equals(expected, actual, StringComparison.OrdinalIgnoreCase))
            {
                error = "The journey export file has been modified and cannot be imported.";
                return false;
            }
        }

        JourneyExportDocument? parsed;
        try
        {
            parsed = JsonSerializer.Deserialize<JourneyExportDocument>(json, JsonOptions);
        }
        catch (JsonException)
        {
            error = "The journey export file could not be read.";
            return false;
        }

        if (parsed == null || parsed.Format != JourneyExportDocument.CurrentFormat)
        {
            error = "This is not a journey export file.";
            return false;
        }
        if (parsed.Version > JourneyExportDocument.CurrentVersion)
        {
            error = $"This journey was exported by a newer version (format {parsed.Version}); upgrade before importing.";
            return false;
        }
        if (parsed.Policy == null || string.IsNullOrWhiteSpace(parsed.Policy.Name) || string.IsNullOrWhiteSpace(parsed.Policy.Type))
        {
            error = "The journey export file has no policy in it.";
            return false;
        }

        document = parsed;
        return true;
    }

    /// <summary>File name for a policy: lower-case slug plus the extension.</summary>
    public static string FileNameFor(string policyName)
    {
        var slug = new StringBuilder();
        foreach (var ch in policyName.Trim().ToLowerInvariant())
        {
            if (char.IsLetterOrDigit(ch)) slug.Append(ch);
            else if (slug.Length > 0 && slug[^1] != '-') slug.Append('-');
        }
        var name = slug.ToString().Trim('-');
        return (name.Length == 0 ? "journey" : name) + FileExtension;
    }
}
