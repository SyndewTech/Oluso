using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.Extensions.Logging;
using Oluso.Core.Domain.Entities;
using Oluso.Core.Domain.Interfaces;
using Oluso.Core.Protocols;
using Oluso.Core.UserJourneys;
using Microsoft.AspNetCore.RateLimiting;
using Oluso.Core.Security;

namespace Oluso.UI.Pages.Embed;

/// <summary>
/// Embeddable auth page served inside an iframe.
/// Uses the journey orchestrator for authentication, then postMessages
/// an authorization code back to the parent window. The request is validated, and the code issued, by
/// <see cref="IAuthorizationCodeIssuer"/> like every sign-in that returns a code to a page: the parent
/// origin must be registered for the client, the scopes allowed for it, and PKCE S256 is required.
/// </summary>
[EnableRateLimiting(SignInRateLimitPolicies.SignIn)]
public class AuthModel : PageModel
{
    private readonly IJourneyOrchestrator _orchestrator;
    private readonly IJourneyStateStore _stateStore;
    private readonly IJourneyPolicyStore _policyStore;
    private readonly IAuthorizationCodeIssuer _codes;
    private readonly ITenantSettingsProvider _settingsProvider;
    private readonly ILogger<AuthModel> _logger;

    public AuthModel(
        IJourneyOrchestrator orchestrator,
        IJourneyStateStore stateStore,
        IJourneyPolicyStore policyStore,
        IAuthorizationCodeIssuer codes,
        ITenantSettingsProvider settingsProvider,
        ILogger<AuthModel> logger)
    {
        _orchestrator = orchestrator;
        _stateStore = stateStore;
        _policyStore = policyStore;
        _codes = codes;
        _settingsProvider = settingsProvider;
        _logger = logger;
    }

    // Query parameters
    /// <summary>The embedded journey, by slug (<c>?journey=</c>).</summary>
    [BindProperty(SupportsGet = true, Name = "journey")]
    public string JourneyPolicyId { get; set; } = "signin";

    [BindProperty(SupportsGet = true, Name = "client_id")]
    public string ClientId { get; set; } = null!;

    /// <summary>The parent page's origin; null until validated, so the page never posts to (or echoes) an unchecked one.</summary>
    [BindProperty(SupportsGet = true, Name = "origin")]
    public string? ParentOrigin { get; set; }

    [BindProperty(SupportsGet = true, Name = "code_challenge")]
    public string? CodeChallenge { get; set; }

    [BindProperty(SupportsGet = true, Name = "code_challenge_method")]
    public string? CodeChallengeMethod { get; set; }

    [BindProperty(SupportsGet = true, Name = "scope")]
    public string Scope { get; set; } = "openid profile email";

    // View state
    public string? JourneyId { get; private set; }
    public string? CurrentStepView { get; private set; }
    public object? StepViewModel { get; private set; }
    public string? ErrorMessage { get; private set; }
    public bool IsCompleted { get; private set; }
    public string? AuthorizationCode { get; private set; }
    public bool HideEmbedBranding { get; private set; }
    public JourneyUiConfiguration? UiConfig { get; private set; }

    public async Task<IActionResult> OnGetAsync()
    {
        if (!await ValidateRequestAsync())
        {
            return Page();
        }

        SetSecurityHeaders();

        // Check if resuming an existing journey
        var existingJourneyId = TempData["EmbedJourneyId"] as string;
        if (!string.IsNullOrEmpty(existingJourneyId))
        {
            JourneyId = existingJourneyId;
            var state = await _stateStore.GetAsync(existingJourneyId);
            if (state != null)
            {
                return await ContinueExistingJourneyAsync(state);
            }
        }

        // Start a new journey
        var policy = await _policyStore.GetBySlugAsync(JourneyPolicyId);
        if (policy == null)
        {
            ErrorMessage = "Journey not found";
            return Page();
        }
        if (!policy.Enabled)
        {
            ErrorMessage = "This journey is currently unavailable";
            return Page();
        }
        // A module's journey runs only from its module's launch link, which the embed doesn't take.
        if (policy.IsModuleOwned)
        {
            _logger.LogWarning("Journey {PolicyId} of module {Module} requested through the embed", policy.Id, policy.OwnerModule);
            ErrorMessage = "This form opens from the link you were sent";
            return Page();
        }

        UiConfig = policy.Ui;
        await LoadBrandingAsync();

        var journeyState = await _orchestrator.StartJourneyAsync(policy, new JourneyStartContext
        {
            HttpContext = HttpContext,
            ProtocolName = "embed",
            Properties = new Dictionary<string, object>
            {
                ["embed_origin"] = ParentOrigin,
                ["embed_client_id"] = ClientId,
            }
        });

        JourneyId = journeyState.Id;
        TempData["EmbedJourneyId"] = JourneyId;
        TempData["EmbedClientId"] = ClientId;
        TempData["EmbedOrigin"] = ParentOrigin;
        TempData["EmbedCodeChallenge"] = CodeChallenge;
        TempData["EmbedCodeChallengeMethod"] = CodeChallengeMethod ?? "S256";
        TempData["EmbedScope"] = Scope;

        // Get the first step
        var result = await _orchestrator.ContinueJourneyAsync(JourneyId, new JourneyStepInput
        {
            StepId = journeyState.CurrentStepId,
            Action = "init"
        });

        return await HandleJourneyResultAsync(result);
    }

    public async Task<IActionResult> OnPostAsync()
    {
        JourneyId = TempData["EmbedJourneyId"] as string;
        ClientId = (TempData["EmbedClientId"] as string) ?? ClientId;
        ParentOrigin = (TempData["EmbedOrigin"] as string) ?? ParentOrigin;
        CodeChallenge = TempData["EmbedCodeChallenge"] as string;
        CodeChallengeMethod = (TempData["EmbedCodeChallengeMethod"] as string) ?? "S256";
        Scope = (TempData["EmbedScope"] as string) ?? Scope;

        if (string.IsNullOrEmpty(JourneyId))
        {
            ErrorMessage = "Session expired. Please try again.";
            return Page();
        }

        SetSecurityHeaders();
        await LoadBrandingAsync();

        var state = await _stateStore.GetAsync(JourneyId);
        if (state == null)
        {
            ErrorMessage = "Session expired. Please try again.";
            return Page();
        }

        // Collect form data
        var userInput = Request.Form
            .Where(f => !f.Key.StartsWith("__RequestVerification") && !f.Key.StartsWith("__VIEWSTATE"))
            .ToDictionary(f => f.Key, f => (object)f.Value.ToString());

        // Continue the journey
        var result = await _orchestrator.ContinueJourneyAsync(JourneyId, new JourneyStepInput
        {
            StepId = state.CurrentStepId,
            Values = userInput
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
                // Persist TempData for next request
                TempData["EmbedJourneyId"] = JourneyId;
                TempData["EmbedClientId"] = ClientId;
                TempData["EmbedOrigin"] = ParentOrigin;
                TempData["EmbedCodeChallenge"] = CodeChallenge;
                TempData["EmbedCodeChallengeMethod"] = CodeChallengeMethod;
                TempData["EmbedScope"] = Scope;
                return Page();

            case JourneyStatus.Completed:
                return await IssueCodeAndCompleteAsync(result);

            case JourneyStatus.Failed:
                ErrorMessage = result.ErrorDescription ?? "Authentication failed";
                return Page();

            case JourneyStatus.Cancelled:
                ErrorMessage = "Authentication was cancelled";
                return Page();

            case JourneyStatus.Expired:
                ErrorMessage = "Session expired. Please try again.";
                return Page();

            default:
                ErrorMessage = "Unknown error";
                return Page();
        }
    }

    private DirectAuthorizationRequest AuthorizationRequest() => new()
    {
        ClientId = ClientId,
        Scope = Scope,
        CodeChallenge = CodeChallenge,
        CodeChallengeMethod = CodeChallengeMethod,
        Origin = ParentOrigin,
        RedirectUri = DirectAuthorizationRequest.PostMessage
    };

    private async Task<IActionResult> IssueCodeAndCompleteAsync(JourneyResult result)
    {
        var state = string.IsNullOrEmpty(JourneyId) ? null : await _stateStore.GetAsync(JourneyId);
        if (string.IsNullOrEmpty(result.Completion?.UserId) || state == null)
        {
            ErrorMessage = "Authentication failed: no user";
            return Page();
        }

        var issued = await _codes.IssueForJourneyAsync(AuthorizationRequest(), state, result.Completion, HttpContext.RequestAborted);
        if (!issued.Succeeded)
        {
            _logger.LogWarning("Embed journey {JourneyId} completed but no code was issued for client {ClientId}: {Error} {Description}",
                JourneyId, ClientId, issued.Error, issued.ErrorDescription);
            ErrorMessage = issued.ErrorDescription ?? "Authentication failed";
            return Page();
        }

        IsCompleted = true;
        AuthorizationCode = issued.Code;
        return Page();
    }

    private async Task<bool> ValidateRequestAsync()
    {
        if (string.IsNullOrEmpty(ClientId))
        {
            ParentOrigin = null;
            ErrorMessage = "Missing client_id parameter";
            return false;
        }

        // The client, scopes, PKCE and parent origin, by the same rules as every code returned to a page.
        var validation = await _codes.ValidateAsync(AuthorizationRequest(), HttpContext.RequestAborted);
        if (!validation.IsValid)
        {
            _logger.LogWarning("Embed request for client {ClientId} from {Origin} rejected: {Error} {Description}",
                ClientId, ParentOrigin, validation.Error, validation.ErrorDescription);
            ParentOrigin = null;
            ErrorMessage = validation.OriginRejected ? "Origin not allowed" : validation.ErrorDescription ?? "Invalid request";
            return false;
        }
        ParentOrigin = validation.Origin;

        // The journey may narrow who can run it further with its own origin list.
        var policy = await _policyStore.GetBySlugAsync(JourneyPolicyId);
        if (policy == null)
        {
            ErrorMessage = "Journey not found";
            return false;
        }

        if (!string.IsNullOrEmpty(policy.AllowedOrigins))
        {
            var allowed = policy.AllowedOrigins
                .Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
            if (!allowed.Contains(ParentOrigin, StringComparer.OrdinalIgnoreCase) && !allowed.Contains("*"))
            {
                _logger.LogWarning("Origin {Origin} not allowed for policy {PolicyId}", ParentOrigin, JourneyPolicyId);
                ErrorMessage = "Origin not allowed";
                return false;
            }
        }

        return true;
    }

    private async Task<IActionResult> ContinueExistingJourneyAsync(JourneyState state)
    {
        await LoadBrandingAsync();
        var policy = await _policyStore.GetByIdAsync(state.PolicyId);
        UiConfig = policy?.Ui;

        var result = await _orchestrator.ContinueJourneyAsync(JourneyId!, new JourneyStepInput
        {
            StepId = state.CurrentStepId,
            Action = "init"
        });

        return await HandleJourneyResultAsync(result);
    }

    private async Task LoadBrandingAsync()
    {
        try
        {
            var branding = await _settingsProvider.GetBrandingSettingsAsync();
            HideEmbedBranding = branding?.HideEmbedBranding ?? false;
        }
        catch
        {
            HideEmbedBranding = false;
        }
    }

    private void SetSecurityHeaders()
    {
        // Only the validated parent may frame it (browsers ignore X-Frame-Options' ALLOW-FROM, so the CSP alone says so).
        Response.Headers["Content-Security-Policy"] = global::Oluso.Middleware.FramePolicy.Normalize(ParentOrigin) is { } parent
            ? $"frame-ancestors {parent}"
            : "frame-ancestors 'none'";
    }

}
