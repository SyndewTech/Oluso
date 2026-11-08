using System.Globalization;
using System.Text.Json;
using System.Text.Json.Nodes;
using Oluso.Core.Domain.Entities;
using Oluso.Enterprise.Scim.Entities;
using Oluso.Enterprise.Scim.Models;
using Oluso.Enterprise.Scim.Outbound;

namespace Oluso.Enterprise.Scim.Services;

/// <summary>
/// An inbound request could not be mapped onto a user: a required
/// attribute is missing, or a configured expression failed. Surfaces to the
/// identity provider as a 400 <c>invalidValue</c>.
/// </summary>
public sealed class ScimInboundMappingException : Exception
{
    public ScimInboundMappingException(string message) : base(message)
    {
    }
}

public enum ScimInboundMode
{
    /// <summary>POST: every mapping applies; required ones must resolve.</summary>
    Create,

    /// <summary>PUT: every mapping applies; a mapping that resolves to nothing clears its target, except the identity anchors.</summary>
    Replace,

    /// <summary>PATCH: only values that resolve are written, plus explicit removals.</summary>
    Patch,
}

/// <summary>
/// Internal user properties an inbound mapping may write. <c>custom:&lt;key&gt;</c>
/// writes into the user's custom properties.
/// </summary>
public static class ScimInboundTargets
{
    public const string UserName = "UserName";
    public const string Email = "Email";
    public const string FirstName = "FirstName";
    public const string LastName = "LastName";
    public const string DisplayName = "DisplayName";
    public const string PhoneNumber = "PhoneNumber";
    public const string Locale = "Locale";
    public const string TimeZone = "TimeZone";
    public const string IsActive = "IsActive";
    public const string ProfilePictureUrl = "ProfilePictureUrl";
    public const string CustomPrefix = "custom:";

    public static readonly string[] All =
    {
        UserName, Email, FirstName, LastName, DisplayName, PhoneNumber, Locale, TimeZone, IsActive, ProfilePictureUrl
    };

    public static bool IsKnown(string property) =>
        All.Any(p => string.Equals(p, property, StringComparison.OrdinalIgnoreCase))
        || (property.StartsWith(CustomPrefix, StringComparison.OrdinalIgnoreCase) && property.Length > CustomPrefix.Length);
}

/// <summary>
/// The mappings used when a client has configured none. They reproduce the
/// fixed behaviour the module had before mappings were honoured, so existing
/// clients see no change. Two rows target DisplayName: the explicit attribute
/// wins by priority and the name parts are the fallback.
/// </summary>
public static class ScimInboundDefaultMappings
{
    public static IReadOnlyList<ScimAttributeMapping> Rows { get; } = new List<ScimAttributeMapping>
    {
        new() { ScimAttribute = "userName", InternalProperty = ScimInboundTargets.UserName, IsRequired = true, Priority = 100 },
        new() { ScimAttribute = "name.givenName", InternalProperty = ScimInboundTargets.FirstName },
        new() { ScimAttribute = "name.familyName", InternalProperty = ScimInboundTargets.LastName },
        new() { ScimAttribute = "displayName", InternalProperty = ScimInboundTargets.DisplayName, Priority = 10 },
        new() { ScimAttribute = "name.formatted", InternalProperty = ScimInboundTargets.DisplayName, Expression = "{{ scim.name?.givenName }} {{ scim.name?.familyName }}", Priority = 0 },
        new() { ScimAttribute = "emails[type eq \"work\"].value", InternalProperty = ScimInboundTargets.Email },
        new() { ScimAttribute = "phoneNumbers[type eq \"work\"].value", InternalProperty = ScimInboundTargets.PhoneNumber },
        new() { ScimAttribute = "locale", InternalProperty = ScimInboundTargets.Locale },
        new() { ScimAttribute = "timezone", InternalProperty = ScimInboundTargets.TimeZone },
        new() { ScimAttribute = "active", InternalProperty = ScimInboundTargets.IsActive },
    };
}

/// <summary>Result of a dry run of the inbound mappings against a sample resource.</summary>
public sealed class ScimInboundPreview
{
    public bool Success { get; init; }
    public string? Error { get; init; }

    /// <summary>Internal property name to the value that would be written; null values mean "cleared".</summary>
    public IReadOnlyDictionary<string, string?> Fields { get; init; } = new Dictionary<string, string?>();
}

/// <summary>
/// Applies a client's inbound attribute mappings to a user. One code path
/// for POST, PUT and PATCH: PATCH first applies its operations to the
/// user's current SCIM representation, then maps like a replace that only
/// writes what resolved.
/// </summary>
public sealed class ScimInboundMapper
{
    private static readonly JsonSerializerOptions JsonOptions = new() { DefaultIgnoreCondition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull };

    private readonly ScimExpressionEngine _expressions;

    public ScimInboundMapper(ScimExpressionEngine expressions)
    {
        _expressions = expressions;
    }

    public static JsonObject ToJson(ScimUser scimUser) =>
        JsonSerializer.SerializeToNode(scimUser, JsonOptions) as JsonObject ?? new JsonObject();

    public void Apply(
        OlusoUser user,
        JsonObject resource,
        ScimClient client,
        IReadOnlyList<ScimAttributeMapping> mappings,
        ScimInboundMode mode,
        IReadOnlySet<string>? removedPaths = null)
    {
        var resolved = Resolve(resource, client, mappings, mode, removedPaths);
        var custom = ReadCustom(user);
        var customChanged = false;

        foreach (var (property, outcome) in resolved)
        {
            if (mode == ScimInboundMode.Patch && !outcome.Present && !outcome.Removed)
                continue;

            if (property.StartsWith(ScimInboundTargets.CustomPrefix, StringComparison.OrdinalIgnoreCase))
            {
                var key = property[ScimInboundTargets.CustomPrefix.Length..];
                if (outcome.Value == null)
                    customChanged |= custom.Remove(key);
                else
                {
                    custom[key] = outcome.Value;
                    customChanged = true;
                }

                continue;
            }

            AssignBuiltIn(user, property, outcome, mode);
        }

        if (customChanged)
            user.CustomPropertiesJson = JsonSerializer.Serialize(custom);

        user.UpdatedAt = DateTime.UtcNow;
    }

    public ScimInboundPreview Preview(JsonObject resource, ScimClient client, IReadOnlyList<ScimAttributeMapping> mappings, ScimInboundMode mode)
    {
        try
        {
            var resolved = Resolve(resource, client, mappings, mode, null);
            var fields = resolved
                .Where(kv => mode != ScimInboundMode.Patch || kv.Value.Present)
                .ToDictionary(kv => kv.Key, kv => kv.Value.Value, StringComparer.OrdinalIgnoreCase);
            return new ScimInboundPreview { Success = true, Fields = fields };
        }
        catch (ScimInboundMappingException ex)
        {
            return new ScimInboundPreview { Success = false, Error = ex.Message };
        }
    }

    private readonly record struct Outcome(string? Value, bool Present, bool Removed);

    /// <summary>
    /// One outcome per target property. Mappings for the same target are
    /// tried by descending priority; the first that yields a value wins.
    /// </summary>
    private Dictionary<string, Outcome> Resolve(
        JsonObject resource,
        ScimClient client,
        IReadOnlyList<ScimAttributeMapping> mappings,
        ScimInboundMode mode,
        IReadOnlySet<string>? removedPaths)
    {
        var effective = mappings.Where(m => m.IsEnabled).ToList();
        if (effective.Count == 0)
            effective = ScimInboundDefaultMappings.Rows.ToList();

        var globals = new Lazy<Scriban.Runtime.ScriptObject>(() => ScimExpressionEngine.BuildInboundGlobals(resource, client.Id, client.Name));
        var outcomes = new Dictionary<string, Outcome>(StringComparer.OrdinalIgnoreCase);

        foreach (var group in effective.GroupBy(m => m.InternalProperty.Trim(), StringComparer.OrdinalIgnoreCase))
        {
            var property = group.Key;
            if (string.IsNullOrEmpty(property))
                continue;

            string? value = null;
            var present = false;
            var removed = false;
            var required = false;

            foreach (var mapping in group.OrderByDescending(m => m.Priority))
            {
                required |= mapping.IsRequired;

                if (removedPaths != null && removedPaths.Any(r => ScimAttributePath.IsUnder(mapping.ScimAttribute, r)))
                    removed = true;

                if (!string.IsNullOrWhiteSpace(mapping.Condition)
                    && !Guard(() => _expressions.Evaluate(mapping.Condition, globals.Value, $"Condition on '{mapping.ScimAttribute}'")))
                    continue;

                string? candidate;
                if (!string.IsNullOrWhiteSpace(mapping.Expression))
                    candidate = Guard(() => _expressions.Render(mapping.Expression, globals.Value, $"Expression for '{mapping.ScimAttribute}'"));
                else
                    candidate = ScimAttributePath.Read(resource, mapping.ScimAttribute);

                candidate = ApplyTransformation(candidate, mapping.Transformation);
                if (string.IsNullOrEmpty(candidate))
                    candidate = string.IsNullOrEmpty(mapping.DefaultValue) ? null : mapping.DefaultValue;

                if (!string.IsNullOrEmpty(candidate))
                {
                    value = candidate;
                    present = true;
                    break;
                }
            }

            if (required && !present && mode == ScimInboundMode.Create)
                throw new ScimInboundMappingException($"{group.First().ScimAttribute} is required");

            outcomes[property] = new Outcome(value, present, removed);
        }

        return outcomes;
    }

    private static void AssignBuiltIn(OlusoUser user, string property, Outcome outcome, ScimInboundMode mode)
    {
        var value = outcome.Value;
        switch (property.ToLowerInvariant())
        {
            case "username":
                // Identity anchor: never cleared by an omission.
                if (!string.IsNullOrEmpty(value))
                {
                    user.UserName = value;
                    user.NormalizedUserName = value.ToUpperInvariant();
                }
                break;
            case "email":
                if (!string.IsNullOrEmpty(value))
                {
                    // A new address hasn't been proven; the provisioning service trusts it only at a domain the
                    // tenant has verified. A casing-only change is the same mailbox and keeps its state.
                    if (!string.Equals(user.Email, value, StringComparison.OrdinalIgnoreCase))
                    {
                        user.EmailConfirmed = false;
                    }
                    user.Email = value;
                    user.NormalizedEmail = value.ToUpperInvariant();
                }
                break;
            case "isactive":
                // Omitted on create means active (RFC 7643); omitted on replace or patch leaves the flag alone.
                if (value != null && TryParseBool(value, out var active))
                    user.IsActive = active;
                else if (mode == ScimInboundMode.Create && value == null)
                    user.IsActive = true;
                break;
            case "firstname":
                user.FirstName = value;
                break;
            case "lastname":
                user.LastName = value;
                break;
            case "displayname":
                user.DisplayName = value;
                break;
            case "phonenumber":
                user.PhoneNumber = value;
                break;
            case "locale":
                user.Locale = value;
                break;
            case "timezone":
                user.TimeZone = value;
                break;
            case "profilepictureurl":
                user.ProfilePictureUrl = value;
                break;
            default:
                throw new ScimInboundMappingException($"Unknown internal property '{property}' in attribute mapping");
        }
    }

    private static bool TryParseBool(string value, out bool result)
    {
        if (bool.TryParse(value, out result))
            return true;
        switch (value.Trim().ToLowerInvariant())
        {
            case "1" or "yes" or "y" or "active" or "enabled":
                result = true;
                return true;
            case "0" or "no" or "n" or "inactive" or "disabled":
                result = false;
                return true;
        }

        return false;
    }

    private static Dictionary<string, string> ReadCustom(OlusoUser user)
    {
        if (string.IsNullOrWhiteSpace(user.CustomPropertiesJson))
            return new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

        try
        {
            return JsonSerializer.Deserialize<Dictionary<string, string>>(user.CustomPropertiesJson) is { } dict
                ? new Dictionary<string, string>(dict, StringComparer.OrdinalIgnoreCase)
                : new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        }
        catch (JsonException)
        {
            return new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        }
    }

    private static T Guard<T>(Func<T> action)
    {
        try
        {
            return action();
        }
        catch (ScimExpressionException ex)
        {
            throw new ScimInboundMappingException(ex.Message);
        }
    }

    internal static string? ApplyTransformation(string? value, string? transformation)
    {
        if (value == null)
            return null;

        switch ((transformation ?? ScimOutboundTransformations.None).ToLowerInvariant())
        {
            case ScimOutboundTransformations.None:
            case "":
                return value;
            case ScimOutboundTransformations.Lowercase:
                return value.ToLowerInvariant();
            case ScimOutboundTransformations.Uppercase:
                return value.ToUpperInvariant();
            case ScimOutboundTransformations.Trim:
                return value.Trim();
            case ScimOutboundTransformations.NormalizeEmail:
                return value.Trim().ToLowerInvariant();
            case ScimOutboundTransformations.NormalizePhone:
            {
                var digits = new string(value.Where(char.IsDigit).ToArray());
                return value.TrimStart().StartsWith('+') ? "+" + digits : digits;
            }
            case ScimOutboundTransformations.ExtractDomain:
            {
                var at = value.LastIndexOf('@');
                return at >= 0 && at < value.Length - 1 ? value[(at + 1)..] : null;
            }
            case ScimOutboundTransformations.FormatName:
                return CultureInfo.InvariantCulture.TextInfo.ToTitleCase(value.Trim().ToLowerInvariant());
            default:
                throw new ScimInboundMappingException($"Unknown transformation '{transformation}' in attribute mapping");
        }
    }
}

/// <summary>
/// Applies RFC 7644 PATCH operations to a SCIM resource document, recording
/// the paths that were removed so the mapper can clear their targets.
/// Handles the identity-provider dialects the module already accepted:
/// pathless operations whose value is an attribute bag (Entra's disable
/// form), bracketed multi-valued paths, and dotted keys inside a value bag.
/// </summary>
public static class ScimPatchDocument
{
    public static HashSet<string> Apply(JsonObject resource, IEnumerable<ScimPatchOperation> operations)
    {
        var removed = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        foreach (var op in operations)
        {
            var name = op.Op?.Trim().ToLowerInvariant() ?? string.Empty;
            var path = op.Path?.Trim();
            var value = ToNode(op.Value);

            switch (name)
            {
                case "add":
                case "replace":
                    if (string.IsNullOrEmpty(path))
                    {
                        if (value is JsonObject bag)
                        {
                            foreach (var (key, item) in bag.ToList())
                                Write(resource, key, item?.DeepClone(), append: name == "add");
                        }
                    }
                    else
                    {
                        Write(resource, path, value, append: name == "add");
                    }
                    break;

                case "remove":
                    if (!string.IsNullOrEmpty(path))
                    {
                        ScimAttributePath.Write(resource, path, null);
                        removed.Add(path);
                    }
                    break;
            }
        }

        return removed;
    }

    private static void Write(JsonObject resource, string path, JsonNode? value, bool append)
    {
        if (append && value is JsonArray incoming && ScimAttributePath.ReadNode(resource, path) is JsonArray)
        {
            // add on a multi-valued attribute appends rather than replaces.
            var existing = (JsonArray)ScimAttributePath.ReadNode(resource, path)!;
            foreach (var item in incoming)
                existing.Add(item?.DeepClone());
            return;
        }

        ScimAttributePath.Write(resource, path, value);
    }

    private static JsonNode? ToNode(object? value) => value switch
    {
        null => null,
        JsonNode node => node,
        JsonElement element => element.ValueKind == JsonValueKind.Null ? null : JsonSerializer.SerializeToNode(element),
        _ => JsonSerializer.SerializeToNode(value),
    };
}
