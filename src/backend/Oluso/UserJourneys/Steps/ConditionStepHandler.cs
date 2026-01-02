using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Oluso.Core.UserJourneys;

namespace Oluso.UserJourneys.Steps;

/// <summary>
/// Evaluates conditions and branches flow accordingly.
/// This is a logic step that does not show any UI.
/// </summary>
/// <remarks>
/// Configuration options:
/// - conditions: array of condition objects (source, key, operator, value, negate)
/// - combineWith: "and" | "or" (default: "and")
/// - onTrue: step ID to go to if condition is true
/// - onFalse: step ID to go to if condition is false
///
/// Condition sources: claim, input, data, config
/// Operators: equals, notequals, contains, startswith, endswith, exists, notexists, gt, gte, lt, lte, in, notin, matches
/// </remarks>
public class ConditionStepHandler : IStepHandler
{
    public string StepType => "condition";

    public Task<StepHandlerResult> ExecuteAsync(StepExecutionContext context, CancellationToken cancellationToken = default)
    {
        var logger = context.ServiceProvider.GetRequiredService<ILogger<ConditionStepHandler>>();

        var conditions = context.GetConfig<List<ConditionConfig>>("conditions", new());
        var combineWith = context.GetConfig("combineWith", "and");
        var onTrue = context.GetConfig<string?>("onTrue", null);
        var onFalse = context.GetConfig<string?>("onFalse", null);

        if (conditions.Count == 0)
        {
            logger.LogWarning("Condition step has no conditions defined, skipping");
            return Task.FromResult(StepHandlerResult.Skip());
        }

        var results = conditions.Select(c => EvaluateCondition(c, context, logger)).ToList();
        var finalResult = combineWith.Equals("or", StringComparison.OrdinalIgnoreCase)
            ? results.Any(r => r)
            : results.All(r => r);

        logger.LogDebug("Condition step evaluated to {Result} (combining {Count} conditions with {Combiner})",
            finalResult, conditions.Count, combineWith);

        if (finalResult)
        {
            if (!string.IsNullOrEmpty(onTrue))
            {
                return Task.FromResult(StepHandlerResult.Branch(onTrue));
            }
            return Task.FromResult(StepHandlerResult.Success(new Dictionary<string, object>
            {
                ["condition_result"] = "true"
            }));
        }
        else
        {
            if (!string.IsNullOrEmpty(onFalse))
            {
                return Task.FromResult(StepHandlerResult.Branch(onFalse));
            }
            // No onFalse defined - skip to next step
            return Task.FromResult(StepHandlerResult.Skip());
        }
    }

    private bool EvaluateCondition(ConditionConfig condition, StepExecutionContext context, ILogger logger)
    {
        var sourceValue = GetSourceValue(condition.Source, condition.Key, context);
        var result = Compare(sourceValue, condition.Operator, condition.Value);

        if (condition.Negate)
        {
            result = !result;
        }

        logger.LogTrace("Condition: {Source}.{Key} {Operator} '{Value}' = {Result} (actual: '{Actual}')",
            condition.Source, condition.Key, condition.Operator, condition.Value, result, sourceValue);

        return result;
    }

    private string? GetSourceValue(string source, string key, StepExecutionContext context)
    {
        return source.ToLower() switch
        {
            "claim" or "claims" => context.JourneyData.TryGetValue(key, out var claimVal) ? claimVal?.ToString() : null,
            "input" => context.GetInput(key),
            "data" or "state" => context.GetData<string>(key),
            "config" => context.GetConfig<string>(key, null),
            "user" => GetUserValue(key, context),
            _ => context.GetData<string>(key)
        };
    }

    private string? GetUserValue(string key, StepExecutionContext context)
    {
        return key.ToLower() switch
        {
            "id" or "userid" => context.UserId,
            "authenticated" => (!string.IsNullOrEmpty(context.UserId)).ToString().ToLower(),
            _ => context.GetData<string>(key)
        };
    }

    private bool Compare(string? sourceValue, string op, string targetValue)
    {
        return op.ToLower() switch
        {
            "equals" or "eq" or "==" => string.Equals(sourceValue, targetValue, StringComparison.OrdinalIgnoreCase),
            "notequals" or "neq" or "!=" => !string.Equals(sourceValue, targetValue, StringComparison.OrdinalIgnoreCase),
            "contains" => sourceValue?.Contains(targetValue, StringComparison.OrdinalIgnoreCase) ?? false,
            "notcontains" => !(sourceValue?.Contains(targetValue, StringComparison.OrdinalIgnoreCase) ?? false),
            "startswith" => sourceValue?.StartsWith(targetValue, StringComparison.OrdinalIgnoreCase) ?? false,
            "endswith" => sourceValue?.EndsWith(targetValue, StringComparison.OrdinalIgnoreCase) ?? false,
            "exists" or "isset" or "notnull" => !string.IsNullOrEmpty(sourceValue),
            "notexists" or "isnotset" or "null" or "empty" => string.IsNullOrEmpty(sourceValue),
            "gt" or ">" => CompareNumeric(sourceValue, targetValue, (a, b) => a > b),
            "gte" or ">=" => CompareNumeric(sourceValue, targetValue, (a, b) => a >= b),
            "lt" or "<" => CompareNumeric(sourceValue, targetValue, (a, b) => a < b),
            "lte" or "<=" => CompareNumeric(sourceValue, targetValue, (a, b) => a <= b),
            "in" => targetValue.Split(',').Select(s => s.Trim()).Contains(sourceValue, StringComparer.OrdinalIgnoreCase),
            "notin" => !targetValue.Split(',').Select(s => s.Trim()).Contains(sourceValue, StringComparer.OrdinalIgnoreCase),
            "matches" or "regex" => !string.IsNullOrEmpty(sourceValue) && System.Text.RegularExpressions.Regex.IsMatch(sourceValue, targetValue),
            "true" => sourceValue?.Equals("true", StringComparison.OrdinalIgnoreCase) ?? false,
            "false" => sourceValue?.Equals("false", StringComparison.OrdinalIgnoreCase) ?? false,
            _ => string.Equals(sourceValue, targetValue, StringComparison.OrdinalIgnoreCase)
        };
    }

    private static bool CompareNumeric(string? sourceValue, string targetValue, Func<decimal, decimal, bool> comparison)
    {
        if (!decimal.TryParse(sourceValue, out var sv) ||
            !decimal.TryParse(targetValue, out var tv))
        {
            return false;
        }
        return comparison(sv, tv);
    }
}

/// <summary>
/// Configuration for a single condition
/// </summary>
public class ConditionConfig
{
    /// <summary>
    /// Source of the value: claim, input, data, config, user
    /// </summary>
    public string Source { get; set; } = "data";

    /// <summary>
    /// Key/name to look up in the source
    /// </summary>
    public string Key { get; set; } = null!;

    /// <summary>
    /// Comparison operator
    /// </summary>
    public string Operator { get; set; } = "equals";

    /// <summary>
    /// Value to compare against
    /// </summary>
    public string Value { get; set; } = null!;

    /// <summary>
    /// Whether to negate the result
    /// </summary>
    public bool Negate { get; set; }
}
