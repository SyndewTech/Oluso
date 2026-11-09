using System.Collections;
using System.Collections.Concurrent;
using System.Text.Json;
using System.Text.Json.Nodes;
using Scriban;
using Scriban.Parsing;
using Scriban.Runtime;
using Scriban.Syntax;

namespace Oluso.Enterprise.Scim.Outbound;

/// <summary>
/// A template or expression failed to parse or run. Callers translate this
/// into the error type of their direction (outbound dead-letter, inbound 400).
/// </summary>
public sealed class ScimExpressionException : Exception
{
    public ScimExpressionException(string message) : base(message)
    {
    }
}

/// <summary>
/// Evaluates the Scriban templates and boolean expressions administrators
/// attach to attribute mappings, in both directions. Each direction supplies
/// its own globals:
/// <list type="bullet">
/// <item>Outbound (we push): <c>user</c> (id, user_name, email, first_name, last_name,
/// display_name, full_name, phone_number, locale, time_zone, is_active, picture_url, custom),
/// <c>roles</c>, <c>teams</c>, <c>team_codes</c>, <c>custom</c>.</item>
/// <item>Inbound (an IdP pushes to us): <c>scim</c> (the incoming resource as sent, e.g.
/// <c>scim.userName</c>, <c>scim.name.givenName</c>, <c>scim.emails</c>), <c>enterprise</c>
/// (the enterprise extension object), <c>attr "path"</c> (reads any SCIM path, including
/// <c>emails[type eq "work"].value</c> and extension URNs), <c>client</c> (id, name).</item>
/// </list>
/// Parsed templates are cached.
/// </summary>
public sealed class ScimExpressionEngine
{
    private const int CacheLimit = 1000;
    private static readonly LexerOptions ExpressionLexer = new() { Mode = ScriptMode.ScriptOnly };

    private readonly ConcurrentDictionary<string, Template> _templates = new(StringComparer.Ordinal);
    private readonly ConcurrentDictionary<string, Template> _expressions = new(StringComparer.Ordinal);

    /// <summary>Returns a parse error message, or null when the template is valid.</summary>
    public static string? ValidateTemplate(string template)
    {
        var parsed = Template.Parse(template);
        return parsed.HasErrors ? string.Join("; ", parsed.Messages.Select(m => m.ToString())) : null;
    }

    /// <summary>Returns a parse error message, or null when the expression is valid.</summary>
    public static string? ValidateExpression(string expression)
    {
        var parsed = Template.Parse(expression, lexerOptions: ExpressionLexer);
        return parsed.HasErrors ? string.Join("; ", parsed.Messages.Select(m => m.ToString())) : null;
    }

    public string Render(string template, ScriptObject globals, string what)
    {
        var parsed = GetOrParse(_templates, template, isExpression: false, what);
        try
        {
            return parsed.Render(BuildContext(globals)).Trim();
        }
        catch (ScriptRuntimeException ex)
        {
            throw new ScimExpressionException($"{what} failed: {ex.Message}");
        }
    }

    public bool Evaluate(string expression, ScriptObject globals, string what)
    {
        var parsed = GetOrParse(_expressions, expression, isExpression: true, what);
        try
        {
            return IsTruthy(parsed.Evaluate(BuildContext(globals)));
        }
        catch (ScriptRuntimeException ex)
        {
            throw new ScimExpressionException($"{what} failed: {ex.Message}");
        }
    }

    // ----------------------------------------------------------- outbound

    public string Render(string template, ScimOutboundUserContext context, string what)
    {
        try
        {
            return Render(template, BuildOutboundGlobals(context), what);
        }
        catch (ScimExpressionException ex)
        {
            throw new ScimOutboundMappingException($"{ex.Message} (user {context.User.Id})");
        }
    }

    public bool Evaluate(string expression, ScimOutboundUserContext context, string what)
    {
        try
        {
            return Evaluate(expression, BuildOutboundGlobals(context), what);
        }
        catch (ScimExpressionException ex)
        {
            throw new ScimOutboundMappingException($"{ex.Message} (user {context.User.Id})");
        }
    }

    public static ScriptObject BuildOutboundGlobals(ScimOutboundUserContext context)
    {
        var user = context.User;
        var custom = new ScriptObject();
        foreach (var (key, value) in context.CustomProperties)
            custom[key] = value;

        var roles = new ScriptArray(context.RoleNames);
        var teams = new ScriptArray(context.TeamNames);
        var teamCodes = new ScriptArray(context.TeamCodes);

        var fullName = $"{user.FirstName} {user.LastName}".Trim();
        var userObject = new ScriptObject
        {
            ["id"] = user.Id,
            ["user_name"] = user.UserName ?? string.Empty,
            ["email"] = user.Email ?? string.Empty,
            ["first_name"] = user.FirstName ?? string.Empty,
            ["last_name"] = user.LastName ?? string.Empty,
            ["display_name"] = string.IsNullOrWhiteSpace(user.DisplayName) ? fullName : user.DisplayName,
            ["full_name"] = fullName,
            ["phone_number"] = user.PhoneNumber ?? string.Empty,
            ["locale"] = user.Locale ?? string.Empty,
            ["time_zone"] = user.TimeZone ?? string.Empty,
            ["is_active"] = user.IsActive,
            ["picture_url"] = user.ProfilePictureUrl ?? string.Empty,
            ["custom"] = custom,
            ["roles"] = roles,
            ["teams"] = teams,
            ["team_codes"] = teamCodes,
        };

        return new ScriptObject
        {
            ["user"] = userObject,
            ["roles"] = roles,
            ["teams"] = teams,
            ["team_codes"] = teamCodes,
            ["custom"] = custom,
        };
    }

    // ------------------------------------------------------------ inbound

    /// <summary>
    /// Globals for an inbound mapping: the SCIM resource as sent, the
    /// enterprise extension under <c>enterprise</c>, an <c>attr</c> reader
    /// for arbitrary paths, and the calling client.
    /// </summary>
    public static ScriptObject BuildInboundGlobals(JsonObject resource, string clientId, string clientName)
    {
        var scim = (ScriptObject)ToScriptValue(resource)!;
        var enterprise = resource[ScimConstants.Schemas.EnterpriseUser] is JsonObject ext
            ? (ScriptObject)ToScriptValue(ext)!
            : new ScriptObject();

        var globals = new ScriptObject
        {
            ["scim"] = scim,
            ["enterprise"] = enterprise,
            ["client"] = new ScriptObject { ["id"] = clientId, ["name"] = clientName },
        };
        globals.Import("attr", new Func<string, string?>(path => Services.ScimAttributePath.Read(resource, path)));
        return globals;
    }

    private static object? ToScriptValue(JsonNode? node)
    {
        switch (node)
        {
            case null:
                return null;
            case JsonObject obj:
            {
                var result = new ScriptObject();
                foreach (var (key, value) in obj)
                    result[key] = ToScriptValue(value);
                return result;
            }
            case JsonArray array:
            {
                var result = new ScriptArray();
                foreach (var item in array)
                    result.Add(ToScriptValue(item));
                return result;
            }
            case JsonValue value:
            {
                var element = value.GetValue<JsonElement>();
                return element.ValueKind switch
                {
                    JsonValueKind.String => element.GetString(),
                    JsonValueKind.True => true,
                    JsonValueKind.False => false,
                    JsonValueKind.Number => element.TryGetInt64(out var l) ? l : element.GetDouble(),
                    JsonValueKind.Null => null,
                    _ => element.ToString(),
                };
            }
            default:
                return node.ToJsonString();
        }
    }

    // ------------------------------------------------------------- shared

    private Template GetOrParse(ConcurrentDictionary<string, Template> cache, string text, bool isExpression, string what)
    {
        if (cache.TryGetValue(text, out var cached))
            return cached;

        var parsed = isExpression ? Template.Parse(text, lexerOptions: ExpressionLexer) : Template.Parse(text);
        if (parsed.HasErrors)
            throw new ScimExpressionException($"{what} does not parse: {string.Join("; ", parsed.Messages.Select(m => m.ToString()))}");

        if (cache.Count >= CacheLimit)
            cache.Clear();
        cache[text] = parsed;
        return parsed;
    }

    private static TemplateContext BuildContext(ScriptObject globals)
    {
        var context = new TemplateContext
        {
            MemberRenamer = member => member.Name,
            StrictVariables = false,
            EnableRelaxedMemberAccess = true,
            EnableRelaxedTargetAccess = true,
            EnableRelaxedIndexerAccess = true,
        };
        context.PushGlobal(globals);
        return context;
    }

    internal static bool IsTruthy(object? value) => value switch
    {
        null => false,
        bool b => b,
        string s => !string.IsNullOrWhiteSpace(s),
        int i => i != 0,
        long l => l != 0,
        double d => d != 0,
        decimal m => m != 0,
        IEnumerable e => e.Cast<object?>().Any(),
        _ => true,
    };
}
