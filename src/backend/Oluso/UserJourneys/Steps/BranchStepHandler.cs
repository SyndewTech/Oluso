using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Oluso.Core.Conditions;
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

    public async Task<StepHandlerResult> ExecuteAsync(StepExecutionContext context, CancellationToken cancellationToken = default)
    {
        var logger = context.ServiceProvider.GetRequiredService<ILogger<BranchStepHandler>>();

        var branches = context.GetConfig<List<BranchRule>>("branches", new());
        var defaultBranch = context.GetConfig<string?>("defaultBranch", null);
        var evaluationMode = context.GetConfig("evaluationMode", "first-match");

        if (branches!.Count == 0 && string.IsNullOrEmpty(defaultBranch))
        {
            logger.LogWarning("Branch step has no branches configured and no default, continuing to next step");
            return StepHandlerResult.Success();
        }

        var values = await JourneyConditionValues.ForStepAsync(
            context, branches.SelectMany(b => b.Conditions).Select(c => c.Source), cancellationToken);

        // Sort by priority if using that mode
        if (evaluationMode!.Equals("highest-priority", StringComparison.OrdinalIgnoreCase))
        {
            branches = branches.OrderByDescending(b => b.Priority).ToList();
        }

        // Evaluate each branch
        foreach (var branch in branches)
        {
            if (EvaluateBranchConditions(branch, values, logger))
            {
                logger.LogDebug("Branch '{BranchName}' matched, branching to step {StepId}",
                    branch.Name ?? branch.TargetStepId, branch.TargetStepId);

                context.SetData("_branch_taken", branch.Name ?? branch.TargetStepId);

                return StepHandlerResult.Branch(branch.TargetStepId, new Dictionary<string, object>
                {
                    ["branch_name"] = branch.Name ?? branch.TargetStepId,
                    ["branch_reason"] = branch.Description ?? "Condition matched"
                });
            }
        }

        // No branch matched
        if (!string.IsNullOrEmpty(defaultBranch))
        {
            logger.LogDebug("No branch conditions matched, using default branch: {StepId}", defaultBranch);
            return StepHandlerResult.Branch(defaultBranch, new Dictionary<string, object>
            {
                ["branch_name"] = "default",
                ["branch_reason"] = "No conditions matched"
            });
        }

        // Continue to next step
        logger.LogDebug("No branch conditions matched and no default, continuing to next step");
        return StepHandlerResult.Success();
    }

    private static bool EvaluateBranchConditions(BranchRule branch, JourneyConditionValues values, ILogger logger)
    {
        if (branch.Conditions == null || branch.Conditions.Count == 0)
        {
            // No conditions means always match (useful for default-like branches at end)
            return true;
        }

        var logicOperator = branch.LogicOperator?.ToLower() ?? "and";

        if (logicOperator == "or")
        {
            return branch.Conditions.Any(c => EvaluateCondition(c, values, logger));
        }
        else // "and" or default
        {
            return branch.Conditions.All(c => EvaluateCondition(c, values, logger));
        }
    }

    private static bool EvaluateCondition(BranchCondition condition, JourneyConditionValues values, ILogger logger)
    {
        var result = ConditionOperators.Evaluate(
            condition.Operator, values.Get(condition.Source, condition.Field), condition.Value, condition.Values);

        if (condition.Negate)
        {
            result = !result;
        }

        // Values are not logged: claim and profile sources can hold personal data.
        logger.LogTrace("Branch condition: {Source}.{Field} {Operator} '{Expected}' = {Result}",
            condition.Source, condition.Field, condition.Operator, condition.Value, result);

        return result;
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
    /// Source of the value: data, claim, context, user, input, config, path (see <see cref="JourneyConditionValues"/>)
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
