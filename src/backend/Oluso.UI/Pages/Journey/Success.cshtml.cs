using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.Extensions.Logging;
using Oluso.Core.UserJourneys;

namespace Oluso.UI.Pages.Journey;

/// <summary>
/// Success page shown after completing a data collection journey.
/// </summary>
public class SuccessModel : PageModel
{
    private readonly IJourneyStateStore _stateStore;
    private readonly IJourneyPolicyStore _policyStore;
    private readonly ILogger<SuccessModel> _logger;

    public SuccessModel(
        IJourneyStateStore stateStore,
        IJourneyPolicyStore policyStore,
        ILogger<SuccessModel> logger)
    {
        _stateStore = stateStore;
        _policyStore = policyStore;
        _logger = logger;
    }

    [BindProperty(SupportsGet = true)]
    public string? JourneyId { get; set; }

    public string SuccessMessage { get; private set; } = "Thank you! Your submission has been received.";
    public string? Title { get; private set; }
    public JourneyUiConfiguration? UiConfig { get; private set; }

    public async Task<IActionResult> OnGetAsync()
    {
        if (string.IsNullOrEmpty(JourneyId))
        {
            // Check TempData for journey ID
            JourneyId = TempData["CompletedJourneyId"]?.ToString();
        }

        if (string.IsNullOrEmpty(JourneyId))
        {
            return RedirectToPage("/Error", new { message = "Invalid request" });
        }

        var state = await _stateStore.GetAsync(JourneyId);
        if (state == null)
        {
            _logger.LogWarning("Journey {JourneyId} not found for success page", JourneyId);
            // Still show success - the journey may have been cleaned up
            return Page();
        }

        // Load the policy for UI configuration and success message
        var policy = await _policyStore.GetByIdAsync(state.PolicyId);
        if (policy != null)
        {
            if (!string.IsNullOrEmpty(policy.SuccessMessage))
            {
                SuccessMessage = policy.SuccessMessage;
            }
            Title = policy.Ui?.Title ?? policy.Name;
            UiConfig = policy.Ui;
        }

        return Page();
    }
}
