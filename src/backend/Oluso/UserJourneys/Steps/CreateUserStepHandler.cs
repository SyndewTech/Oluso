using Oluso.Core.UserJourneys;

namespace Oluso.UserJourneys.Steps;

/// <summary>
/// Alias for SignUpStepHandler - handles "create_user" step type.
/// This allows policies to use either "signup" or "create_user" for user registration.
/// </summary>
public class CreateUserStepHandler : IStepHandler
{
    private readonly SignUpStepHandler _signUpHandler = new();

    public string StepType => "create_user";

    public Task<StepHandlerResult> ExecuteAsync(StepExecutionContext context, CancellationToken cancellationToken = default)
    {
        return _signUpHandler.ExecuteAsync(context, cancellationToken);
    }
}
