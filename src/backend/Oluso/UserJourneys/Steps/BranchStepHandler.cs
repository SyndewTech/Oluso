using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Oluso.Core.UserJourneys;

namespace Oluso.UserJourneys.Steps;

/// <summary>
/// Handles branching logic with multiple branches and complex rule evaluation.
/// Unlike Condition which provides simple if/then branching, Branch supports
/// multiple branches with priority-based or first-match evaluation.
/// </summary>
/// <remarks>
/// Configuration options:
/// - branches: array of branch rules (name, targetStepId, conditions, priority, logicOperator)
/// - defaultBranch: step ID to branch to if no conditions match
/// - evaluationMode: "first-match" (default) or "highest-priority"
/// </remarks>
public class BranchStepHandler : IStepHandler
{
    public string StepType => "branch";

    public Task<StepHandlerResult> ExecuteAsync(StepExecutionContext context, CancellationToken cancellationToken = default)
    {
        var logger = context.ServiceProvider.GetRequiredService<ILogger<BranchStepHandler>>();

        var branches = context.GetConfig<List<BranchRule>>("branches", new());
        var defaultBranch = context.GetConfig<string?>("defaultBranch", null);
        var evaluationMode = context.GetConfig("evaluationMode", "first-match");

        if (branches.Count == 0 && string.IsNullOrEmpty(defaultBranch))
        {
            logger.LogWarning("Branch step has no branches configured and no default, continuing to next step");
            return Task.FromResult(StepHandlerResult.Success());
        }

        // Sort by priority if using that mode
        if (evaluationMode.Equals("highest-priority", StringComparison.OrdinalIgnoreCase))
        {
            branches = branches.OrderByDescending(b => b.Priority).ToList();
        }

        // Evaluate each branch
        foreach (var branch in branches)
        {
            if (EvaluateBranchConditions(branch, context, logger))
            {
                logger.LogDebug("Branch '{BranchName}' matched, branching to step {StepId}",
                    branch.Name ?? branch.TargetStepId, branch.TargetStepId);

                context.SetData("_branch_taken", branch.Name ?? branch.TargetStepId);

                return Task.FromResult(StepHandlerResult.Branch(branch.TargetStepId, new Dictionary<string, object>
                {
                    ["branch_name"] = branch.Name ?? branch.TargetStepId,
                    ["branch_reason"] = branch.Description ?? "Condition matched"
                }));
            }
        }

        // No branch matched
        if (!string.IsNullOrEmpty(defaultBranch))
        {
            logger.LogDebug("No branch conditions matched, using default branch: {StepId}", defaultBranch);
            return Task.FromResult(StepHandlerResult.Branch(defaultBranch, new Dictionary<string, object>
            {
                ["branch_name"] = "default",
                ["branch_reason"] = "No conditions matched"
            }));
        }

        // Continue to next step
        logger.LogDebug("No branch conditions matched and no default, continuing to next step");
        return Task.FromResult(StepHandlerResult.Success());
    }

    private bool EvaluateBranchConditions(BranchRule branch, StepExecutionContext context, ILogger logger)
    {
        if (branch.Conditions == null || branch.Conditions.Count == 0)
        {
            // No conditions means always match (useful for default-like branches at end)
            return true;
        }

        var logicOperator = branch.LogicOperator?.ToLower() ?? "and";

        if (logicOperator == "or")
        {
            return branch.Conditions.Any(c => EvaluateCondition(c, context, logger));
        }
        else // "and" or default
        {
            return branch.Conditions.All(c => EvaluateCondition(c, context, logger));
        }
    }

    private bool EvaluateCondition(BranchCondition condition, StepExecutionContext context, ILogger logger)
    {
        var actualValue = GetConditionValue(condition, context);

        var result = condition.Operator?.ToLower() switch
        {
            "equals" or "eq" or "==" => string.Equals(actualValue, condition.Value, StringComparison.OrdinalIgnoreCase),
            "notequals" or "neq" or "!=" => !string.Equals(actualValue, condition.Value, StringComparison.OrdinalIgnoreCase),
            "contains" => actualValue?.Contains(condition.Value ?? "", StringComparison.OrdinalIgnoreCase) ?? false,
            "notcontains" => !(actualValue?.Contains(condition.Value ?? "", StringComparison.OrdinalIgnoreCase) ?? false),
            "startswith" => actualValue?.StartsWith(condition.Value ?? "", StringComparison.OrdinalIgnoreCase) ?? false,
            "endswith" => actualValue?.EndsWith(condition.Value ?? "", StringComparison.OrdinalIgnoreCase) ?? false,
            "matches" or "regex" => !string.IsNullOrEmpty(actualValue) && System.Text.RegularExpressions.Regex.IsMatch(actualValue, condition.Value ?? ""),
            "exists" or "notnull" => !string.IsNullOrEmpty(actualValue),
            "notexists" or "null" or "empty" => string.IsNullOrEmpty(actualValue),
            "in" => condition.Values?.Contains(actualValue, StringComparer.OrdinalIgnoreCase) ?? false,
            "notin" => !(condition.Values?.Contains(actualValue, StringComparer.OrdinalIgnoreCase) ?? false),
            "gt" or ">" => CompareNumeric(actualValue, condition.Value, (a, b) => a > b),
            "gte" or ">=" => CompareNumeric(actualValue, condition.Value, (a, b) => a >= b),
            "lt" or "<" => CompareNumeric(actualValue, condition.Value, (a, b) => a < b),
            "lte" or "<=" => CompareNumeric(actualValue, condition.Value, (a, b) => a <= b),
            "true" => actualValue?.Equals("true", StringComparison.OrdinalIgnoreCase) ?? false,
            "false" => actualValue?.Equals("false", StringComparison.OrdinalIgnoreCase) ?? false,
            _ => string.Equals(actualValue, condition.Value, StringComparison.OrdinalIgnoreCase)
        };

        if (condition.Negate)
        {
            result = !result;
        }

        logger.LogTrace("Branch condition: {Source}.{Field} {Operator} '{Expected}' = {Result} (actual: '{Actual}')",
            condition.Source, condition.Field, condition.Operator, condition.Value, result, actualValue);

        return result;
    }

    private string? GetConditionValue(BranchCondition condition, StepExecutionContext context)
    {
        return condition.Source?.ToLower() switch
        {
            "claim" or "claims" or "data" => context.GetData<string>(condition.Field ?? ""),
            "input" => context.GetInput(condition.Field ?? ""),
            "config" => context.GetConfig<string>(condition.Field ?? "", null),
            "user" => GetUserValue(condition.Field, context),
            _ => context.GetData<string>(condition.Field ?? "")
        };
    }

    private static string? GetUserValue(string? field, StepExecutionContext context)
    {
        if (string.IsNullOrEmpty(field)) return null;

        return field.ToLower() switch
        {
            "id" or "userid" => context.UserId,
            "authenticated" or "isauthenticated" => (!string.IsNullOrEmpty(context.UserId)).ToString().ToLower(),
            "email" => context.GetData<string>("email"),
            "name" => context.GetData<string>("name"),
            _ => context.GetData<string>(field)
        };
    }

    private static bool CompareNumeric(string? actual, string? expected, Func<decimal, decimal, bool> comparison)
    {
        if (!decimal.TryParse(actual, out var actualNum) ||
            !decimal.TryParse(expected, out var expectedNum))
        {
            return false;
        }
        return comparison(actualNum, expectedNum);
    }
}

#region Configuration Models

/// <summary>
/// A branch rule with target step and conditions
/// </summary>
public class BranchRule
{
    /// <summary>
    /// Optional name for the branch (for logging/debugging)
    /// </summary>
    public string? Name { get; set; }

    /// <summary>
    /// Description of what this branch does
    /// </summary>
    public string? Description { get; set; }

    /// <summary>
    /// Target step ID to branch to
    /// </summary>
    public string TargetStepId { get; set; } = null!;

    /// <summary>
    /// Priority for evaluation (higher = evaluated first in highest-priority mode)
    /// </summary>
    public int Priority { get; set; }

    /// <summary>
    /// Conditions that must be met for this branch
    /// </summary>
    public List<BranchCondition> Conditions { get; set; } = new();

    /// <summary>
    /// How to combine conditions: "and" (all must match) or "or" (any must match)
    /// </summary>
    public string? LogicOperator { get; set; } = "and";
}

/// <summary>
/// A condition within a branch rule
/// </summary>
public class BranchCondition
{
    /// <summary>
    /// Source of the value: claim, data, input, config, user
    /// </summary>
    public string? Source { get; set; } = "data";

    /// <summary>
    /// Field name to evaluate
    /// </summary>
    public string? Field { get; set; }

    /// <summary>
    /// Comparison operator
    /// </summary>
    public string? Operator { get; set; } = "equals";

    /// <summary>
    /// Expected value for comparison
    /// </summary>
    public string? Value { get; set; }

    /// <summary>
    /// List of allowed values (for "in" and "notin" operators)
    /// </summary>
    public List<string>? Values { get; set; }

    /// <summary>
    /// Negate the result of this condition
    /// </summary>
    public bool Negate { get; set; }
}

#endregion
