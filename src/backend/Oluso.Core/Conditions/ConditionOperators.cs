using System.Collections;
using System.Globalization;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace Oluso.Core.Conditions;

/// <summary>
/// The comparison operators every platform condition shares: the expression engine
/// (<see cref="IExpressionEvaluator"/>, approval rules) and the structured journey
/// conditions (step conditions, the condition step, the branch step). Spellings are
/// matched case-insensitively with underscores ignored, so <c>not_equals</c>,
/// <c>notEquals</c> and <c>ne</c> agree.
/// <para>Equality and text operators are case-insensitive. Ordering compares numbers,
/// then dates, and is false for anything else. Membership splits a comma-separated
/// value into its items, as approval rules do for <c>RequesterRoles</c>.</para>
/// </summary>
public static class ConditionOperators
{
    private static readonly Dictionary<string, string> Aliases = new(StringComparer.Ordinal)
    {
        ["eq"] = "eq", ["equals"] = "eq", ["=="] = "eq", ["="] = "eq",
        ["ne"] = "ne", ["neq"] = "ne", ["notequals"] = "ne", ["!="] = "ne",
        ["gt"] = "gt", [">"] = "gt", ["greaterthan"] = "gt",
        ["gte"] = "gte", [">="] = "gte", ["greaterthanorequals"] = "gte",
        ["lt"] = "lt", ["<"] = "lt", ["lessthan"] = "lt",
        ["lte"] = "lte", ["<="] = "lte", ["lessthanorequals"] = "lte",
        ["contains"] = "contains", ["notcontains"] = "not_contains", ["!contains"] = "not_contains",
        ["startswith"] = "starts_with", ["endswith"] = "ends_with",
        ["exists"] = "exists", ["notnull"] = "exists", ["isset"] = "exists",
        ["notexists"] = "not_exists", ["null"] = "not_exists", ["isnotset"] = "not_exists",
        ["empty"] = "empty", ["notempty"] = "not_empty",
        ["in"] = "in", ["notin"] = "not_in", ["!in"] = "not_in",
        ["regex"] = "regex", ["matches"] = "regex",
        ["true"] = "true", ["false"] = "false",
    };

    /// <summary>Canonical operator names.</summary>
    public static IReadOnlyCollection<string> Names { get; } = Aliases.Values.Distinct().OrderBy(n => n).ToArray();

    public static IReadOnlyCollection<string> NoValueOperators { get; } = new[] { "exists", "not_exists", "empty", "not_empty", "true", "false" };
    public static IReadOnlyCollection<string> NumericOperators { get; } = new[] { "gt", "gte", "lt", "lte" };
    public static IReadOnlyCollection<string> ListOperators { get; } = new[] { "in", "not_in" };

    /// <summary>The canonical name, or null for an operator no condition supports.</summary>
    public static string? Normalize(string? op) =>
        op != null && Aliases.TryGetValue(op.Trim().ToLowerInvariant().Replace("_", ""), out var canonical) ? canonical : null;

    /// <summary>
    /// One value against one expected value, for the single-value operators
    /// (equality, ordering, text and regex). Anything else is false.
    /// </summary>
    public static bool Compare(string actual, string? op, string? expected) => Normalize(op) switch
    {
        "eq" => string.Equals(actual, expected ?? "", StringComparison.OrdinalIgnoreCase),
        "ne" => !string.Equals(actual, expected ?? "", StringComparison.OrdinalIgnoreCase),
        "gt" => Order(actual, expected) > 0,
        "gte" => Order(actual, expected) >= 0,
        "lt" => Order(actual, expected) is < 0 and not int.MinValue,
        "lte" => Order(actual, expected) is <= 0 and not int.MinValue,
        "contains" => actual.Contains(expected ?? "", StringComparison.OrdinalIgnoreCase),
        "not_contains" => !actual.Contains(expected ?? "", StringComparison.OrdinalIgnoreCase),
        "starts_with" => actual.StartsWith(expected ?? "", StringComparison.OrdinalIgnoreCase),
        "ends_with" => actual.EndsWith(expected ?? "", StringComparison.OrdinalIgnoreCase),
        "regex" => MatchesRegex(actual, expected),
        _ => false
    };

    /// <summary>
    /// A condition over a value that may hold several entries (a multi-valued claim such
    /// as <c>role</c>): positive operators pass when any entry matches, negative ones when
    /// none does. An unknown operator never passes.
    /// </summary>
    public static bool Evaluate(string? op, IReadOnlyList<string> actual, string? expected, IReadOnlyCollection<string>? expectedList = null)
    {
        var present = actual.Where(v => !string.IsNullOrEmpty(v)).ToList();

        return Normalize(op) switch
        {
            "exists" or "not_empty" => present.Count > 0,
            "not_exists" or "empty" => present.Count == 0,
            "ne" => !present.Any(v => Compare(v, "eq", expected)),
            "not_contains" => !present.Any(v => Compare(v, "contains", expected)),
            "in" => IsMember(present, ExpectedList(expected, expectedList)),
            "not_in" => !IsMember(present, ExpectedList(expected, expectedList)),
            "true" => present.Any(IsTruthy),
            "false" => !present.Any(IsTruthy),
            var single when single != null => present.Any(v => Compare(v, single, expected)),
            _ => false
        };
    }

    /// <summary>
    /// The individual values a property holds for membership: each element of an
    /// enumerable, each comma-separated item of a string, or the single value itself.
    /// </summary>
    public static IEnumerable<string> MembershipValues(object? actual)
    {
        switch (actual)
        {
            case null:
                yield break;
            case string s:
                foreach (var part in s.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
                    yield return part;
                yield break;
            case IEnumerable enumerable:
                foreach (var item in enumerable)
                {
                    var text = item?.ToString();
                    if (!string.IsNullOrWhiteSpace(text))
                        yield return text.Trim();
                }
                yield break;
            default:
                yield return actual.ToString() ?? string.Empty;
                yield break;
        }
    }

    /// <summary>Flattens a stored value (string, bool, number, list, JSON) into its entries.</summary>
    public static IReadOnlyList<string> ToValues(object? value)
    {
        switch (value)
        {
            case null:
                return Array.Empty<string>();
            case string s:
                return new[] { s };
            case bool b:
                return new[] { b ? "true" : "false" };
            case JsonElement element:
                return FromJson(element);
            case IEnumerable<string> strings:
                return strings.ToList();
            case IEnumerable items:
                return items.Cast<object?>().SelectMany(ToValues).ToList();
            default:
                return new[] { Convert.ToString(value, CultureInfo.InvariantCulture) ?? "" };
        }
    }

    private static IReadOnlyList<string> FromJson(JsonElement element) => element.ValueKind switch
    {
        JsonValueKind.String => new[] { element.GetString() ?? "" },
        JsonValueKind.True => new[] { "true" },
        JsonValueKind.False => new[] { "false" },
        JsonValueKind.Number => new[] { element.GetRawText() },
        JsonValueKind.Array => element.EnumerateArray().SelectMany(FromJson).ToList(),
        JsonValueKind.Object => new[] { element.GetRawText() },
        _ => Array.Empty<string>()
    };

    private static bool IsMember(IEnumerable<string> actual, IReadOnlyCollection<string> expected) =>
        actual.SelectMany(MembershipValues).Any(a => expected.Contains(a, StringComparer.OrdinalIgnoreCase));

    private static IReadOnlyCollection<string> ExpectedList(string? expected, IReadOnlyCollection<string>? expectedList)
    {
        if (expectedList is { Count: > 0 }) return expectedList;
        if (string.IsNullOrWhiteSpace(expected)) return Array.Empty<string>();

        var trimmed = expected.Trim();
        if (trimmed.StartsWith('['))
        {
            try
            {
                return JsonSerializer.Deserialize<string[]>(trimmed) ?? Array.Empty<string>();
            }
            catch (JsonException)
            {
                // Not a JSON array; read it as a comma-separated list.
            }
        }

        return MembershipValues(trimmed).ToArray();
    }

    /// <summary>Numbers, then dates; <see cref="int.MinValue"/> when the two can't be ordered.</summary>
    private static int Order(string actual, string? expected)
    {
        if (expected == null) return int.MinValue;

        if (decimal.TryParse(actual, NumberStyles.Any, CultureInfo.InvariantCulture, out var actualNumber)
            && decimal.TryParse(expected, NumberStyles.Any, CultureInfo.InvariantCulture, out var expectedNumber))
        {
            return actualNumber.CompareTo(expectedNumber);
        }

        if (DateTimeOffset.TryParse(actual, CultureInfo.InvariantCulture, DateTimeStyles.AssumeUniversal, out var actualDate)
            && DateTimeOffset.TryParse(expected, CultureInfo.InvariantCulture, DateTimeStyles.AssumeUniversal, out var expectedDate))
        {
            return actualDate.CompareTo(expectedDate);
        }

        return int.MinValue;
    }

    private static bool MatchesRegex(string value, string? pattern)
    {
        if (string.IsNullOrEmpty(pattern)) return false;
        try
        {
            return Regex.IsMatch(value, pattern, RegexOptions.IgnoreCase, TimeSpan.FromSeconds(1));
        }
        catch (ArgumentException)
        {
            return false;
        }
        catch (RegexMatchTimeoutException)
        {
            return false;
        }
    }

    private static bool IsTruthy(string value) =>
        !value.Equals("false", StringComparison.OrdinalIgnoreCase) && value != "0";
}
