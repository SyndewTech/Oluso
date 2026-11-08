using System.Text.Json;

namespace Oluso.Core.UserJourneys;

/// <summary>
/// Finds the configuration fields of a journey step that hold secrets.
///
/// The step-type schema already declares them — a field rendered as a password
/// box carries <c>x-control: "secret-input"</c> (api_call's <c>clientSecret</c>,
/// <c>password</c>, <c>apiKey</c>, <c>token</c>; webhook's <c>secretKey</c>).
/// Deriving the list from the schema rather than a hand-kept list means a new
/// step type is protected the moment it declares its own secret fields.
///
/// Paths are dotted to reach nested objects, e.g.
/// <c>authentication.clientSecret</c>.
/// </summary>
public interface IJourneyStepSecretResolver
{
    /// <summary>
    /// Dotted paths of the secret-bearing fields for a step type. Empty when the
    /// type is unknown or declares none.
    /// </summary>
    IReadOnlyCollection<string> GetSecretPaths(string stepType);

    /// <summary>
    /// Reads the value at a dotted path from a step's configuration.
    /// </summary>
    string? ReadValue(IDictionary<string, object>? configuration, string path);

    /// <summary>
    /// Writes a value at a dotted path, creating intermediate objects as needed.
    /// A null value removes the key.
    /// </summary>
    void WriteValue(IDictionary<string, object> configuration, string path, string? value);
}

/// <inheritdoc />
public class JourneyStepSecretResolver : IJourneyStepSecretResolver
{
    private const string SecretControl = "secret-input";

    private readonly IExtendedStepHandlerRegistry? _registry;
    private readonly Dictionary<string, IReadOnlyCollection<string>> _cache = new(StringComparer.OrdinalIgnoreCase);
    private readonly object _gate = new();

    public JourneyStepSecretResolver(IExtendedStepHandlerRegistry? registry = null)
    {
        _registry = registry;
    }

    public IReadOnlyCollection<string> GetSecretPaths(string stepType)
    {
        if (string.IsNullOrEmpty(stepType) || _registry == null)
        {
            return Array.Empty<string>();
        }

        lock (_gate)
        {
            if (_cache.TryGetValue(stepType, out var cached))
            {
                return cached;
            }

            var info = _registry.GetRegisteredTypes()
                .FirstOrDefault(t => string.Equals(t.Type, stepType, StringComparison.OrdinalIgnoreCase));

            var paths = new List<string>();
            if (info?.ConfigurationSchema != null)
            {
                CollectSecretPaths(info.ConfigurationSchema, prefix: null, paths);
            }

            IReadOnlyCollection<string> result = paths;
            _cache[stepType] = result;
            return result;
        }
    }

    public string? ReadValue(IDictionary<string, object>? configuration, string path)
    {
        if (configuration == null)
        {
            return null;
        }

        var segments = path.Split('.');
        object? current = configuration;

        foreach (var segment in segments)
        {
            if (current is IDictionary<string, object> dict)
            {
                if (!dict.TryGetValue(segment, out current))
                {
                    return null;
                }
                continue;
            }

            if (current is JsonElement element && element.ValueKind == JsonValueKind.Object)
            {
                if (!element.TryGetProperty(segment, out var next))
                {
                    return null;
                }
                current = next;
                continue;
            }

            return null;
        }

        return current switch
        {
            null => null,
            string s => s,
            JsonElement { ValueKind: JsonValueKind.String } e => e.GetString(),
            _ => current.ToString()
        };
    }

    public void WriteValue(IDictionary<string, object> configuration, string path, string? value)
    {
        var segments = path.Split('.');
        var container = configuration;

        for (var i = 0; i < segments.Length - 1; i++)
        {
            var segment = segments[i];

            if (!container.TryGetValue(segment, out var next) || next == null)
            {
                if (value == null)
                {
                    return; // nothing to remove
                }
                var created = new Dictionary<string, object>();
                container[segment] = created;
                container = created;
                continue;
            }

            // The configuration round-trips through JSON, so an intermediate can
            // arrive as a JsonElement. Materialise it so it stays writable.
            container = next switch
            {
                IDictionary<string, object> dict => dict,
                JsonElement { ValueKind: JsonValueKind.Object } element => Materialise(container, segment, element),
                _ => container
            };
        }

        var leaf = segments[^1];
        if (value == null)
        {
            container.Remove(leaf);
        }
        else
        {
            container[leaf] = value;
        }
    }

    private static IDictionary<string, object> Materialise(
        IDictionary<string, object> parent, string key, JsonElement element)
    {
        var dict = new Dictionary<string, object>();
        foreach (var property in element.EnumerateObject())
        {
            dict[property.Name] = property.Value.ValueKind == JsonValueKind.String
                ? property.Value.GetString() ?? string.Empty
                : property.Value;
        }
        parent[key] = dict;
        return dict;
    }

    /// <summary>
    /// Walks a step's JSON-Schema-ish dictionary collecting the dotted paths of
    /// every field marked <c>x-control: secret-input</c>. Handles both the plain
    /// property form and nested objects that carry their own "properties" map.
    /// </summary>
    private static void CollectSecretPaths(object schemaNode, string? prefix, List<string> paths)
    {
        var properties = AsPropertyMap(schemaNode);
        if (properties == null)
        {
            return;
        }

        foreach (var (name, definition) in properties)
        {
            var path = prefix == null ? name : $"{prefix}.{name}";

            if (IsSecret(definition))
            {
                paths.Add(path);
                continue;
            }

            // Nested object: recurse through its own properties.
            var nested = GetMember(definition, "properties");
            if (nested != null)
            {
                CollectSecretPaths(nested, path, paths);
            }
        }
    }

    private static bool IsSecret(object? definition) =>
        GetMember(definition, "x-control") is string control &&
        string.Equals(control, SecretControl, StringComparison.OrdinalIgnoreCase);

    /// <summary>
    /// Schemas are built from anonymous objects and dictionaries alike, so the
    /// property bag has to be read either way.
    /// </summary>
    private static IEnumerable<KeyValuePair<string, object?>>? AsPropertyMap(object? node)
    {
        switch (node)
        {
            case null:
                return null;
            case IDictionary<string, object> dict:
                return dict.Select(kv => new KeyValuePair<string, object?>(kv.Key, kv.Value));
            default:
                var props = node.GetType().GetProperties();
                return props.Length == 0
                    ? null
                    : props.Select(p => new KeyValuePair<string, object?>(p.Name, p.GetValue(node)));
        }
    }

    private static object? GetMember(object? node, string name)
    {
        switch (node)
        {
            case null:
                return null;
            case IDictionary<string, object> dict:
                return dict.TryGetValue(name, out var value) ? value : null;
            default:
                // Anonymous types can't express "x-control", so those never carry
                // one — only the dictionary form does.
                return node.GetType().GetProperty(name)?.GetValue(node);
        }
    }
}
