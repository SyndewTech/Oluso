using System.Text.Json;

namespace Oluso.Core.UserJourneys;

/// <summary>
/// How a journey signed its user in, read from its data the way login steps record it: <c>auth_method</c>
/// (the step's method, e.g. <c>fido2</c>) and <c>amr</c> (one value or a list, e.g. <c>hwk</c>, <c>mfa</c>).
/// The hosted journey's session cookie and the codes embedded and headless journeys complete with carry
/// the same methods.
/// </summary>
public static class JourneyAuthenticationMethods
{
    public const string MethodKey = "auth_method";
    public const string AmrKey = "amr";

    /// <summary>Set by a login step when it authenticates the user in this journey.</summary>
    public const string AuthenticatedAtKey = "authenticated_at";

    public static IReadOnlyList<string> Read(IDictionary<string, object>? data)
    {
        var methods = new List<string>();
        if (data == null) return methods;

        if (ReadString(data, MethodKey) is { Length: > 0 } method) methods.Add(method);

        if (data.TryGetValue(AmrKey, out var amr))
        {
            switch (amr)
            {
                case string single:
                    methods.Add(single);
                    break;
                case IEnumerable<string> many:
                    methods.AddRange(many);
                    break;
                case JsonElement { ValueKind: JsonValueKind.String } element:
                    methods.Add(element.GetString()!);
                    break;
                case JsonElement { ValueKind: JsonValueKind.Array } element:
                    methods.AddRange(element.EnumerateArray()
                        .Where(item => item.ValueKind == JsonValueKind.String)
                        .Select(item => item.GetString()!));
                    break;
            }
        }

        return methods.Where(m => !string.IsNullOrWhiteSpace(m)).Distinct(StringComparer.Ordinal).ToList();
    }

    /// <summary>The step method that signed the user in (<c>auth_method</c>), if any.</summary>
    public static string? Method(IDictionary<string, object>? data) =>
        data == null ? null : ReadString(data, MethodKey);

    /// <summary>A login step authenticated the user during this journey (not an existing session).</summary>
    public static bool AuthenticatedInJourney(IDictionary<string, object>? data) =>
        data?.ContainsKey(AuthenticatedAtKey) == true;

    private static string? ReadString(IDictionary<string, object> data, string key)
    {
        if (!data.TryGetValue(key, out var value)) return null;
        return value switch
        {
            string s => s,
            JsonElement { ValueKind: JsonValueKind.String } element => element.GetString(),
            null => null,
            _ => value.ToString()
        };
    }
}
