using Oluso.Core.UserJourneys;

namespace Oluso.UserJourneys.Steps;

/// <summary>
/// Claims Collection step handler - an alias for DynamicFormStepHandler.
/// This allows the "claims_collection" step type to be used in journey policies.
/// </summary>
/// <remarks>
/// Claims Collection and Dynamic Form are functionally identical.
/// "claims_collection" is the semantic name for collecting user claims/data during a journey.
/// "dynamic_form" is the technical name for the form rendering mechanism.
/// </remarks>
public class ClaimsCollectionStepHandler : IStepHandler
{
    private readonly DynamicFormStepHandler _innerHandler = new();

    public string StepType => "claims_collection";

    public Task<StepHandlerResult> ExecuteAsync(StepExecutionContext context, CancellationToken cancellationToken = default)
    {
        return _innerHandler.ExecuteAsync(context, cancellationToken);
    }
}
