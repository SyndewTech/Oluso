using System.Text.RegularExpressions;

namespace Oluso.Core.UserJourneys;

/// <summary>
/// How a request chooses its journey policy, the same in every <see cref="IJourneyPolicyStore"/>: among the enabled
/// policies of the request's type for its tenant or global, by priority (highest first) and then tenant-specific
/// before global, a policy whose conditions the request meets is more specific than one without conditions and is
/// chosen first; a policy without conditions is the default; a policy whose conditions fail is never chosen.
/// </summary>
public static class JourneyPolicyMatching
{
    private static readonly TimeSpan RegexTimeout = TimeSpan.FromMilliseconds(100);

    public static JourneyPolicy? Choose(IEnumerable<JourneyPolicy> policies, JourneyPolicyMatchContext context)
    {
        var candidates = policies
            .Where(p => p.Enabled && p.Type == context.Type)
            .Where(p => p.TenantId == null || p.TenantId == context.TenantId)
            .OrderByDescending(p => p.Priority)
            .ThenBy(p => p.TenantId == null ? 1 : 0)
            .ToList();

        return candidates.FirstOrDefault(p => p.Conditions is { Count: > 0 } && Matches(p.Conditions, context))
            ?? candidates.FirstOrDefault(p => p.Conditions is not { Count: > 0 });
    }

    /// <summary>Whether the request meets every condition (names and operators case-insensitive).</summary>
    public static bool Matches(IEnumerable<JourneyPolicyCondition> conditions, JourneyPolicyMatchContext context) =>
        conditions.All(condition => Matches(condition, ValueOf(condition.Type, context)));

    private static string? ValueOf(string type, JourneyPolicyMatchContext context) => type.ToLowerInvariant() switch
    {
        "clientid" or "client_id" => context.ClientId,
        "tenantid" or "tenant_id" => context.TenantId,
        "acrvalue" or "acrvalues" or "acr_values" => context.AcrValues,
        "scope" or "scopes" => context.Scopes is { Count: > 0 } ? string.Join(" ", context.Scopes) : null,
        _ => context.AdditionalParameters?
            .FirstOrDefault(p => string.Equals(p.Key, type, StringComparison.OrdinalIgnoreCase)).Value
    };

    private static bool Matches(JourneyPolicyCondition condition, string? value)
    {
        var expected = condition.Value ?? string.Empty;
        return condition.Operator.ToLowerInvariant() switch
        {
            "eq" or "equals" => string.Equals(value, expected, StringComparison.OrdinalIgnoreCase),
            "ne" or "not_equals" => !string.Equals(value, expected, StringComparison.OrdinalIgnoreCase),
            "contains" => value?.Contains(expected, StringComparison.OrdinalIgnoreCase) ?? false,
            "starts_with" or "startswith" => value?.StartsWith(expected, StringComparison.OrdinalIgnoreCase) ?? false,
            "ends_with" or "endswith" => value?.EndsWith(expected, StringComparison.OrdinalIgnoreCase) ?? false,
            "exists" or "not_empty" => !string.IsNullOrEmpty(value),
            "not_exists" or "empty" => string.IsNullOrEmpty(value),
            "in" => value != null && List(expected).Contains(value, StringComparer.OrdinalIgnoreCase),
            "not_in" => value == null || !List(expected).Contains(value, StringComparer.OrdinalIgnoreCase),
            "regex" or "matches" => value != null && IsMatch(value, expected),
            _ => false
        };
    }

    private static string[] List(string values) =>
        values.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);

    private static bool IsMatch(string value, string pattern)
    {
        try
        {
            return Regex.IsMatch(value, pattern, RegexOptions.IgnoreCase | RegexOptions.CultureInvariant, RegexTimeout);
        }
        catch (Exception ex) when (ex is ArgumentException or RegexMatchTimeoutException)
        {
            return false;
        }
    }
}
