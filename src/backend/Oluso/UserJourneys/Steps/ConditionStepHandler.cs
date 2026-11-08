using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Oluso.Core.Conditions;
using Oluso.Core.UserJourneys;

namespace Oluso.UserJourneys.Steps;

/// <summary>
/// Evaluates conditions and branches flow accordingly.
/// This is a logic step that does not show any UI.
/// </summary>
/// <remarks>
/// Configuration options:
/// - conditions: the same conditions as step conditions (type, field, operator, value,
///   logicalOperator, negate), evaluated left to right
/// - onTrue / onFalse: step to continue to (a step id, or a key of the step's branches);
///   without onTrue the journey continues, without onFalse this step is skipped
///
/// Sources (type): data, claim, context, user, input, config, path
/// </remarks>
public class ConditionStepHandler : IStepHandler
{
    public string StepType => "condition";

    public Task<StepConfigurationValidationResult> ValidateConfigurationAsync(IDictionary<string, object>? configuration)
    {
        var conditions = configuration is not null && configuration.TryGetValue("conditions", out var raw) ? raw : null;
        if (!StepConfigurationHelpers.IsNonEmptyList(conditions))
        {
            return Task.FromResult(StepConfigurationValidationResult.Invalid("At least one condition is required"));
        }
        return Task.FromResult(StepConfigurationValidationResult.Valid());
    }

    public async Task<StepHandlerResult> ExecuteAsync(StepExecutionContext context, CancellationToken cancellationToken = default)
    {
        var logger = context.ServiceProvider.GetRequiredService<ILogger<ConditionStepHandler>>();

        var conditions = context.GetConfig<List<StepCondition>>("conditions", []) ?? [];
        var onTrue = context.GetConfig<string?>("onTrue", null);
        var onFalse = context.GetConfig<string?>("onFalse", null);

        if (conditions.Count == 0)
        {
            logger.LogWarning("Condition step {StepId} has no conditions, skipping", context.StepId);
            return StepHandlerResult.Skip();
        }

        // The same conditions, sources and combining as step conditions use; this step can
        // also read its own input and settings.
        var values = await JourneyConditionValues.ForStepAsync(context, conditions.Select(c => c.Type), cancellationToken);
        var finalResult = DefaultConditionEvaluator.Evaluate(conditions, values);

        logger.LogDebug("Condition step {StepId} evaluated {Count} conditions to {Result}", context.StepId, conditions.Count, finalResult);

        if (finalResult)
        {
            if (!string.IsNullOrEmpty(onTrue))
            {
                return StepHandlerResult.Branch(onTrue);
            }
            return StepHandlerResult.Success(new Dictionary<string, object>
            {
                ["condition_result"] = "true"
            });
        }
        else
        {
            if (!string.IsNullOrEmpty(onFalse))
            {
                return StepHandlerResult.Branch(onFalse);
            }
            // No onFalse defined - skip to next step
            return StepHandlerResult.Skip();
        }
    }
}
