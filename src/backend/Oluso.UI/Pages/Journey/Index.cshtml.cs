using System.Globalization;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Oluso.Core.Domain.Entities;
using Oluso.Core.Domain.Interfaces;
using Oluso.Core.Services;
using Oluso.Core.Authentication;
using Oluso.Core.UserJourneys;
using Microsoft.AspNetCore.RateLimiting;
using Oluso.Core.Security;

namespace Oluso.UI.Pages.Journey;

/// <summary>
/// Main Journey page model - orchestrates the authentication flow.
/// This is the default implementation that ships with Oluso.UI.
/// </summary>
[EnableRateLimiting(SignInRateLimitPolicies.SignIn)]
public class IndexModel : PageModel
{
    private readonly IJourneyOrchestrator _orchestrator;
    private readonly IJourneyStateStore _stateStore;
    private readonly IJourneyPolicyStore _policyStore;
    private readonly IServiceProvider _serviceProvider;
    private readonly ILogger<IndexModel> _logger;
    private readonly IConfiguration _configuration;
    private readonly ITenantContext? _tenantContext;
    private readonly ITenantSettingsProvider? _settingsProvider;

    public IndexModel(
        IJourneyOrchestrator orchestrator,
        IJourneyStateStore stateStore,
        IJourneyPolicyStore policyStore,
        IServiceProvider serviceProvider,
        ILogger<IndexModel> logger,
        IConfiguration configuration,
        ITenantContext? tenantContext = null,
        ITenantSettingsProvider? settingsProvider = null)
    {
        _orchestrator = orchestrator;
        _stateStore = stateStore;
        _policyStore = policyStore;
        _serviceProvider = serviceProvider;
        _logger = logger;
        _configuration = configuration;
        _tenantContext = tenantContext;
        _settingsProvider = settingsProvider;
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
    /// Per-step layout override (narrow / medium / wide / full). When a step
    /// handler writes the well-known <see cref="StepLayoutKey"/> entry into
    /// journey state, the shell uses it for that step in preference to the
    /// policy-level <see cref="JourneyUiConfiguration.Layout"/>. Useful for
    /// flows where most steps fit a narrow column but one step (e.g. a plan
    /// selector or pricing table) needs more horizontal room.
    /// </summary>
    public string? StepLayout { get; private set; }

    /// <summary>
    /// Journey-state key a step handler writes to advertise its preferred
    /// shell layout for the current render. Values: "narrow", "medium",
    /// "wide", "full" (matching <see cref="JourneyUiConfiguration.Layout"/>).
    /// </summary>
    public const string StepLayoutKey = "step_layout";

    /// <summary>
    /// ViewData key holding the "Sign in with your organisation" URL for the login
    /// step partials. Set only on the platform host, for journeys resuming a protocol flow.
    /// </summary>
    public const string OrgSignInUrlKey = "OrgSignInUrl";

    /// <summary>
    /// Progress information for the journey
    /// </summary>
    public JourneyProgressInfo? Progress { get; private set; }

    /// <summary>
    /// Resolved branding for the current request. Cascades journey-policy
    /// UI (most specific) → tenant Branding → platform IConfiguration so the
    /// view never has to combine sources itself.
    /// </summary>
    public JourneyBrandView Brand { get; private set; } = JourneyBrandView.Default;

    /// <summary>
    /// Resolved locale tag for the current journey (e.g. "fr", "en-US").
    /// Set from <c>ui_locales</c> → <c>Accept-Language</c> → "en".
    /// </summary>
    public string ResolvedLocale { get; private set; } = "en";

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
            return await RedirectToErrorAsync(state.PolicyId, null, "Your session has expired. Please try again.", state.CallbackUrl);
        }

        State = state;
        ApplyLocaleFromJourneyData(state);
        _logger.LogInformation("Journey {JourneyId} locale resolved to '{Locale}', data keys: [{Keys}]",
            JourneyId, ResolvedLocale,
            state.Data != null ? string.Join(", ", state.Data.Keys) : "null");
        HttpContext.Items[Localization.TenantConfigLocaleOverrideProvider.JourneyPolicyIdKey] = state.PolicyId;

        State = await CaptureUtmAttributionAsync(state);

        // Load the policy to get UI configuration
        await LoadUiConfigurationAsync(State.PolicyId);
        await LoadBrandAsync();

        // Get the current step result from journey data
        var result = await _orchestrator.GetStateAsync(JourneyId);
        if (result != null)
        {
            State = result;

            // Collect query parameters to pass to step handlers
            // This allows steps to respond to query parameters like ?use_recovery=true
            var queryParams = Request.Query
                .Where(q => !q.Key.Equals("journeyId", StringComparison.OrdinalIgnoreCase) &&
                           !q.Key.Equals("handler", StringComparison.OrdinalIgnoreCase))
                .ToDictionary(q => q.Key, q => (object)q.Value.ToString());

            // The view and model should come from continuing the journey
            var continueResult = await _orchestrator.ContinueJourneyAsync(JourneyId, new JourneyStepInput
            {
                StepId = result.CurrentStepId,
                Action = "init",
                Values = queryParams.Count > 0 ? queryParams : null
            });

            return await HandleJourneyResultAsync(continueResult);
        }

        return Page();
    }

    /// <summary>
    /// Picks up UTM / campaign attribution from the query string (rare on this
    /// page — Start usually owns the initial click) or the sticky
    /// <c>oluso_utm</c> cookie, then merges into the journey state bag so
    /// Data Collection steps with <c>Source = "session"</c> can read them.
    /// Initial-click capture happens on <c>Pages/Journey/Start.cshtml.cs</c>.
    /// </summary>
    private async Task<JourneyState> CaptureUtmAttributionAsync(JourneyState state)
    {
        var captured = JourneyUtmCapture.ReadFromRequest(Request);
        if (captured.Count == 0) return state;

        var data = state.Data is { } existing
            ? new Dictionary<string, object>(existing, StringComparer.OrdinalIgnoreCase)
            : new Dictionary<string, object>(StringComparer.OrdinalIgnoreCase);

        var changed = false;
        foreach (var (k, v) in captured)
        {
            if (data.TryGetValue(k, out var es) && es is string s && string.Equals(s, v, StringComparison.Ordinal)) continue;
            data[k] = v;
            changed = true;
        }

        var updated = state;
        if (changed)
        {
            updated = state with { Data = data };
            await _stateStore.SaveAsync(updated);
        }

        JourneyUtmCapture.WriteCookie(Response, Request, captured);
        return updated;
    }

    /// <summary>
    /// Consumes a magic-link verification token. Hit by email recipients
    /// (possibly on a different device than where the journey started).
    /// Forwards the token to the orchestrator as <c>magic_token</c> input on
    /// the current step — the active <c>email_verification</c> step verifies
    /// it (constant-time compare) and emits success. The journey advances on
    /// the server, which the original device picks up via its status poll.
    /// </summary>
    public async Task<IActionResult> OnGetVerifyEmailAsync(string? t)
    {
        if (string.IsNullOrEmpty(JourneyId) || string.IsNullOrEmpty(t))
        {
            return RedirectToPage("/Error", new { message = "Invalid verification link" });
        }

        var state = await _stateStore.GetAsync(JourneyId);
        if (state == null)
        {
            return RedirectToPage("/Error", new { message = "This verification link has expired or the journey is no longer active." });
        }

        var continueResult = await _orchestrator.ContinueJourneyAsync(JourneyId, new JourneyStepInput
        {
            StepId = state.CurrentStepId,
            Action = "init",
            Values = new Dictionary<string, object> { ["magic_token"] = t }
        });

        if (continueResult.Status == JourneyStatus.Failed)
        {
            return RedirectToPage("/Error", new
            {
                message = continueResult.ErrorDescription ?? "This verification link is invalid or has already been used.",
                returnUrl = state.CallbackUrl
            });
        }

        // Verified — render a "you can close this tab" page on the click
        // device. The original device's poll detects the step advance and
        // continues the journey there, so we deliberately don't redirect
        // this device into the next step.
        return RedirectToPage("/Journey/VerificationComplete");
    }

    /// <summary>
    /// JSON status endpoint polled by the verify view on the original device.
    /// Returns the current step ID so the client can detect when an
    /// out-of-band action (e.g. a magic-link click on another device) has
    /// advanced the journey, and reload to render the next step; and the
    /// <see cref="JourneyStatusRevision"/>, which changes when something outside
    /// the browser (a provider's webhook) changed what the current step shows.
    /// </summary>
    public async Task<IActionResult> OnGetStatusAsync()
    {
        if (string.IsNullOrEmpty(JourneyId))
        {
            return new JsonResult(new { error = "invalid_journey" }) { StatusCode = 400 };
        }

        var state = await _stateStore.GetAsync(JourneyId);
        if (state == null)
        {
            return new JsonResult(new { error = "not_found" }) { StatusCode = 404 };
        }

        return new JsonResult(new
        {
            currentStepId = state.CurrentStepId,
            status = state.Status.ToString(),
            revision = JourneyStatusRevision.Read(state.Data),
            expired = state.ExpiresAt.HasValue && state.ExpiresAt < DateTime.UtcNow
        });
    }

    private async Task LoadUiConfigurationAsync(string policyId)
    {
        try
        {
            var policy = await _policyStore.GetByIdAsync(policyId);
            UiConfig = policy?.Ui;
            Title = UiConfig?.Title ?? policy?.Name;
            global::Oluso.Middleware.FramePolicy.Allow(HttpContext, policy?.AllowedOrigins);

            // Calculate progress based on policy steps
            if (policy?.Steps != null && State != null)
            {
                var steps = policy.Steps.OrderBy(s => s.Order).ToList();
                var currentStepIndex = steps.FindIndex(s => s.Id == State.CurrentStepId);

                var progressStyle = UiConfig?.ProgressStyle?.ToLowerInvariant() switch
                {
                    "vertical" => "vertical",
                    "none" => "none",
                    _ => "horizontal"
                };

                Progress = new JourneyProgressInfo
                {
                    TotalSteps = steps.Count,
                    CurrentStepIndex = currentStepIndex >= 0 ? currentStepIndex : 0,
                    CompletedSteps = currentStepIndex >= 0 ? currentStepIndex : 0,
                    CurrentStepName = steps.ElementAtOrDefault(currentStepIndex)?.DisplayName ?? "Authentication",
                    StepNames = steps.Select((s, idx) => s.DisplayName ?? $"Step {idx + 1}").ToList(),
                    Style = progressStyle
                };
            }
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to load UI configuration for policy {PolicyId}", policyId);
        }
    }

    private async Task LoadBrandAsync()
    {
        Brand = await JourneyBrandView.ResolveAsync(UiConfig, _settingsProvider, _tenantContext, _configuration, _logger);
    }

    /// <summary>
    /// The error page, carrying the journey's slug so it shows the journey's branding
    /// (and its way back) rather than the platform's.
    /// </summary>
    private async Task<IActionResult> RedirectToErrorAsync(string? policyId, string? error, string? message, string? returnUrl)
    {
        string? slug = null;
        if (!string.IsNullOrEmpty(policyId))
        {
            try
            {
                slug = (await _policyStore.GetByIdAsync(policyId))?.Slug;
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Failed to load policy {PolicyId} for the error page", policyId);
            }
        }

        return RedirectToPage("/Error", new { error, message, returnUrl, journey = slug });
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

        // Files go to the step alongside the values; the step validates and stores them.
        var files = Request.HasFormContentType && Request.Form.Files.Count > 0
            ? Request.Form.Files
                .Select(f => new JourneyInputFile(f.Name, f.FileName, f.ContentType, f.Length, f.OpenReadStream))
                .ToList()
            : null;

        // Continue the journey with user input
        var result = await _orchestrator.ContinueJourneyAsync(JourneyId, new JourneyStepInput
        {
            StepId = state.CurrentStepId,
            Values = userInput,
            Files = files
        });

        // Hydrate the page model for the post-step render: reload state
        // (CurrentStepId may have advanced inside ContinueJourneyAsync) and
        // resolve UI config + branding so the progress indicator and
        // chrome render the same as on a GET.
        State = await _stateStore.GetAsync(JourneyId) ?? state;
        ApplyLocaleFromJourneyData(State);
        HttpContext.Items[Localization.TenantConfigLocaleOverrideProvider.JourneyPolicyIdKey] = State.PolicyId;
        await LoadUiConfigurationAsync(State.PolicyId);
        await LoadBrandAsync();

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

        State = state;
        ApplyLocaleFromJourneyData(state);
        HttpContext.Items[Localization.TenantConfigLocaleOverrideProvider.JourneyPolicyIdKey] = state.PolicyId;

        // Continue the journey (the external IdP step handler will process the auth result).
        // Whatever the external party put on the return URL goes to the step as input; the
        // step decides what it trusts.
        var returned = Request.Query
            .Where(q => !q.Key.Equals("journeyId", StringComparison.OrdinalIgnoreCase) &&
                       !q.Key.Equals("handler", StringComparison.OrdinalIgnoreCase))
            .ToDictionary(q => q.Key, q => (object)q.Value.ToString());
        var result = await _orchestrator.ContinueJourneyAsync(JourneyId, new JourneyStepInput
        {
            StepId = state.CurrentStepId,
            Action = "callback",
            Values = returned.Count > 0 ? returned : null
        });

        // Refresh state and hydrate page model AFTER the orchestrator runs,
        // so progress/branding reflect the step we actually land on.
        State = await _stateStore.GetAsync(JourneyId) ?? state;
        await LoadUiConfigurationAsync(State.PolicyId);
        await LoadBrandAsync();

        return await HandleJourneyResultAsync(result);
    }

    private async Task<string?> ResolveOrgSignInUrlAsync()
    {
        var organizationSignIn = _serviceProvider.GetService<Oluso.Authentication.OrganizationSignIn.IOrganizationSignInService>();
        if (organizationSignIn == null || State == null) return null;
        return await organizationSignIn.GetJourneyEntryUrlAsync(HttpContext, State.Id, HttpContext.RequestAborted);
    }

    /// <summary>
    /// Reads the step's layout override out of the latest journey-state snapshot.
    /// Result.CurrentStep doesn't carry data state, so we fetch the state again
    /// — cheap, in-memory or one DB read depending on the store.
    /// </summary>
    private async Task<string?> ResolveStepLayoutAsync()
    {
        var state = await _stateStore.GetAsync(JourneyId);
        if (state == null) return null;

        // A runtime override written by a step handler wins (most specific).
        if (state.Data?.TryGetValue(StepLayoutKey, out var raw) == true
            && raw?.ToString() is { Length: > 0 } dynamicLayout)
        {
            return dynamicLayout;
        }

        // Otherwise the current step's configured layout from the policy.
        var policy = await _policyStore.GetByIdAsync(state.PolicyId);
        return policy?.Steps.FirstOrDefault(s => s.Id == state.CurrentStepId)?.Layout;
    }

    private async Task<IActionResult> HandleJourneyResultAsync(JourneyResult result)
    {
        switch (result.Status)
        {
            case JourneyStatus.InProgress:
                if (result.CurrentStep != null)
                {
                    if (result.CurrentStep.ViewName == "_Redirect")
                    {
                        if (HttpContext.Response.HasStarted || HttpContext.Response.StatusCode is >= 300 and < 400)
                        {
                            return new EmptyResult();
                        }

                        if (result.CurrentStep.ViewData?.TryGetValue("redirectUrl", out var url) == true
                            && url is string redirectUrl
                            && !string.IsNullOrEmpty(redirectUrl))
                        {
                            return Redirect(redirectUrl);
                        }
                    }

                    CurrentStepView = result.CurrentStep.ViewName;
                    StepViewModel = result.CurrentStep.ViewModel;
                }
                StepLayout = await ResolveStepLayoutAsync();
                ViewData[OrgSignInUrlKey] = await ResolveOrgSignInUrlAsync();
                return Page();

            case JourneyStatus.Completed:
                // Journey completed successfully - issue session if login happened
                return await RedirectToJourneyCompletionAsync(result);

            case JourneyStatus.Failed:
                _logger.LogWarning("Journey {JourneyId} failed: {ErrorCode} - {ErrorMessage}",
                    JourneyId, result.Error, result.ErrorDescription);
                return await RedirectToErrorAsync(State?.PolicyId, result.Error, result.ErrorDescription, State?.CallbackUrl);

            case JourneyStatus.Cancelled:
                return await RedirectToErrorAsync(State?.PolicyId, null, "Authentication was cancelled", State?.CallbackUrl);

            case JourneyStatus.Expired:
                return await RedirectToErrorAsync(State?.PolicyId, null, "Your session has expired. Please try again.", State?.CallbackUrl);

            default:
                return await RedirectToErrorAsync(State?.PolicyId, null, "Unknown error", State?.CallbackUrl);
        }
    }

    private async Task<IActionResult> RedirectToJourneyCompletionAsync(JourneyResult result)
    {
        // Store completion info for the callback handler
        TempData["CompletedJourneyId"] = result.JourneyId;

        // Check if authentication actually happened during this journey
        // by looking for the authenticated_at flag set by login steps
        var journeyState = await _stateStore.GetAsync(result.JourneyId);
        var shouldIssueSession = ShouldIssueSession(journeyState);

        if (shouldIssueSession && !string.IsNullOrEmpty(result.Completion?.UserId))
        {
            await IssueSessionAsync(result.Completion.UserId, journeyState!);
        }

        // If there's a redirect URI in the completion, use it. A protocol callback for the
        // request this journey ran for also gets the journey, so its claims reach the tokens.
        if (!string.IsNullOrEmpty(result.Completion?.RedirectUri))
        {
            var redirect = result.Completion.RedirectUri;
            if (!string.IsNullOrEmpty(journeyState?.CorrelationId)
                && redirect.Contains($"correlation_id={Uri.EscapeDataString(journeyState.CorrelationId)}", StringComparison.Ordinal)
                && !redirect.Contains("journey_id=", StringComparison.Ordinal))
            {
                redirect += $"&journey_id={Uri.EscapeDataString(result.JourneyId)}";
            }
            return Redirect(redirect);
        }

        // Data collection journey types show the success page; auth
        // journeys redirect to the OIDC authorize callback.
        var policy = await _policyStore.GetByIdAsync(State?.PolicyId ?? "");
        var isDataCollectionJourney = policy?.Type is JourneyType.Waitlist
            or JourneyType.ContactForm or JourneyType.Survey
            or JourneyType.Feedback or JourneyType.DataCollection;
        // Data collection journeys, and journeys opened directly rather than for a sign-in
        // request (no protocol request waits on them), finish on the success page.
        if (isDataCollectionJourney || string.IsNullOrEmpty(journeyState?.CorrelationId))
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
    private static bool ShouldIssueSession(JourneyState? state)
    {
        if (state?.Data == null)
        {
            return false;
        }

        // A login step authenticated the user in this journey...
        if (state.Data.ContainsKey("authenticated_at"))
        {
            return true;
        }

        // ...or a journey run on the existing session added MFA, which the cookie must record.
        return state.Data.ContainsKey(JourneyStartContext.SessionUserIdDataKey)
            && GetAuthenticationMethods(state.Data).Any(IsSecondFactor);
    }

    /// <summary>
    /// Issues a cookie session for the authenticated user.
    /// </summary>
    private void ApplyLocaleFromJourneyData(JourneyState state)
    {
        string? locale = null;

        if (state.Data != null)
        {
            var uiLocales = GetStringFromData(state.Data, "ui_locales");
            if (!string.IsNullOrEmpty(uiLocales))
                locale = uiLocales.Split(' ', ',').FirstOrDefault(s => !string.IsNullOrWhiteSpace(s));

            if (string.IsNullOrEmpty(locale))
                locale = GetStringFromData(state.Data, "locale");
        }

        locale = string.IsNullOrEmpty(locale) ? "en" : locale;
        ResolvedLocale = locale;

        try
        {
            var culture = CultureInfo.GetCultureInfo(locale);
            CultureInfo.CurrentUICulture = culture;
        }
        catch (CultureNotFoundException)
        {
            // Unknown culture tag — keep default
        }
    }

    private static bool IsTrue(IDictionary<string, object>? data, string key)
    {
        if (data == null || !data.TryGetValue(key, out var value)) return false;
        return value switch
        {
            bool b => b,
            string s => string.Equals(s, "true", StringComparison.OrdinalIgnoreCase),
            System.Text.Json.JsonElement el => el.ValueKind == System.Text.Json.JsonValueKind.True
                || (el.ValueKind == System.Text.Json.JsonValueKind.String && string.Equals(el.GetString(), "true", StringComparison.OrdinalIgnoreCase)),
            _ => false
        };
    }

    private static string? GetStringFromData(IDictionary<string, object> data, string key)
    {
        if (!data.TryGetValue(key, out var value)) return null;
        if (value is string s) return s;
        if (value is System.Text.Json.JsonElement el && el.ValueKind == System.Text.Json.JsonValueKind.String)
            return el.GetString();
        return value?.ToString();
    }

    private static bool IsSecondFactor(string method) =>
        method is "mfa" or "otp" or "recovery_code" or "hwk" or "swk";

    // The same reading as the codes embedded and headless journeys complete with.
    private static IEnumerable<string> GetAuthenticationMethods(IDictionary<string, object>? data) =>
        JourneyAuthenticationMethods.Read(data);

    private async Task IssueSessionAsync(string userId, JourneyState state)
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
        // "Remember me" from the login step: persistent cookie, and the tokens
        // issued against it are marked so the app keeps them across restarts.
        var rememberMe = IsTrue(state.Data, "remember_me");
        var methods = GetAuthenticationMethods(state.Data).ToList();
        var authTime = DateTimeOffset.UtcNow;

        // A journey on the existing session (no new login) keeps that sign-in's time,
        // methods and persistence, and adds what this journey proved (MFA).
        if (state.Data?.ContainsKey("authenticated_at") != true)
        {
            var current = await HttpContext.AuthenticateAsync(IdentityConstants.ApplicationScheme);
            if (current.Succeeded && current.Principal != null)
            {
                rememberMe = current.Properties?.IsPersistent == true;
                methods.InsertRange(0, current.Principal.FindAll("amr").Select(c => c.Value));
                if (long.TryParse(current.Principal.FindFirst("auth_time")?.Value, out var unix))
                {
                    authTime = DateTimeOffset.FromUnixTimeSeconds(unix);
                }
            }
        }

        // The cookie records how and when this sign-in happened, so max_age and a later
        // policy run's MFA step can tell whether the session already passed MFA.
        await signInManager.SignInWithClaimsAsync(user, isPersistent: rememberMe,
            SessionAuthenticationClaims.Create(methods, authTime));

        _logger.LogInformation("Session issued for user {UserId} after journey completion", userId);

        // One sign-in event per issued session, whichever step (local, external,
        // SAML, LDAP, passkey) authenticated the user, and only after MFA and any
        // remaining steps have passed.
        var auditor = _serviceProvider.GetService<ISignInAuditor>();
        if (auditor != null)
        {
            await auditor.RecordSuccessAsync(new SignInAudit
            {
                SubjectId = userId,
                Username = user.UserName ?? user.Email ?? userId,
                TenantId = string.IsNullOrEmpty(user.TenantId) ? state.TenantId : user.TenantId,
                AuthenticationMethod = GetStringFromData(state.Data!, "auth_method") ?? "journey",
                ClientId = state.ClientId,
                SessionId = state.JourneyId,
                IpAddress = GetStringFromData(state.Data!, "ip_address")
            });
        }
    }
}

/// <summary>
/// Resolved brand surface for the journey page. Single source of truth so
/// the view doesn't have to fan out across journey-policy UI, tenant
/// branding, and platform configuration.
/// </summary>
public sealed class JourneyBrandView
{
    public string Name { get; init; } = "Oluso";
    public string? LogoUrl { get; init; }
    public string? PrimaryColor { get; init; }
    public string? BackgroundColor { get; init; }
    public string? CustomCss { get; init; }

    public static JourneyBrandView Default { get; } = new();

    /// <summary>
    /// The journey's own UI settings win, then the tenant's branding, then the platform's
    /// configured name.
    /// </summary>
    public static async Task<JourneyBrandView> ResolveAsync(
        JourneyUiConfiguration? ui,
        ITenantSettingsProvider? settingsProvider,
        ITenantContext? tenantContext,
        IConfiguration? configuration,
        ILogger logger)
    {
        TenantBrandingSettings? branding = null;
        if (settingsProvider != null)
        {
            try
            {
                branding = await settingsProvider.GetBrandingSettingsAsync();
            }
            catch (Exception ex)
            {
                logger.LogWarning(ex, "Failed to load tenant branding settings; falling back to platform config");
            }
        }

        var tenantName = tenantContext?.Tenant?.DisplayName ?? tenantContext?.Tenant?.Name;
        var configName = configuration?["Oluso:Branding:CompanyName"];

        return new JourneyBrandView
        {
            Name = !string.IsNullOrWhiteSpace(tenantName) ? tenantName!
                 : !string.IsNullOrWhiteSpace(configName) ? configName!
                 : "Oluso",
            LogoUrl = !string.IsNullOrEmpty(ui?.LogoUrl) ? ui!.LogoUrl : branding?.LogoUrl,
            PrimaryColor = !string.IsNullOrEmpty(ui?.PrimaryColor) ? ui!.PrimaryColor : branding?.PrimaryColor,
            BackgroundColor = !string.IsNullOrEmpty(ui?.BackgroundColor) ? ui!.BackgroundColor : branding?.BackgroundColor,
            CustomCss = !string.IsNullOrEmpty(ui?.CustomCss) ? ui!.CustomCss : branding?.CustomCss,
        };
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

    /// <summary>
    /// Display names of every step in order, for the vertical indicator.
    /// </summary>
    public IReadOnlyList<string> StepNames { get; init; } = Array.Empty<string>();

    /// <summary>
    /// "horizontal" (default), "vertical", or "none". Resolved from
    /// <see cref="JourneyUiConfiguration.ProgressStyle"/>.
    /// </summary>
    public string Style { get; init; } = "horizontal";

    public int PercentComplete => TotalSteps > 0 ? (int)((double)CurrentStepIndex / TotalSteps * 100) : 0;
    public bool ShowProgress => TotalSteps > 1 && !string.Equals(Style, "none", StringComparison.OrdinalIgnoreCase);
}
