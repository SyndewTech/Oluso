using System.Text.Json;
using System.Text.Json.Nodes;

namespace Oluso.Core.Events;

/// <summary>
/// Minimum-necessary handling of classified event data: which keys of an event's data are
/// confidential, and copies of the data with them removed for receivers not authorised to see them.
/// A restricted event releases only its declared non-sensitive properties (and the receiver's own
/// envelope keys); anything else on it, declared or added later, is withheld. An unrestricted event
/// withholds only the properties declared sensitive.
/// </summary>
public static class SensitiveEventData
{
    /// <summary>Keys the default webhook mapper adds to every payload's data.</summary>
    public static readonly IReadOnlySet<string> WebhookEnvelopeKeys =
        new HashSet<string>(["event_type", "timestamp"], StringComparer.OrdinalIgnoreCase);

    private static readonly JsonSerializerOptions SnakeCase = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower,
    };

    /// <summary>True when the event is restricted to holders of a permission.</summary>
    public static bool IsRestricted(this WebhookEventDefinition? definition) =>
        definition is { Sensitivity: DataSensitivity.Restricted };

    /// <summary>True when the event carries anything that must be withheld from unauthorised receivers.</summary>
    public static bool HasSensitiveData(this WebhookEventDefinition? definition) =>
        definition.IsRestricted() || definition?.Properties?.Any(p => p.Sensitive) == true;

    /// <summary>Names (snake_case, as in the data) of the event's declared sensitive properties.</summary>
    public static IReadOnlySet<string> SensitivePropertyNames(this WebhookEventDefinition? definition)
    {
        if (definition?.Properties == null) return new HashSet<string>();
        return definition.Properties.Where(p => p.Sensitive)
            .Select(p => p.Name)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);
    }

    /// <summary>
    /// Whether <paramref name="key"/> of the event's data is confidential. <paramref name="envelopeKeys"/>
    /// are the keys the receiving channel adds itself (ids, type, timestamp), never confidential.
    /// </summary>
    public static bool IsConfidential(this WebhookEventDefinition? definition, string key, IReadOnlySet<string> envelopeKeys)
    {
        if (definition == null) return false;
        var declared = definition.Properties?.FirstOrDefault(p => string.Equals(p.Name, key, StringComparison.OrdinalIgnoreCase));
        if (declared != null) return declared.Sensitive;
        return definition.IsRestricted() && !envelopeKeys.Contains(key);
    }

    /// <summary>
    /// A copy of <paramref name="data"/> without the keys <paramref name="withhold"/> picks. Dictionaries
    /// are copied key by key; any other object is serialised (snake_case) and copied as JSON.
    /// </summary>
    public static object Redact(object data, Func<string, bool> withhold, out IReadOnlyList<string> removed)
    {
        var dropped = new List<string>();
        removed = dropped;

        if (data is IDictionary<string, object?> dictionary)
        {
            var copy = new Dictionary<string, object?>(dictionary.Count, StringComparer.Ordinal);
            foreach (var (key, value) in dictionary)
            {
                if (withhold(key)) dropped.Add(key);
                else copy[key] = value;
            }
            return copy;
        }

        var node = JsonSerializer.SerializeToNode(data, data.GetType(), SnakeCase);
        if (node is JsonObject obj) RemoveFrom(obj, withhold, dropped);
        return node ?? data;
    }

    /// <summary>
    /// A serialised webhook payload with the keys <paramref name="withhold"/> picks removed from its
    /// <c>data</c> object and their names listed in <c>redacted_fields</c>. Returns the input when
    /// nothing was removed.
    /// </summary>
    public static string RedactPayloadJson(string payloadJson, Func<string, bool> withhold)
    {
        if (JsonNode.Parse(payloadJson) is not JsonObject root || root["data"] is not JsonObject data)
            return payloadJson;

        var dropped = new List<string>();
        RemoveFrom(data, withhold, dropped);
        if (dropped.Count == 0) return payloadJson;

        var already = root["redacted_fields"] as JsonArray;
        var names = (already?.Select(n => n?.GetValue<string>()).OfType<string>() ?? Enumerable.Empty<string>())
            .Concat(dropped)
            .Distinct(StringComparer.Ordinal)
            .ToList();
        root["redacted_fields"] = new JsonArray(names.Select(n => (JsonNode)JsonValue.Create(n)!).ToArray());
        return root.ToJsonString();
    }

    private static void RemoveFrom(JsonObject obj, Func<string, bool> withhold, List<string> dropped)
    {
        foreach (var key in obj.Select(p => p.Key).Where(withhold).ToList())
        {
            obj.Remove(key);
            dropped.Add(key);
        }
    }
}
