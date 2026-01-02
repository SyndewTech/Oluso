using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.Extensions.Logging;
using Oluso.Core.UserJourneys;

namespace Oluso.UI.Pages.Journey;

/// <summary>
/// Starts a journey from a policy ID. This is the entry point for data collection
/// journeys (waitlists, surveys, contact forms) and can also be used to start
/// authentication journeys directly without going through OIDC.
/// </summary>
public class StartModel : PageModel
{
    private readonly IJourneyOrchestrator _orchestrator;
    private readonly IJourneyPolicyStore _policyStore;
    private readonly IJourneySubmissionStore? _submissionStore;
    private readonly ILogger<StartModel> _logger;

    public StartModel(
        IJourneyOrchestrator orchestrator,
        IJourneyPolicyStore policyStore,
        ILogger<StartModel> logger,
        IJourneySubmissionStore? submissionStore = null)
    {
        _orchestrator = orchestrator;
        _policyStore = policyStore;
        _submissionStore = submissionStore;
        _logger = logger;
    }

    [BindProperty(SupportsGet = true)]
    public string PolicyId { get; set; } = null!;

    public string? ErrorMessage { get; private set; }

    public async Task<IActionResult> OnGetAsync()
    {
        if (string.IsNullOrEmpty(PolicyId))
        {
            return RedirectToPage("/Error", new { message = "Policy ID is required" });
        }

        // Look up the policy
        var policy = await _policyStore.GetByIdAsync(PolicyId);
        if (policy == null)
        {
            _logger.LogWarning("Policy {PolicyId} not found", PolicyId);
            return RedirectToPage("/Error", new { message = "Journey not found" });
        }

        if (!policy.Enabled)
        {
            _logger.LogWarning("Policy {PolicyId} is disabled", PolicyId);
            return RedirectToPage("/Error", new { message = "This journey is currently unavailable" });
        }

        // Check if max submissions reached (for data collection journeys)
        if (policy.MaxSubmissions > 0 && _submissionStore != null)
        {
            var count = await _submissionStore.CountByPolicyAsync(PolicyId);
            if (count >= policy.MaxSubmissions)
            {
                _logger.LogInformation("Max submissions ({Max}) reached for policy {PolicyId}",
                    policy.MaxSubmissions, PolicyId);
                return RedirectToPage("/Error", new { message = "This form is no longer accepting submissions" });
            }
        }

        try
        {
            // Start a new journey with context properties
            var properties = new Dictionary<string, object>
            {
                ["TenantId"] = policy.TenantId ?? "",
                ["ClientId"] = "" // Data collection journeys don't require a client
            };

            var state = await _orchestrator.StartJourneyAsync(policy, new JourneyStartContext
            {
                HttpContext = HttpContext,
                CallbackUrl = policy.SuccessRedirectUrl,
                Properties = properties
            });

            _logger.LogInformation("Started journey {JourneyId} for policy {PolicyId} ({PolicyName})",
                state.Id, PolicyId, policy.Name);

            // Redirect to the journey page
            return RedirectToPage("Index", new { journeyId = state.Id });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to start journey for policy {PolicyId}", PolicyId);
            return RedirectToPage("/Error", new { message = "Failed to start journey" });
        }
    }
}
