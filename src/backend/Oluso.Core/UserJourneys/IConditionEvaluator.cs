using System.Text.RegularExpressions;

namespace Oluso.Core.UserJourneys;

/// <summary>
/// Evaluates step conditions to determine if a step should execute
/// </summary>
public interface IConditionEvaluator
{
    /// <summary>
    /// Evaluates all conditions and returns true if the step should execute
    /// </summary>
    Task<bool> EvaluateConditionsAsync(
        IList<StepCondition> conditions,
        ConditionEvaluationContext context,
        CancellationToken cancellationToken = default);
}

/// <summary>
/// Context provided for condition evaluation
/// </summary>
public class ConditionEvaluationContext
{
    public IDictionary<string, object> JourneyData { get; init; } = new Dictionary<string, object>();
    public string? UserId { get; init; }
    public string? TenantId { get; init; }
    public string? ClientId { get; init; }
    public IReadOnlyDictionary<string, string>? Claims { get; init; }
}

/// <summary>
/// Default condition evaluator implementation
/// </summary>
public class DefaultConditionEvaluator : IConditionEvaluator
{
    public Task<bool> EvaluateConditionsAsync(
        IList<StepCondition> conditions,
        ConditionEvaluationContext context,
        CancellationToken cancellationToken = default)
    {
        if (conditions.Count == 0)
            return Task.FromResult(true);

        // Group conditions by logical operator
        var result = true;
        var currentLogicalOp = "and";

        foreach (var condition in conditions)
        {
            var conditionResult = EvaluateCondition(condition, context);

            // Apply negation if specified
            if (condition.Negate)
                conditionResult = !conditionResult;

            // Combine with previous result based on logical operator
            result = currentLogicalOp switch
            {
                "and" => result && conditionResult,
                "or" => result || conditionResult,
                _ => result && conditionResult
            };

            // Update logical operator for next condition
            currentLogicalOp = condition.LogicalOperator.ToLowerInvariant();
        }

        return Task.FromResult(result);
    }

    private bool EvaluateCondition(StepCondition condition, ConditionEvaluationContext context)
    {
        var value = GetFieldValue(condition.Type, condition.Field, context);

        return condition.Operator.ToLowerInvariant() switch
        {
            "eq" or "equals" => CompareValues(value, condition.Value) == 0,
            "ne" or "not_equals" => CompareValues(value, condition.Value) != 0,
            "gt" or "greater_than" => CompareValues(value, condition.Value) > 0,
            "lt" or "less_than" => CompareValues(value, condition.Value) < 0,
            "gte" or "greater_than_or_equals" => CompareValues(value, condition.Value) >= 0,
            "lte" or "less_than_or_equals" => CompareValues(value, condition.Value) <= 0,
            "contains" => value?.ToString()?.Contains(condition.Value ?? "", StringComparison.OrdinalIgnoreCase) ?? false,
            "starts_with" => value?.ToString()?.StartsWith(condition.Value ?? "", StringComparison.OrdinalIgnoreCase) ?? false,
            "ends_with" => value?.ToString()?.EndsWith(condition.Value ?? "", StringComparison.OrdinalIgnoreCase) ?? false,
            "exists" => value != null,
            "not_exists" => value == null,
            "empty" => string.IsNullOrEmpty(value?.ToString()),
            "not_empty" => !string.IsNullOrEmpty(value?.ToString()),
            "regex" or "matches" => MatchesRegex(value?.ToString(), condition.Value),
            "in" => IsInList(value, condition.Value),
            "not_in" => !IsInList(value, condition.Value),
            "true" => IsTruthy(value),
            "false" => !IsTruthy(value),
            _ => false
        };
    }

    private object? GetFieldValue(string type, string field, ConditionEvaluationContext context)
    {
        return type.ToLowerInvariant() switch
        {
            "journeydata" or "data" =>
                context.JourneyData.TryGetValue(field, out var dataValue) ? dataValue : null,

            "claim" or "claims" =>
                context.Claims?.TryGetValue(field, out var claimValue) == true ? claimValue : null,

            "context" => field.ToLowerInvariant() switch
            {
                "userid" or "user_id" => context.UserId,
                "tenantid" or "tenant_id" => context.TenantId,
                "clientid" or "client_id" => context.ClientId,
                "isauthenticated" or "is_authenticated" => !string.IsNullOrEmpty(context.UserId),
                _ => null
            },

            // Support dot-notation paths like "user.email" in journey data
            "path" => GetNestedValue(context.JourneyData, field),

            _ => null
        };
    }

    private static object? GetNestedValue(IDictionary<string, object> data, string path)
    {
        var parts = path.Split('.');
        object? current = data;

        foreach (var part in parts)
        {
            if (current is IDictionary<string, object> dict)
            {
                if (!dict.TryGetValue(part, out current))
                    return null;
            }
            else
            {
                return null;
            }
        }

        return current;
    }

    private static int CompareValues(object? left, string? right)
    {
        if (left == null && right == null) return 0;
        if (left == null) return -1;
        if (right == null) return 1;

        var leftStr = left.ToString();

        // Try numeric comparison
        if (double.TryParse(leftStr, out var leftNum) && double.TryParse(right, out var rightNum))
        {
            return leftNum.CompareTo(rightNum);
        }

        // Try date comparison
        if (DateTime.TryParse(leftStr, out var leftDate) && DateTime.TryParse(right, out var rightDate))
        {
            return leftDate.CompareTo(rightDate);
        }

        // Fall back to string comparison
        return string.Compare(leftStr, right, StringComparison.OrdinalIgnoreCase);
    }

    private static bool MatchesRegex(string? value, string? pattern)
    {
        if (string.IsNullOrEmpty(value) || string.IsNullOrEmpty(pattern))
            return false;

        try
        {
            return Regex.IsMatch(value, pattern, RegexOptions.IgnoreCase, TimeSpan.FromSeconds(1));
        }
        catch
        {
            return false;
        }
    }

    private static bool IsInList(object? value, string? listValue)
    {
        if (value == null || string.IsNullOrEmpty(listValue))
            return false;

        var items = listValue.Split(',').Select(s => s.Trim());
        var valueStr = value.ToString();

        return items.Any(item => string.Equals(item, valueStr, StringComparison.OrdinalIgnoreCase));
    }

    private static bool IsTruthy(object? value)
    {
        return value switch
        {
            null => false,
            bool b => b,
            string s => !string.IsNullOrEmpty(s) && !s.Equals("false", StringComparison.OrdinalIgnoreCase) && s != "0",
            int i => i != 0,
            double d => d != 0,
            _ => true
        };
    }
}
