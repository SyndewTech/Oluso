using System.Net;
using System.Text.Json;

namespace Oluso.Enterprise.Verification.Providers;

/// <summary>Shared reading of vendor HTTP answers.</summary>
internal static class ProviderHttp
{
    /// <summary>
    /// The outcome an unsuccessful status means. 4xx are final (never retried):
    /// 400/422 malformed, 401/402/403 credentials, wallet or product access. 408, 424,
    /// 429 and 5xx mean the vendor or the registry behind it couldn't answer.
    /// </summary>
    public static BusinessLookupOutcome Classify(HttpStatusCode status) => (int)status switch
    {
        400 or 422 => BusinessLookupOutcome.InvalidRequest,
        401 or 402 or 403 => BusinessLookupOutcome.Unauthorized,
        404 => BusinessLookupOutcome.NotFound,
        _ => BusinessLookupOutcome.ProviderUnavailable,
    };

    public static JsonElement? Parse(string body)
    {
        if (string.IsNullOrWhiteSpace(body)) return null;
        try
        {
            using var doc = JsonDocument.Parse(body);
            return doc.RootElement.Clone();
        }
        catch (JsonException)
        {
            return null;
        }
    }

    /// <summary>The vendor's error text, from the first of these properties that holds a string.</summary>
    public static string? ErrorText(JsonElement? root, params string[] properties)
    {
        if (root is not { ValueKind: JsonValueKind.Object } obj) return null;
        foreach (var name in properties)
        {
            if (String(obj, name) is { } text) return text;
        }
        return null;
    }

    public static string? String(JsonElement element, string property)
    {
        if (element.ValueKind != JsonValueKind.Object || !element.TryGetProperty(property, out var value)) return null;
        var text = value.ValueKind switch
        {
            JsonValueKind.String => value.GetString(),
            JsonValueKind.Number => value.GetRawText(),
            _ => null,
        };
        return string.IsNullOrWhiteSpace(text) ? null : text.Trim();
    }

    public static IEnumerable<JsonElement> Array(JsonElement element, string property) =>
        element.ValueKind == JsonValueKind.Object
        && element.TryGetProperty(property, out var value)
        && value.ValueKind == JsonValueKind.Array
            ? value.EnumerateArray()
            : Enumerable.Empty<JsonElement>();

    public static string Describe(HttpStatusCode status, string? error) =>
        error == null ? $"{(int)status} {status}" : $"{(int)status} {status}: {error}";
}

/// <summary>Reads a registry's status text as trading or not.</summary>
public static class RegistryStatus
{
    private static readonly string[] InactiveWords =
    {
        "INACTIVE", "NOT ACTIVE", "STRUCK", "DISSOLV", "WOUND", "WINDING", "LIQUIDAT", "DELIST", "CLOSED", "CEASED", "DEREGISTER", "STRIKE OFF",
    };

    /// <summary>
    /// "ACTIVE" (any case, surrounding spaces ignored) is trading; text containing an
    /// inactive word (inactive, not active, struck off, dissolved, wound up, in
    /// liquidation, delisted, closed, ceased, deregistered) is not; anything else,
    /// including no status, is unknown.
    /// </summary>
    public static BusinessActivityStatus Parse(string? status)
    {
        if (string.IsNullOrWhiteSpace(status)) return BusinessActivityStatus.Unknown;
        var text = status.Trim().ToUpperInvariant().Replace('_', ' ');
        if (InactiveWords.Any(text.Contains)) return BusinessActivityStatus.Inactive;
        return text == "ACTIVE" ? BusinessActivityStatus.Active : BusinessActivityStatus.Unknown;
    }
}
