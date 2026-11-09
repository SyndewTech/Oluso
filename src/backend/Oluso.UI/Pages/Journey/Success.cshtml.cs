using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using Oluso.Core.Domain.Interfaces;
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
    private readonly IConfiguration _configuration;
    private readonly ITenantSettingsProvider? _settingsProvider;
    private readonly ITenantContext? _tenantContext;

    public SuccessModel(
        IJourneyStateStore stateStore,
        IJourneyPolicyStore policyStore,
        ILogger<SuccessModel> logger,
        IConfiguration configuration,
        ITenantSettingsProvider? settingsProvider = null,
        ITenantContext? tenantContext = null)
    {
        _stateStore = stateStore;
        _policyStore = policyStore;
        _logger = logger;
        _configuration = configuration;
        _settingsProvider = settingsProvider;
        _tenantContext = tenantContext;
    }

    /// <summary>The same branding the journey showed: its UI settings, then the tenant's.</summary>
    public JourneyBrandView Brand { get; private set; } = JourneyBrandView.Default;

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
            Brand = await JourneyBrandView.ResolveAsync(null, _settingsProvider, _tenantContext, _configuration, _logger);
            return Page();
        }

        // Load the policy for UI configuration and success message
        var policy = await _policyStore.GetByIdAsync(state.PolicyId);
        if (policy != null)
        {
            if (!string.IsNullOrEmpty(policy.SuccessMessage))
            {
                SuccessMessage = ResolveLocalizedValue(policy.SuccessMessage) ?? policy.SuccessMessage;
            }
            Title = policy.Ui?.Title ?? policy.Name;
            UiConfig = policy.Ui;
        }

        Brand = await JourneyBrandView.ResolveAsync(UiConfig, _settingsProvider, _tenantContext, _configuration, _logger);

        return Page();
    }

    private static string? ResolveLocalizedValue(string raw)
    {
        if (string.IsNullOrEmpty(raw) || !raw.TrimStart().StartsWith('{'))
            return null;
        try
        {
            var ls = System.Text.Json.JsonSerializer.Deserialize<Oluso.Core.Locale.LocalizedString>(raw);
            return ls?.Resolve(System.Globalization.CultureInfo.CurrentUICulture);
        }
        catch { return null; }
    }
}
