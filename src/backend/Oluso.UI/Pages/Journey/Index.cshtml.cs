using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Oluso.Core.Domain.Entities;
using Oluso.Core.UserJourneys;

namespace Oluso.UI.Pages.Journey;

/// <summary>
/// Main Journey page model - orchestrates the authentication flow.
/// This is the default implementation that ships with Oluso.UI.
/// </summary>
public class IndexModel : PageModel
{
    private readonly IJourneyOrchestrator _orchestrator;
    private readonly IJourneyStateStore _stateStore;
    private readonly IJourneyPolicyStore _policyStore;
    private readonly IServiceProvider _serviceProvider;
    private readonly ILogger<IndexModel> _logger;

    public IndexModel(
        IJourneyOrchestrator orchestrator,
        IJourneyStateStore stateStore,
        IJourneyPolicyStore policyStore,
        IServiceProvider serviceProvider,
        ILogger<IndexModel> logger)
    {
        _orchestrator = orchestrator;
        _stateStore = stateStore;
        _policyStore = policyStore;
        _serviceProvider = serviceProvider;
        _logger = logger;
    }

    [BindProperty(SupportsGet = true)]
    public string JourneyId { get; set; } = null!;

    public JourneyState? State { get; private set; }
    public string? CurrentStepView { get; private set; }
    public object? StepViewModel { get; private set; }
    public string? ErrorMessage { get; private set; }
    public string? Title { get; private set; }

    /// <summary>
    /// UI configuration from the journey policy (for custom styling)
    /// </summary>
    public JourneyUiConfiguration? UiConfig { get; private set; }

    /// <summary>
    /// Progress information for the journey
    /// </summary>
    public JourneyProgressInfo? Progress { get; private set; }

    public async Task<IActionResult> OnGetAsync()
    {
        if (string.IsNullOrEmpty(JourneyId))
        {
            return RedirectToPage("/Error", new { message = "Invalid journey" });
        }

        var state = await _stateStore.GetAsync(JourneyId);
        if (state == null)
        {
            _logger.LogWarning("Journey {JourneyId} not found", JourneyId);
            return RedirectToPage("/Error", new { message = "Journey not found or expired" });
        }

        if (state.ExpiresAt.HasValue && state.ExpiresAt < DateTime.UtcNow)
        {
            _logger.LogWarning("Journey {JourneyId} has expired", JourneyId);
            return RedirectToPage("/Error", new { message = "Your session has expired. Please try again." });
        }

        State = state;

        // Load the policy to get UI configuration
        await LoadUiConfigurationAsync(state.PolicyId);

        // Get the current step result from journey data
        var result = await _orchestrator.GetStateAsync(JourneyId);
        if (result != null)
        {
            State = result;
            // The view and model should come from continuing the journey
            var continueResult = await _orchestrator.ContinueJourneyAsync(JourneyId, new JourneyStepInput
            {
                StepId = result.CurrentStepId,
                Action = "init"
            });

            return await HandleJourneyResultAsync(continueResult);
        }

        return Page();
    }

    private async Task LoadUiConfigurationAsync(string policyId)
    {
        try
        {
            var policy = await _policyStore.GetByIdAsync(policyId);
            UiConfig = policy?.Ui;
            Title = UiConfig?.Title ?? policy?.Name;

            // Calculate progress based on policy steps
            if (policy?.Steps != null && State != null)
            {
                var steps = policy.Steps.OrderBy(s => s.Order).ToList();
                var currentStepIndex = steps.FindIndex(s => s.Id == State.CurrentStepId);

                Progress = new JourneyProgressInfo
                {
                    TotalSteps = steps.Count,
                    CurrentStepIndex = currentStepIndex >= 0 ? currentStepIndex : 0,
                    CompletedSteps = currentStepIndex >= 0 ? currentStepIndex : 0,
                    CurrentStepName = steps.ElementAtOrDefault(currentStepIndex)?.DisplayName ?? "Authentication"
                };
            }
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to load UI configuration for policy {PolicyId}", policyId);
        }
    }

    public async Task<IActionResult> OnPostAsync()
    {
        if (string.IsNullOrEmpty(JourneyId))
        {
            return RedirectToPage("/Error", new { message = "Invalid journey" });
        }

        // Get current state
        var state = await _stateStore.GetAsync(JourneyId);
        if (state == null)
        {
            return RedirectToPage("/Error", new { message = "Journey not found or expired" });
        }

        // Collect all form data as user input
        // Allow __submitted and __cancel for form handlers, but exclude ASP.NET internal fields
        var userInput = Request.Form
            .Where(f => !f.Key.StartsWith("__RequestVerification") && !f.Key.StartsWith("__VIEWSTATE"))
            .ToDictionary(f => f.Key, f => (object)f.Value.ToString());

        _logger.LogDebug("Journey {JourneyId} received user input: {InputKeys}",
            JourneyId, string.Join(", ", userInput.Keys));

        // Continue the journey with user input
        var result = await _orchestrator.ContinueJourneyAsync(JourneyId, new JourneyStepInput
        {
            StepId = state.CurrentStepId,
            Values = userInput
        });

        return await HandleJourneyResultAsync(result);
    }

    /// <summary>
    /// Handle callback from external identity provider
    /// </summary>
    public async Task<IActionResult> OnGetCallbackAsync()
    {
        if (string.IsNullOrEmpty(JourneyId))
        {
            return RedirectToPage("/Error", new { message = "Invalid journey" });
        }

        _logger.LogDebug("Journey {JourneyId} received external callback", JourneyId);

        var state = await _stateStore.GetAsync(JourneyId);
        if (state == null)
        {
            return RedirectToPage("/Error", new { message = "Journey not found or expired" });
        }

        // Continue the journey (the external IdP step handler will process the auth result)
        var result = await _orchestrator.ContinueJourneyAsync(JourneyId, new JourneyStepInput
        {
            StepId = state.CurrentStepId,
            Action = "callback"
        });

        return await HandleJourneyResultAsync(result);
    }

    private async Task<IActionResult> HandleJourneyResultAsync(JourneyResult result)
    {
        switch (result.Status)
        {
            case JourneyStatus.InProgress:
                if (result.CurrentStep != null)
                {
                    CurrentStepView = result.CurrentStep.ViewName;
                    StepViewModel = result.CurrentStep.ViewModel;
                }
                return Page();

            case JourneyStatus.Completed:
                // Journey completed successfully - issue session if login happened
                return await RedirectToJourneyCompletionAsync(result);

            case JourneyStatus.Failed:
                _logger.LogWarning("Journey {JourneyId} failed: {ErrorCode} - {ErrorMessage}",
                    JourneyId, result.Error, result.ErrorDescription);
                return RedirectToPage("/Error", new
                {
                    error = result.Error,
                    message = result.ErrorDescription
                });

            case JourneyStatus.Cancelled:
                return RedirectToPage("/Error", new { message = "Authentication was cancelled" });

            case JourneyStatus.Expired:
                return RedirectToPage("/Error", new { message = "Your session has expired. Please try again." });

            default:
                return RedirectToPage("/Error", new { message = "Unknown error" });
        }
    }

    private async Task<IActionResult> RedirectToJourneyCompletionAsync(JourneyResult result)
    {
        // Store completion info for the callback handler
        TempData["CompletedJourneyId"] = result.JourneyId;

        // Check if authentication actually happened during this journey
        // by looking for the authenticated_at flag set by login steps
        var shouldIssueSession = await ShouldIssueSessionAsync(result);

        if (shouldIssueSession && !string.IsNullOrEmpty(result.Completion?.UserId))
        {
            await IssueSessionAsync(result.Completion.UserId, result.Completion.Claims);
        }

        // If there's a redirect URI in the completion, use it
        if (!string.IsNullOrEmpty(result.Completion?.RedirectUri))
        {
            return Redirect(result.Completion.RedirectUri);
        }

        // For data collection journeys (no redirect URI, has success message or no user),
        // show the success page instead of redirecting to OIDC callback
        var isDataCollectionJourney = string.IsNullOrEmpty(result.Completion?.UserId) ||
                                       !string.IsNullOrEmpty(result.Completion?.SuccessMessage);
        if (isDataCollectionJourney)
        {
            return RedirectToPage("Success", new { journeyId = result.JourneyId });
        }

        // Default: redirect to authorize callback (for OIDC authentication flows)
        var callbackUrl = $"/connect/authorize/callback?journey_id={result.JourneyId}";
        return Redirect(callbackUrl);
    }

    /// <summary>
    /// Determines if a session should be issued based on whether a login step actually
    /// authenticated the user during this journey. This allows JourneyType.SignIn to be
    /// used for non-login flows without accidentally issuing sessions.
    /// </summary>
    private async Task<bool> ShouldIssueSessionAsync(JourneyResult result)
    {
        // Get the journey state to check for authentication flags
        var state = await _stateStore.GetAsync(result.JourneyId);
        if (state?.Data == null)
        {
            return false;
        }

        // Check if any login step set the authenticated_at flag
        // This flag is set by LocalLoginStepHandler, ExternalLoginStepHandler,
        // CompositeLoginStepHandler, etc. when authentication succeeds
        return state.Data.ContainsKey("authenticated_at");
    }

    /// <summary>
    /// Issues a cookie session for the authenticated user.
    /// </summary>
    private async Task IssueSessionAsync(string userId, IReadOnlyDictionary<string, object>? claims)
    {
        var signInManager = _serviceProvider.GetService<SignInManager<OlusoUser>>();
        var userManager = _serviceProvider.GetService<UserManager<OlusoUser>>();

        if (signInManager == null || userManager == null)
        {
            _logger.LogDebug("SignInManager or UserManager not available, skipping session issuance");
            return;
        }

        var user = await userManager.FindByIdAsync(userId);
        if (user == null)
        {
            _logger.LogWarning("Cannot issue session: user {UserId} not found", userId);
            return;
        }

        // Sign in the user using SignInManager which will use OlusoClaimsPrincipalFactory
        // to build the claims principal with all plugin claims, tenant claims, etc.
        await signInManager.SignInAsync(user, isPersistent: false);

        _logger.LogInformation("Session issued for user {UserId} after journey completion", userId);
    }
}

/// <summary>
/// Progress information for display in the journey UI
/// </summary>
public class JourneyProgressInfo
{
    public int TotalSteps { get; init; }
    public int CurrentStepIndex { get; init; }
    public int CompletedSteps { get; init; }
    public string CurrentStepName { get; init; } = "Authentication";

    public int PercentComplete => TotalSteps > 0 ? (int)((double)CurrentStepIndex / TotalSteps * 100) : 0;
    public bool ShowProgress => TotalSteps > 1;
}
