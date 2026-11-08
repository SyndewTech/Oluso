using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.RegularExpressions;

namespace Oluso.Enterprise.Scim.Services;

/// <summary>
/// Reads and writes SCIM attribute paths on a JSON resource: simple
/// (<c>displayName</c>), complex (<c>name.givenName</c>), filtered
/// multi-valued (<c>emails[type eq "work"].value</c>), whole multi-valued
/// (<c>emails</c>, <c>emails.value</c>) and extension-qualified
/// (<c>urn:...:2.0:User:department</c>). Paths are matched without regard to case,
/// as RFC 7643 requires.
/// </summary>
public static partial class ScimAttributePath
{
    [GeneratedRegex(@"^(?<attr>[A-Za-z][A-Za-z0-9_]*)\[(?<fattr>[A-Za-z][A-Za-z0-9_]*)\s+eq\s+""?(?<fval>[^""\]]+?)""?\](?:\.(?<sub>[A-Za-z][A-Za-z0-9_.]*))?$")]
    private static partial Regex FilteredPath();

    /// <summary>Splits an extension-qualified path into (schema URN, attribute path); null when not extension-qualified.</summary>
    public static (string Schema, string Path)? SplitExtension(string path)
    {
        if (!path.StartsWith("urn:", StringComparison.OrdinalIgnoreCase))
            return null;

        var separator = path.LastIndexOf(':');
        if (separator <= 0 || separator == path.Length - 1)
            return null;

        return (path[..separator], path[(separator + 1)..]);
    }

    /// <summary>
    /// Reads a scalar at the path as a string. Multi-valued reads are lenient
    /// the way identity providers need them to be: a filter that matches
    /// nothing falls back to the primary element, then the first, because
    /// Entra and Okta routinely send an email with no <c>type</c>.
    /// </summary>
    public static string? Read(JsonObject resource, string path)
    {
        var node = ReadNode(resource, path);
        return node == null ? null : Scalar(node);
    }

    public static JsonNode? ReadNode(JsonObject resource, string path)
    {
        var extension = SplitExtension(path);
        if (extension is { } ext)
        {
            return FindProperty(resource, ext.Schema) is JsonObject extObject ? ReadNode(extObject, ext.Path) : null;
        }

        var filtered = FilteredPath().Match(path);
        if (filtered.Success)
        {
            var array = FindProperty(resource, filtered.Groups["attr"].Value) as JsonArray;
            if (array == null)
                return null;

            var element = MatchElement(array, filtered.Groups["fattr"].Value, filtered.Groups["fval"].Value)
                          ?? PreferredElement(array);
            var sub = filtered.Groups["sub"].Value;
            return element == null ? null : (string.IsNullOrEmpty(sub) ? element : ReadNode(element, sub));
        }

        var dot = path.IndexOf('.');
        if (dot > 0)
        {
            var head = FindProperty(resource, path[..dot]);
            var rest = path[(dot + 1)..];
            return head switch
            {
                JsonObject child => ReadNode(child, rest),
                JsonArray array => PreferredElement(array) is { } element ? ReadNode(element, rest) : null,
                _ => null,
            };
        }

        var value = FindProperty(resource, path);
        return value is JsonArray plain ? (PreferredElement(plain) is { } e ? (e["value"] ?? e) : plain.FirstOrDefault()) : value;
    }

    /// <summary>
    /// Writes a value at the path, creating intermediate objects and
    /// multi-valued elements as needed. A null value removes the attribute.
    /// </summary>
    public static void Write(JsonObject resource, string path, JsonNode? value)
    {
        var extension = SplitExtension(path);
        if (extension is { } ext)
        {
            var key = FindKey(resource, ext.Schema) ?? ext.Schema;
            if (resource[key] is not JsonObject extObject)
            {
                if (value == null)
                    return;
                extObject = new JsonObject();
                resource[key] = extObject;
                AddSchema(resource, ext.Schema);
            }

            Write(extObject, ext.Path, value);
            return;
        }

        var filtered = FilteredPath().Match(path);
        if (filtered.Success)
        {
            var attrKey = FindKey(resource, filtered.Groups["attr"].Value) ?? filtered.Groups["attr"].Value;
            if (resource[attrKey] is not JsonArray array)
            {
                if (value == null)
                    return;
                array = new JsonArray();
                resource[attrKey] = array;
            }

            var filterAttr = filtered.Groups["fattr"].Value;
            var filterValue = filtered.Groups["fval"].Value;
            var sub = filtered.Groups["sub"].Value;
            var element = MatchElement(array, filterAttr, filterValue);

            if (string.IsNullOrEmpty(sub))
            {
                if (element != null)
                    array.Remove(element);
                if (value is JsonObject replacement)
                {
                    var clone = (JsonObject)replacement.DeepClone();
                    if (clone[filterAttr] == null)
                        clone[filterAttr] = bool.TryParse(filterValue, out var b) ? b : filterValue;
                    array.Add(clone);
                }
                return;
            }

            if (element == null)
            {
                if (value == null)
                    return;
                element = new JsonObject { [filterAttr] = bool.TryParse(filterValue, out var b) ? b : filterValue };
                if (array.Count == 0 && !string.Equals(filterAttr, "primary", StringComparison.OrdinalIgnoreCase))
                    element["primary"] = true;
                array.Add(element);
            }

            Write(element, sub, value);
            return;
        }

        var dot = path.IndexOf('.');
        if (dot > 0)
        {
            var headName = path[..dot];
            var rest = path[(dot + 1)..];
            var headKey = FindKey(resource, headName) ?? headName;
            switch (resource[headKey])
            {
                case JsonObject child:
                    Write(child, rest, value);
                    return;
                case JsonArray array:
                {
                    var element = PreferredElement(array);
                    if (element == null)
                    {
                        if (value == null)
                            return;
                        element = new JsonObject { ["primary"] = true };
                        array.Add(element);
                    }

                    Write(element, rest, value);
                    return;
                }
                default:
                    if (value == null)
                        return;
                    var created = new JsonObject();
                    resource[headKey] = created;
                    Write(created, rest, value);
                    return;
            }
        }

        var existingKey = FindKey(resource, path);
        if (value == null)
        {
            if (existingKey != null)
                resource.Remove(existingKey);
            return;
        }

        resource[existingKey ?? path] = value.DeepClone();
    }

    /// <summary>True when <paramref name="candidate"/> is the same attribute as, or nested under, <paramref name="root"/>.</summary>
    public static bool IsUnder(string candidate, string root)
    {
        if (candidate.Equals(root, StringComparison.OrdinalIgnoreCase))
            return true;
        if (!candidate.StartsWith(root, StringComparison.OrdinalIgnoreCase) || candidate.Length <= root.Length)
            return false;
        var next = candidate[root.Length];
        return next is '.' or '[' or ':';
    }

    public static string? Scalar(JsonNode node) => node switch
    {
        JsonValue value => value.GetValue<JsonElement>() switch
        {
            { ValueKind: JsonValueKind.String } e => e.GetString(),
            { ValueKind: JsonValueKind.True } => "true",
            { ValueKind: JsonValueKind.False } => "false",
            { ValueKind: JsonValueKind.Null } => null,
            var e => e.ToString(),
        },
        JsonObject obj => obj["value"] is { } inner ? Scalar(inner) : null,
        _ => null,
    };

    private static JsonObject? MatchElement(JsonArray array, string filterAttr, string filterValue) =>
        array.OfType<JsonObject>().FirstOrDefault(e =>
            FindProperty(e, filterAttr) is JsonValue fv && string.Equals(Scalar(fv), filterValue, StringComparison.OrdinalIgnoreCase));

    private static JsonObject? PreferredElement(JsonArray array)
    {
        var objects = array.OfType<JsonObject>().ToList();
        return objects.FirstOrDefault(e => FindProperty(e, "primary") is JsonValue p && string.Equals(Scalar(p), "true", StringComparison.OrdinalIgnoreCase))
               ?? objects.FirstOrDefault();
    }

    private static string? FindKey(JsonObject obj, string name) =>
        obj.FirstOrDefault(kv => string.Equals(kv.Key, name, StringComparison.OrdinalIgnoreCase)).Key;

    private static JsonNode? FindProperty(JsonObject obj, string name)
    {
        var key = FindKey(obj, name);
        return key == null ? null : obj[key];
    }

    private static void AddSchema(JsonObject resource, string schema)
    {
        if (resource["schemas"] is not JsonArray schemas)
        {
            schemas = new JsonArray();
            resource["schemas"] = schemas;
        }

        if (!schemas.Any(s => string.Equals(s?.GetValue<string>(), schema, StringComparison.OrdinalIgnoreCase)))
            schemas.Add(schema);
    }
}
