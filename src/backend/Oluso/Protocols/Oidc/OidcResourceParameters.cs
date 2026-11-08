using Microsoft.Extensions.Primitives;

namespace Oluso.Protocols.Oidc;

/// <summary>
/// RFC 8707 sends one <c>resource</c> parameter per resource. Parameters elsewhere are kept as
/// single strings, so resources are held space-separated (a URI can't contain a raw space), never
/// comma-joined as repeated values otherwise would be.
/// </summary>
public static class OidcResourceParameters
{
    public const string Name = "resource";

    /// <summary>A parameter's value for the parameter dictionary: resources space-joined, others as-is.</summary>
    public static string Flatten(string key, StringValues values) =>
        key == Name ? string.Join(' ', Read(values)) : values.ToString();

    /// <summary>The distinct, non-empty resources in repeated (or space-separated) parameters.</summary>
    public static List<string> Read(StringValues values) =>
        values.SelectMany(v => Split(v)).Distinct(StringComparer.Ordinal).ToList();

    public static List<string> Split(string? value) =>
        string.IsNullOrWhiteSpace(value)
            ? new List<string>()
            : value.Split(' ', StringSplitOptions.RemoveEmptyEntries).Distinct(StringComparer.Ordinal).ToList();
}
