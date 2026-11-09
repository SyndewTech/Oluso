using System.Text.Json;

namespace Oluso.Core.UserJourneys;

/// <summary>
/// Steps a step's own settings send the journey to. A setting is a step target when its
/// schema picks from the journey's steps (<c>x-enumSource.endpoint = "$steps"</c>, the
/// builder's step picker), so any step type that declares one is covered.
/// </summary>
public static class JourneyStepReferences
{
    public const string StepsEnumSource = "$steps";

    public static IEnumerable<(string Setting, string Target)> FromSettings(object? schema, IDictionary<string, object>? settings)
    {
        if (schema == null || settings == null || settings.Count == 0) return [];

        JsonElement schemaJson, settingsJson;
        try
        {
            schemaJson = JsonSerializer.SerializeToElement(schema);
            settingsJson = JsonSerializer.SerializeToElement(settings);
        }
        catch (Exception)
        {
            return [];
        }

        var found = new List<(string, string)>();
        // A step's schema is its map of settings; nested objects carry their own "properties".
        Collect(schemaJson, settingsJson, null, found);
        return found;
    }

    private static void Collect(JsonElement properties, JsonElement value, string? path, List<(string, string)> found)
    {
        if (properties.ValueKind != JsonValueKind.Object || value.ValueKind != JsonValueKind.Object)
        {
            return;
        }

        foreach (var property in properties.EnumerateObject())
        {
            if (!value.TryGetPropertyIgnoreCase(property.Name, out var setting)) continue;
            var settingPath = path == null ? property.Name : $"{path}.{property.Name}";
            var definition = property.Value;

            if (IsStepPicker(definition))
            {
                if (JsonLookup.AsString(setting) is { Length: > 0 } target) found.Add((settingPath, target));
                continue;
            }

            if (setting.ValueKind == JsonValueKind.Array
                && definition.TryGetPropertyIgnoreCase("items", out var items) && items.ValueKind == JsonValueKind.Object)
            {
                foreach (var item in setting.EnumerateArray())
                {
                    if (IsStepPicker(items))
                    {
                        if (JsonLookup.AsString(item) is { Length: > 0 } target) found.Add((settingPath, target));
                    }
                    else if (items.TryGetPropertyIgnoreCase("properties", out var itemProperties))
                    {
                        Collect(itemProperties, item, settingPath, found);
                    }
                }
                continue;
            }

            if (definition.TryGetPropertyIgnoreCase("properties", out var nested))
            {
                Collect(nested, setting, settingPath, found);
            }
        }
    }

    private static bool IsStepPicker(JsonElement definition) =>
        definition.TryGetPropertyIgnoreCase("x-enumSource", out var source)
        && string.Equals(source.GetStringSetting("endpoint"), StepsEnumSource, StringComparison.Ordinal);
}
