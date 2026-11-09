using Oluso.Core.Conditions;

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

    /// <summary>
    /// The journey user's claims, loaded only when a condition reads the "claim" source.
    /// </summary>
    public IReadOnlyDictionary<string, IReadOnlyList<string>>? Claims { get; init; }

    /// <summary>
    /// The journey's user, loaded only when a condition reads the "user" source.
    /// Read through <see cref="UserConditionFields"/>; never stored.
    /// </summary>
    public Services.OlusoUserInfo? User { get; init; }
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

        var values = new JourneyConditionValues
        {
            JourneyData = context.JourneyData,
            UserId = context.UserId,
            TenantId = context.TenantId,
            ClientId = context.ClientId,
            User = context.User,
            Claims = context.Claims
        };

        return Task.FromResult(Evaluate(conditions, values));
    }

    /// <summary>
    /// Conditions evaluated left to right: each joins the result so far with the logical
    /// operator of the condition before it. Shared by step conditions and the condition step.
    /// </summary>
    public static bool Evaluate(IEnumerable<StepCondition> conditions, JourneyConditionValues values)
    {
        var result = true;
        var currentLogicalOp = "and";

        foreach (var condition in conditions)
        {
            var conditionResult = ConditionOperators.Evaluate(
                condition.Operator, values.Get(condition.Type, condition.Field), condition.Value);

            if (condition.Negate)
                conditionResult = !conditionResult;

            result = currentLogicalOp switch
            {
                "or" => result || conditionResult,
                _ => result && conditionResult
            };

            currentLogicalOp = (condition.LogicalOperator ?? "and").ToLowerInvariant();
        }

        return result;
    }
}
