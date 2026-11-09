using System.Globalization;
using System.Text.Json;

namespace Oluso.Core.UserJourneys;

/// <summary>
/// Reads values out of JSON written by people and other systems: journey step settings,
/// partner API responses and journey data. Names match exactly first and otherwise without
/// regard to case, and numbers and booleans are also accepted as text ("200", "true"), so a
/// setting written in another style is read rather than silently ignored or thrown on.
/// </summary>
public static class JsonLookup
{
    public static bool TryGetPropertyIgnoreCase(this JsonElement element, string name, out JsonElement value)
    {
        value = default;
        if (element.ValueKind != JsonValueKind.Object) return false;
        if (element.TryGetProperty(name, out value)) return true;

        foreach (var property in element.EnumerateObject())
        {
            if (string.Equals(property.Name, name, StringComparison.OrdinalIgnoreCase))
            {
                value = property.Value;
                return true;
            }
        }
        return false;
    }

    /// <summary>The value as text: strings as-is, numbers and booleans as written; null when absent.</summary>
    public static string? GetStringSetting(this JsonElement element, string name) =>
        element.TryGetPropertyIgnoreCase(name, out var value) ? AsString(value) : null;

    public static bool GetBoolSetting(this JsonElement element, string name, bool defaultValue = false)
    {
        if (!element.TryGetPropertyIgnoreCase(name, out var value)) return defaultValue;
        return value.ValueKind switch
        {
            JsonValueKind.True => true,
            JsonValueKind.False => false,
            JsonValueKind.String when bool.TryParse(value.GetString(), out var parsed) => parsed,
            _ => defaultValue,
        };
    }

    public static int? GetIntSetting(this JsonElement element, string name)
    {
        if (!element.TryGetPropertyIgnoreCase(name, out var value)) return null;
        return value.ValueKind switch
        {
            JsonValueKind.Number when value.TryGetInt32(out var number) => number,
            JsonValueKind.String when int.TryParse(value.GetString(), NumberStyles.Integer, CultureInfo.InvariantCulture, out var parsed) => parsed,
            _ => null,
        };
    }

    public static string? AsString(JsonElement value) => value.ValueKind switch
    {
        JsonValueKind.String => value.GetString(),
        JsonValueKind.Number or JsonValueKind.True or JsonValueKind.False => value.GetRawText(),
        _ => null,
    };
}
