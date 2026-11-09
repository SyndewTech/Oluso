using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.RegularExpressions;
using Oluso.Core.Domain.Entities;
using Oluso.Enterprise.Scim.Entities;

namespace Oluso.Enterprise.Scim.Outbound;

/// <summary>
/// A payload could not be built from the configured mappings. This is a
/// configuration or data problem, not a transport one, so the queue item is
/// not retried.
/// </summary>
public sealed class ScimOutboundMappingException : Exception
{
    public ScimOutboundMappingException(string message) : base(message)
    {
    }
}

/// <summary>
/// Everything an outbound mapping may read about a user. Role and team
/// names are only needed by expressions and conditions; callers that know
/// no mapping uses them can pass empty lists.
/// </summary>
public sealed class ScimOutboundUserContext
{
    public required OlusoUser User { get; init; }
    public IReadOnlyList<string> RoleNames { get; init; } = Array.Empty<string>();
    public IReadOnlyList<string> TeamNames { get; init; } = Array.Empty<string>();
    public IReadOnlyList<string> TeamCodes { get; init; } = Array.Empty<string>();

    private IReadOnlyDictionary<string, string>? _custom;

    public IReadOnlyDictionary<string, string> CustomProperties => _custom ??= ReadCustomProperties(User);

    private static IReadOnlyDictionary<string, string> ReadCustomProperties(OlusoUser user)
    {
        var result = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        if (string.IsNullOrWhiteSpace(user.CustomPropertiesJson))
            return result;

        try
        {
            if (JsonNode.Parse(user.CustomPropertiesJson) is not JsonObject node)
                return result;

            foreach (var (key, value) in node)
            {
                if (value == null)
                    continue;
                result[key] = value is JsonValue v && v.TryGetValue<string>(out var s) ? s : value.ToJsonString();
            }
        }
        catch (JsonException)
        {
        }

        return result;
    }
}

/// <summary>
/// Builds the SCIM representations we push to a provider.
/// </summary>
public interface IScimOutboundMapper
{
    /// <summary>True when the provider or any mapping evaluates a Scriban expression, so the caller must load role and team names.</summary>
    bool UsesExpressions(ScimProvider provider, IReadOnlyList<ScimProviderAttributeMapping> mappings);

    /// <summary>
    /// The value used as the SCIM <c>userName</c> for this provider, or null
    /// when the user has no value for the configured source.
    /// </summary>
    string? ResolveUserName(ScimProvider provider, ScimOutboundUserContext context);

    JsonObject BuildUser(
        ScimProvider provider,
        ScimProviderFlavorProfile flavor,
        ScimOutboundUserContext context,
        IReadOnlyList<ScimProviderAttributeMapping> mappings);

    /// <summary>
    /// <paramref name="externalId"/> is the group's queue key (<c>role:&lt;id&gt;</c>
    /// or <c>team:&lt;id&gt;</c>) so the provider can be searched by it later.
    /// </summary>
    JsonObject BuildGroup(string externalId, string displayName, IEnumerable<string> remoteMemberIds);

    /// <summary>
    /// <see cref="BuildGroup(string, string, IEnumerable{string})"/> plus the team extension, for flavors that send it
    /// (<see cref="ScimProviderFlavorProfile.SendsTeamExtension"/>). Lead ids are remote ids, like member ids.
    /// </summary>
    JsonObject BuildGroup(string externalId, string displayName, IEnumerable<string> remoteMemberIds, ScimTeamGroupExtension team);

    string ComputeHash(JsonNode payload);
}

/// <summary>
/// The team extension of a Group (<see cref="Schema"/>): the team's code, its parent team's group (<c>team:&lt;id&gt;</c>,
/// null at the top), the remote ids of the members who lead it directly, and the module that manages it (omitted when none).
/// </summary>
public sealed record ScimTeamGroupExtension(string? Code, string? Parent, IReadOnlyList<string> Leads, string? ManagedBy)
{
    public const string Schema = "urn:syndew:params:scim:schemas:extension:team:2.0:Group";
}

public sealed partial class ScimOutboundMapper : IScimOutboundMapper
{
    private static readonly JsonSerializerOptions HashOptions = new() { WriteIndented = false };

    private readonly ScimExpressionEngine _expressions;

    public ScimOutboundMapper(ScimExpressionEngine expressions)
    {
        _expressions = expressions;
    }

    public bool UsesExpressions(ScimProvider provider, IReadOnlyList<ScimProviderAttributeMapping> mappings) =>
        string.Equals(provider.UserNameSource, ScimProviderUserNameSources.Expression, StringComparison.OrdinalIgnoreCase)
        || mappings.Any(m => m.IsEnabled && (!string.IsNullOrWhiteSpace(m.Expression) || !string.IsNullOrWhiteSpace(m.Condition)));

    public string? ResolveUserName(ScimProvider provider, ScimOutboundUserContext context)
    {
        var user = context.User;
        string? value;
        if (string.Equals(provider.UserNameSource, ScimProviderUserNameSources.Expression, StringComparison.OrdinalIgnoreCase))
        {
            if (string.IsNullOrWhiteSpace(provider.UserNameExpression))
                throw new ScimOutboundMappingException("userName source is 'expression' but no userName expression is configured");
            value = _expressions.Render(provider.UserNameExpression, context, "userName expression");
        }
        else if (string.Equals(provider.UserNameSource, ScimProviderUserNameSources.Email, StringComparison.OrdinalIgnoreCase))
        {
            value = user.Email;
        }
        else
        {
            value = user.UserName;
        }

        return string.IsNullOrWhiteSpace(value) ? null : value.Trim();
    }

    public JsonObject BuildUser(
        ScimProvider provider,
        ScimProviderFlavorProfile flavor,
        ScimOutboundUserContext context,
        IReadOnlyList<ScimProviderAttributeMapping> mappings)
    {
        var user = context.User;
        var userName = ResolveUserName(provider, context)
            ?? throw new ScimOutboundMappingException(
                $"User {user.Id} has no {provider.UserNameSource} to use as the SCIM userName");

        if (flavor.RequiresEmailUserName && !userName.Contains('@'))
            throw new ScimOutboundMappingException(
                $"{flavor.DisplayName} requires userName to be an email address; '{userName}' is not one");

        var payload = new JsonObject
        {
            ["schemas"] = new JsonArray(ScimConstants.Schemas.User),
            ["externalId"] = user.Id,
            ["userName"] = userName,
            ["active"] = user.IsActive,
        };

        var effective = mappings.Count > 0
            ? mappings.Where(m => m.IsEnabled).ToList()
            : flavor.DefaultUserMappings.Select(m => new ScimProviderAttributeMapping
            {
                ScimAttribute = m.ScimAttribute,
                InternalProperty = m.InternalProperty,
            }).ToList();

        foreach (var mapping in effective)
        {
            var attribute = mapping.ScimAttribute;
            if (string.IsNullOrWhiteSpace(attribute) || IsReservedAttribute(attribute))
                continue;

            if (!flavor.SupportsPhoneNumbers && attribute.StartsWith("phoneNumbers", StringComparison.OrdinalIgnoreCase))
                continue;

            if (!flavor.SupportsEnterpriseExtension && attribute.StartsWith(ScimConstants.Schemas.EnterpriseUser, StringComparison.OrdinalIgnoreCase))
                continue;

            if (!string.IsNullOrWhiteSpace(mapping.Condition)
                && !_expressions.Evaluate(mapping.Condition, context, $"Condition on '{attribute}'"))
                continue;

            string? raw;
            if (!string.IsNullOrWhiteSpace(mapping.Expression))
                raw = _expressions.Render(mapping.Expression, context, $"Expression for '{attribute}'");
            else if (!string.IsNullOrWhiteSpace(mapping.InternalProperty))
                raw = ResolveProperty(context, mapping.InternalProperty);
            else
                throw new ScimOutboundMappingException($"Mapping for '{attribute}' has neither an internal property nor an expression");

            var value = ApplyTransformation(raw, mapping.Transformation);
            if (string.IsNullOrEmpty(value))
                value = string.IsNullOrEmpty(mapping.DefaultValue) ? null : mapping.DefaultValue;
            if (value == null)
                continue;

            SetAttribute(payload, attribute, value);
        }

        return payload;
    }

    public JsonObject BuildGroup(string externalId, string displayName, IEnumerable<string> remoteMemberIds)
    {
        var members = new JsonArray();
        foreach (var id in remoteMemberIds.Distinct(StringComparer.Ordinal).OrderBy(id => id, StringComparer.Ordinal))
        {
            members.Add(new JsonObject { ["value"] = id, ["type"] = "User" });
        }

        return new JsonObject
        {
            ["schemas"] = new JsonArray(ScimConstants.Schemas.Group),
            ["externalId"] = externalId,
            ["displayName"] = displayName,
            ["members"] = members,
        };
    }

    public JsonObject BuildGroup(string externalId, string displayName, IEnumerable<string> remoteMemberIds, ScimTeamGroupExtension team)
    {
        var payload = BuildGroup(externalId, displayName, remoteMemberIds);
        payload["schemas"]!.AsArray().Add(ScimTeamGroupExtension.Schema);

        var leads = new JsonArray();
        foreach (var id in team.Leads.Distinct(StringComparer.Ordinal).OrderBy(id => id, StringComparer.Ordinal))
            leads.Add(id);
        var extension = new JsonObject
        {
            ["code"] = team.Code,
            ["parent"] = team.Parent,
            ["leads"] = leads,
        };
        if (!string.IsNullOrEmpty(team.ManagedBy))
            extension["managedBy"] = team.ManagedBy;
        payload[ScimTeamGroupExtension.Schema] = extension;
        return payload;
    }

    public string ComputeHash(JsonNode payload)
    {
        var json = payload.ToJsonString(HashOptions);
        var bytes = SHA256.HashData(Encoding.UTF8.GetBytes(json));
        return Convert.ToHexString(bytes).ToLowerInvariant();
    }

    /// <summary>
    /// Attributes the pipeline owns; a mapping row naming one is ignored so a
    /// stray configuration cannot break identity or lifecycle semantics.
    /// </summary>
    private static bool IsReservedAttribute(string attribute) =>
        attribute.Equals("schemas", StringComparison.OrdinalIgnoreCase)
        || attribute.Equals("id", StringComparison.OrdinalIgnoreCase)
        || attribute.Equals("externalId", StringComparison.OrdinalIgnoreCase)
        || attribute.Equals("userName", StringComparison.OrdinalIgnoreCase)
        || attribute.Equals("active", StringComparison.OrdinalIgnoreCase)
        || attribute.Equals("password", StringComparison.OrdinalIgnoreCase)
        || attribute.Equals("meta", StringComparison.OrdinalIgnoreCase)
        || attribute.Equals("groups", StringComparison.OrdinalIgnoreCase);

    private static string? ResolveProperty(ScimOutboundUserContext context, string property)
    {
        var user = context.User;
        if (property.StartsWith(ScimOutboundProperties.CustomPrefix, StringComparison.OrdinalIgnoreCase))
        {
            var key = property[ScimOutboundProperties.CustomPrefix.Length..];
            return context.CustomProperties.TryGetValue(key, out var v) ? v : null;
        }

        return property.ToLowerInvariant() switch
        {
            "username" => user.UserName,
            "email" => user.Email,
            "firstname" => user.FirstName,
            "lastname" => user.LastName,
            "displayname" => !string.IsNullOrWhiteSpace(user.DisplayName) ? user.DisplayName : FullName(user),
            "fullname" => FullName(user),
            "phonenumber" => user.PhoneNumber,
            "locale" => user.Locale,
            "timezone" => user.TimeZone,
            "id" => user.Id,
            "profilepictureurl" => user.ProfilePictureUrl,
            _ => throw new ScimOutboundMappingException($"Unknown internal property '{property}' in outbound attribute mapping"),
        };
    }

    private static string? FullName(OlusoUser user)
    {
        var name = $"{user.FirstName} {user.LastName}".Trim();
        return name.Length == 0 ? null : name;
    }

    private static string? ApplyTransformation(string? value, string? transformation)
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
                throw new ScimOutboundMappingException($"Unknown transformation '{transformation}' in outbound attribute mapping");
        }
    }

    [GeneratedRegex(@"^(?<attr>[A-Za-z][A-Za-z0-9_]*)\[(?<fattr>[A-Za-z][A-Za-z0-9_]*)\s+eq\s+""?(?<fval>[^""\]]+?)""?\]\.(?<sub>[A-Za-z][A-Za-z0-9_]*)$")]
    private static partial Regex MultiValuedPath();

    /// <summary>
    /// Writes a value at a SCIM attribute path: simple (<c>displayName</c>),
    /// complex (<c>name.givenName</c>), filtered multi-valued
    /// (<c>emails[type eq "work"].value</c>) or extension-qualified
    /// (<c>urn:...:2.0:User:department</c>).
    /// </summary>
    internal static void SetAttribute(JsonObject payload, string attribute, string value)
    {
        if (attribute.StartsWith("urn:", StringComparison.OrdinalIgnoreCase))
        {
            var separator = attribute.LastIndexOf(':');
            if (separator <= 0 || separator == attribute.Length - 1)
                throw new ScimOutboundMappingException($"Extension attribute '{attribute}' has no attribute name after the schema URN");

            var schema = attribute[..separator];
            var subPath = attribute[(separator + 1)..];

            var schemas = payload["schemas"] as JsonArray ?? new JsonArray();
            if (!schemas.Any(s => string.Equals(s?.GetValue<string>(), schema, StringComparison.OrdinalIgnoreCase)))
                schemas.Add(schema);
            payload["schemas"] = schemas;

            if (payload[schema] is not JsonObject extension)
            {
                extension = new JsonObject();
                payload[schema] = extension;
            }

            SetSimplePath(extension, subPath, value);
            return;
        }

        SetSimplePath(payload, attribute, value);
    }

    private static void SetSimplePath(JsonObject target, string path, string value)
    {
        var multi = MultiValuedPath().Match(path);
        if (multi.Success)
        {
            var attr = multi.Groups["attr"].Value;
            var filterAttr = multi.Groups["fattr"].Value;
            var filterValue = multi.Groups["fval"].Value;
            var sub = multi.Groups["sub"].Value;

            if (target[attr] is not JsonArray array)
            {
                array = new JsonArray();
                target[attr] = array;
            }

            var element = array.OfType<JsonObject>().FirstOrDefault(e =>
                e[filterAttr] is JsonValue fv && string.Equals(fv.ToString(), filterValue, StringComparison.OrdinalIgnoreCase));

            if (element == null)
            {
                element = new JsonObject();
                if (bool.TryParse(filterValue, out var boolFilter))
                    element[filterAttr] = boolFilter;
                else
                    element[filterAttr] = filterValue;

                if (array.Count == 0 && element["primary"] == null)
                    element["primary"] = true;

                array.Add(element);
            }

            element[sub] = value;
            return;
        }

        var dot = path.IndexOf('.');
        if (dot > 0)
        {
            var head = path[..dot];
            var rest = path[(dot + 1)..];
            if (target[head] is not JsonObject child)
            {
                child = new JsonObject();
                target[head] = child;
            }

            SetSimplePath(child, rest, value);
            return;
        }

        if (path.Equals("active", StringComparison.OrdinalIgnoreCase) && bool.TryParse(value, out var active))
        {
            target[path] = active;
            return;
        }

        target[path] = value;
    }
}
