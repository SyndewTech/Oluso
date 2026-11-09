using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using System.Xml;
using Microsoft.Extensions.Logging;
using Oluso.Core.Domain.Entities;
using Oluso.Imaging;

namespace Oluso.Admin.Services.ProviderIcons;

/// <summary>An icon we fetched, validated and re-encoded, ready to be stored.</summary>
public sealed record ProviderIconCandidate(string Source, string SourceUrl, string DataUrl, int Width, int Height, string SourceFormat);

public sealed record ProviderIconDiscoveryOutcome(string? Origin, IReadOnlyList<ProviderIconCandidate> Candidates, IReadOnlyList<string> Notes);

/// <summary>
/// Finds candidate icons for an identity provider from what its
/// configuration already tells us about the provider's web presence.
/// </summary>
public interface IProviderIconDiscoveryService
{
    Task<ProviderIconDiscoveryOutcome> DiscoverAsync(IdentityProvider provider, CancellationToken cancellationToken = default);
}

/// <summary>
/// Discovery order, most authoritative first:
/// <list type="number">
/// <item>SAML metadata <c>mdui:Logo</c> elements, when the provider has a metadata URL;</item>
/// <item><c>&lt;link rel="icon"&gt;</c> and <c>apple-touch-icon</c> on the provider's site root;</item>
/// <item><c>/apple-touch-icon.png</c> and <c>/favicon.ico</c> conventions.</item>
/// </list>
/// Every fetch goes through <see cref="IProviderIconFetcher"/> and every
/// image through <see cref="IIconImageNormalizer"/>; nothing the remote
/// site sent is returned, only PNGs we encoded from decoded pixels.
/// </summary>
public sealed partial class ProviderIconDiscoveryService : IProviderIconDiscoveryService
{
    public const int IconEdge = 128;
    private const int MaxDocumentBytes = 512 * 1024;
    private const int MaxImageBytes = 512 * 1024;
    private const int MaxImageFetches = 8;
    private const string MduiNamespace = "urn:oasis:names:tc:SAML:metadata:ui";

    private readonly IProviderIconFetcher _fetcher;
    private readonly IIconImageNormalizer _normalizer;
    private readonly ILogger<ProviderIconDiscoveryService> _logger;

    public ProviderIconDiscoveryService(
        IProviderIconFetcher fetcher,
        IIconImageNormalizer normalizer,
        ILogger<ProviderIconDiscoveryService> logger)
    {
        _fetcher = fetcher;
        _normalizer = normalizer;
        _logger = logger;
    }

    public async Task<ProviderIconDiscoveryOutcome> DiscoverAsync(IdentityProvider provider, CancellationToken cancellationToken = default)
    {
        var config = ReadConfiguration(provider.Properties);
        var notes = new List<string>();
        var sources = new List<(string Source, Uri Url)>();

        var origins = DeriveOrigins(provider.ProviderType, config).ToList();
        if (origins.Count == 0)
        {
            notes.Add("The provider configuration has no https URL to derive a site from.");
            return new ProviderIconDiscoveryOutcome(null, Array.Empty<ProviderIconCandidate>(), notes);
        }

        if (provider.ProviderType == ExternalProviderType.Saml2 && TryGetHttpsUri(GetString(config, "metadataUrl"), out var metadataUrl))
        {
            var metadata = await _fetcher.FetchAsync(metadataUrl, MaxDocumentBytes, cancellationToken);
            if (metadata.Success)
            {
                foreach (var logo in ParseSamlLogos(metadata.Bytes!))
                {
                    if (TryGetHttpsUri(logo, out var logoUrl))
                        sources.Add(("SAML metadata logo", logoUrl));
                    else
                        notes.Add($"Skipped metadata logo {logo}: not an https URL");
                }
            }
            else
            {
                notes.Add($"SAML metadata: {metadata.Error}");
            }
        }

        foreach (var origin in origins.Take(2))
        {
            var page = await _fetcher.FetchAsync(origin, MaxDocumentBytes, cancellationToken);
            if (page.Success && IsHtml(page.ContentType, page.Bytes!))
            {
                var baseUrl = page.FinalUrl ?? origin;
                foreach (var link in ParseIconLinks(Encoding.UTF8.GetString(page.Bytes!), baseUrl))
                {
                    if (link.Href.AbsolutePath.EndsWith(".svg", StringComparison.OrdinalIgnoreCase))
                    {
                        notes.Add($"Skipped {link.Href}: SVG icons are not accepted");
                        continue;
                    }

                    sources.Add((link.Source, link.Href));
                }
            }
            else if (!page.Success)
            {
                notes.Add($"Site root: {page.Error}");
            }

            sources.Add(("apple-touch-icon.png", new Uri(origin, "/apple-touch-icon.png")));
            sources.Add(("favicon.ico", new Uri(origin, "/favicon.ico")));
        }

        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var hashes = new HashSet<string>(StringComparer.Ordinal);
        var candidates = new List<ProviderIconCandidate>();
        var fetches = 0;

        foreach (var (source, url) in sources)
        {
            if (!seen.Add(url.AbsoluteUri))
                continue;
            if (fetches++ >= MaxImageFetches)
            {
                notes.Add($"Stopped after {MaxImageFetches} image fetches.");
                break;
            }

            var fetched = await _fetcher.FetchAsync(url, MaxImageBytes, cancellationToken);
            if (!fetched.Success)
            {
                notes.Add($"{source}: {fetched.Error}");
                continue;
            }

            var normalized = _normalizer.Normalize(fetched.Bytes!, IconEdge, out var rejection);
            if (normalized == null)
            {
                notes.Add($"Rejected {url}: {rejection}");
                continue;
            }

            if (!hashes.Add(Convert.ToHexString(SHA256.HashData(normalized.Png))))
                continue;

            candidates.Add(new ProviderIconCandidate(source, url.AbsoluteUri, normalized.ToDataUrl(), normalized.Width, normalized.Height, normalized.SourceFormat));
        }

        var ordered = candidates
            .OrderByDescending(c => Math.Max(c.Width, c.Height))
            .ThenBy(c => c.Source)
            .ToList();

        _logger.LogInformation("Provider icon discovery for {Scheme}: {Count} candidate(s) from {Origin}", provider.Scheme, ordered.Count, origins[0]);
        return new ProviderIconDiscoveryOutcome(origins[0].GetLeftPart(UriPartial.Authority), ordered, notes);
    }

    /// <summary>
    /// Site roots worth probing, from the provider's own configuration for
    /// generic types and from the well-known login hosts for branded ones.
    /// </summary>
    public static IEnumerable<Uri> DeriveOrigins(ExternalProviderType type, IReadOnlyDictionary<string, string?> config)
    {
        var raw = new List<string?>();
        switch (type)
        {
            case ExternalProviderType.Google:
                raw.Add("https://accounts.google.com/");
                break;
            case ExternalProviderType.Microsoft:
                raw.Add(GetString(config, "authority"));
                raw.Add("https://login.microsoftonline.com/");
                break;
            case ExternalProviderType.GitHub:
                raw.Add("https://github.com/");
                break;
            case ExternalProviderType.Facebook:
                raw.Add("https://www.facebook.com/");
                break;
            case ExternalProviderType.Apple:
                raw.Add("https://appleid.apple.com/");
                break;
            case ExternalProviderType.LinkedIn:
                raw.Add("https://www.linkedin.com/");
                break;
            case ExternalProviderType.Twitter:
                raw.Add("https://x.com/");
                break;
            case ExternalProviderType.Oidc:
                raw.Add(GetString(config, "authority"));
                raw.Add(GetString(config, "metadataUrl"));
                raw.Add(GetString(config, "authorizationEndpoint"));
                break;
            case ExternalProviderType.OAuth2:
                raw.Add(GetString(config, "authorizationEndpoint"));
                raw.Add(GetString(config, "tokenEndpoint"));
                break;
            case ExternalProviderType.Saml2:
                raw.Add(GetString(config, "metadataUrl"));
                raw.Add(GetString(config, "singleSignOnServiceUrl"));
                raw.Add(GetString(config, "entityId"));
                break;
        }

        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var value in raw)
        {
            if (!TryGetHttpsUri(value, out var uri))
                continue;
            var origin = new Uri(uri.GetLeftPart(UriPartial.Authority) + "/");
            if (seen.Add(origin.AbsoluteUri))
                yield return origin;
        }
    }

    public readonly record struct IconLink(string Source, Uri Href, string? Sizes);

    [GeneratedRegex(@"<link\b[^>]*>", RegexOptions.IgnoreCase | RegexOptions.Singleline)]
    private static partial Regex LinkTag();

    [GeneratedRegex(@"([a-zA-Z][\w:-]*)\s*=\s*(?:""([^""]*)""|'([^']*)'|([^\s""'>]+))", RegexOptions.Singleline)]
    private static partial Regex Attribute();

    /// <summary>
    /// Icon links declared in an HTML head, resolved against the page URL.
    /// Deliberately a shallow scan rather than a DOM parse: we only need
    /// link tags, and a tolerant scan cannot be made to execute anything.
    /// </summary>
    public static IReadOnlyList<IconLink> ParseIconLinks(string html, Uri baseUrl)
    {
        var results = new List<IconLink>();
        var headEnd = html.IndexOf("</head", StringComparison.OrdinalIgnoreCase);
        var scope = headEnd > 0 ? html[..headEnd] : html;

        foreach (Match tag in LinkTag().Matches(scope))
        {
            string? rel = null, href = null, sizes = null;
            foreach (Match attr in Attribute().Matches(tag.Value))
            {
                var name = attr.Groups[1].Value.ToLowerInvariant();
                var value = attr.Groups[2].Success ? attr.Groups[2].Value
                    : attr.Groups[3].Success ? attr.Groups[3].Value
                    : attr.Groups[4].Value;
                switch (name)
                {
                    case "rel": rel = value; break;
                    case "href": href = value; break;
                    case "sizes": sizes = value; break;
                }
            }

            if (rel == null || string.IsNullOrWhiteSpace(href))
                continue;

            var tokens = rel.Split(' ', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
                .Select(t => t.ToLowerInvariant())
                .ToHashSet();

            string? source = null;
            if (tokens.Contains("apple-touch-icon") || tokens.Contains("apple-touch-icon-precomposed"))
                source = "apple-touch-icon link";
            else if (tokens.Contains("icon"))
                source = "icon link";
            if (source == null)
                continue;

            var decoded = System.Net.WebUtility.HtmlDecode(href.Trim());
            if (!Uri.TryCreate(baseUrl, decoded, out var resolved))
                continue;
            if (!string.Equals(resolved.Scheme, Uri.UriSchemeHttps, StringComparison.OrdinalIgnoreCase))
                continue;

            results.Add(new IconLink(source, resolved, sizes));
        }

        return results;
    }

    /// <summary>
    /// <c>mdui:Logo</c> URLs from SAML metadata. DTDs and external entities are
    /// refused; the reader never resolves anything.
    /// </summary>
    public static IReadOnlyList<string> ParseSamlLogos(byte[] xml)
    {
        var logos = new List<string>();
        var settings = new XmlReaderSettings
        {
            DtdProcessing = DtdProcessing.Prohibit,
            XmlResolver = null,
            MaxCharactersFromEntities = 0,
            MaxCharactersInDocument = 4 * 1024 * 1024,
            IgnoreComments = true,
            IgnoreProcessingInstructions = true,
        };

        try
        {
            using var stream = new MemoryStream(xml);
            using var reader = XmlReader.Create(stream, settings);
            while (reader.Read())
            {
                if (reader.NodeType == XmlNodeType.Element && reader.LocalName == "Logo" && reader.NamespaceURI == MduiNamespace)
                {
                    var value = reader.ReadElementContentAsString().Trim();
                    if (value.Length > 0)
                        logos.Add(value);
                }
            }
        }
        catch (XmlException)
        {
            return logos;
        }

        return logos;
    }

    private static bool IsHtml(string? contentType, byte[] bytes)
    {
        if (contentType != null && contentType.Contains("html", StringComparison.OrdinalIgnoreCase))
            return true;
        if (contentType != null)
            return false;
        var head = Encoding.ASCII.GetString(bytes, 0, Math.Min(bytes.Length, 512));
        return head.Contains("<html", StringComparison.OrdinalIgnoreCase) || head.Contains("<!doctype html", StringComparison.OrdinalIgnoreCase);
    }

    private static bool TryGetHttpsUri(string? value, out Uri uri)
    {
        uri = null!;
        if (string.IsNullOrWhiteSpace(value))
            return false;
        if (!Uri.TryCreate(value.Trim(), UriKind.Absolute, out var parsed))
            return false;
        if (!string.Equals(parsed.Scheme, Uri.UriSchemeHttps, StringComparison.OrdinalIgnoreCase))
            return false;
        uri = parsed;
        return true;
    }

    private static string? GetString(IReadOnlyDictionary<string, string?> config, string key) =>
        config.TryGetValue(key, out var value) && !string.IsNullOrWhiteSpace(value) ? value : null;

    public static IReadOnlyDictionary<string, string?> ReadConfiguration(string? propertiesJson)
    {
        var result = new Dictionary<string, string?>(StringComparer.OrdinalIgnoreCase);
        if (string.IsNullOrWhiteSpace(propertiesJson))
            return result;

        try
        {
            using var doc = JsonDocument.Parse(propertiesJson);
            if (doc.RootElement.ValueKind != JsonValueKind.Object)
                return result;
            foreach (var property in doc.RootElement.EnumerateObject())
            {
                result[property.Name] = property.Value.ValueKind switch
                {
                    JsonValueKind.String => property.Value.GetString(),
                    JsonValueKind.Null or JsonValueKind.Undefined => null,
                    _ => property.Value.ToString(),
                };
            }
        }
        catch (JsonException)
        {
        }

        return result;
    }
}
