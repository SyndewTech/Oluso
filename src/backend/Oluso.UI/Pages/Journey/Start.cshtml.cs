using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.Extensions.Logging;
using Oluso.Core.Domain.Interfaces;
using Oluso.Core.UserJourneys;

namespace Oluso.UI.Pages.Journey;

/// <summary>
/// Shared utility for capturing UTM / campaign-attribution query params off
/// the current request, persisting them into the journey-state data bag, and
/// dropping a 30-day stickiness cookie. Used by both <c>Start</c> (initial
/// click of a tagged URL) and <c>Index</c> (return visits where the cookie
/// is the source of truth).
/// </summary>
internal static class JourneyUtmCapture
{
    public static readonly string[] UtmKeys =
    {
        "utm_source", "utm_medium", "utm_campaign", "utm_term", "utm_content", "campaign_id",
    };

    public const string CookieName = "oluso_utm";

    public static Dictionary<string, string> ReadFromRequest(Microsoft.AspNetCore.Http.HttpRequest request)
    {
        var captured = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        foreach (var key in UtmKeys)
        {
            var qv = request.Query[key].ToString();
            if (!string.IsNullOrWhiteSpace(qv)) captured[key] = qv;
        }
        if (captured.Count == 0 && request.Cookies.TryGetValue(CookieName, out var cookieJson) && !string.IsNullOrEmpty(cookieJson))
        {
            try
            {
                var parsed = System.Text.Json.JsonSerializer.Deserialize<Dictionary<string, string>>(cookieJson);
                if (parsed != null)
                {
                    foreach (var (k, v) in parsed)
                    {
                        if (!string.IsNullOrWhiteSpace(v)) captured[k] = v;
                    }
                }
            }
            catch (System.Text.Json.JsonException) { }
        }
        return captured;
    }

    public static void WriteCookie(Microsoft.AspNetCore.Http.HttpResponse response, Microsoft.AspNetCore.Http.HttpRequest request, IDictionary<string, string> captured)
    {
        if (captured.Count == 0) return;
        response.Cookies.Append(CookieName, System.Text.Json.JsonSerializer.Serialize(captured), new Microsoft.AspNetCore.Http.CookieOptions
        {
            HttpOnly = false,
            Secure = request.IsHttps,
            SameSite = Microsoft.AspNetCore.Http.SameSiteMode.Lax,
            Expires = DateTimeOffset.UtcNow.AddDays(30),
            Path = "/",
        });
    }
}

/// <summary>
/// Starts a journey from a policy ID. This is the entry point for data collection
/// journeys (waitlists, surveys, contact forms) and can also be used to start
/// authentication journeys directly without going through OIDC.
/// </summary>
public class StartModel : PageModel
{
    private readonly IJourneyOrchestrator _orchestrator;
    private readonly IJourneyPolicyStore _policyStore;
    private readonly IJourneyStateStore _stateStore;
    private readonly IJourneySubmissionStore? _submissionStore;
    private readonly IJourneyLaunchTokenService? _launchTokens;
    private readonly ITenantContext? _tenantContext;
    private readonly ILogger<StartModel> _logger;

    public StartModel(
        IJourneyOrchestrator orchestrator,
        IJourneyPolicyStore policyStore,
        IJourneyStateStore stateStore,
        ILogger<StartModel> logger,
        IJourneySubmissionStore? submissionStore = null,
        IJourneyLaunchTokenService? launchTokens = null,
        ITenantContext? tenantContext = null)
    {
        _orchestrator = orchestrator;
        _policyStore = policyStore;
        _stateStore = stateStore;
        _submissionStore = submissionStore;
        _launchTokens = launchTokens;
        _tenantContext = tenantContext;
        _logger = logger;
    }

    /// <summary>The journey to start, by slug (<c>/journey/start/{slug}</c>).</summary>
    [BindProperty(SupportsGet = true, Name = "slug")]
    public string PolicyId { get; set; } = null!;

    public string? ErrorMessage { get; private set; }

    public async Task<IActionResult> OnGetAsync()
    {
        if (string.IsNullOrEmpty(PolicyId))
        {
            return RedirectToPage("/Error", new { message = "Policy ID is required" });
        }

        // The route names the policy by slug. The share URL the admin generates
        // points at the policy's tenant host (auto-subdomain or CustomDomain), so
        // the request's resolved tenant is the one the slug belongs to.
        var policy = await _policyStore.GetBySlugAsync(PolicyId);
        if (policy == null)
        {
            _logger.LogWarning("Policy {PolicyId} not found", PolicyId);
            return RedirectToPage("/Error", new { message = "Journey not found" });
        }

        if (!policy.Enabled)
        {
            _logger.LogWarning("Policy {PolicyId} is disabled", PolicyId);
            return RedirectToPage("/Error", new { message = "This journey is currently unavailable", journey = PolicyId });
        }

        // Check if max submissions reached (for data collection journeys)
        if (policy.MaxSubmissions > 0 && _submissionStore != null)
        {
            var count = await _submissionStore.CountByPolicyAsync(policy.Id);
            if (count >= policy.MaxSubmissions)
            {
                _logger.LogInformation("Max submissions ({Max}) reached for policy {PolicyId}",
                    policy.MaxSubmissions, PolicyId);
                return RedirectToPage("/Error", new { message = "This form is no longer accepting submissions", journey = PolicyId });
            }
        }

        JourneyLaunchTicket? launch = null;
        var launchToken = Request.Query[JourneyLaunchData.QueryParameter].FirstOrDefault();
        if (!string.IsNullOrEmpty(launchToken))
        {
            var (ticket, launchError) = await ReadLaunchAsync(launchToken, policy);
            if (ticket == null)
            {
                return RedirectToPage("/Error", new { message = launchError, journey = PolicyId });
            }
            launch = ticket;
        }

        // A module's journey opens only from a launch link its module (or, for a shared one, another module) made,
        // so its answers always have a module to go to; the address alone doesn't open it.
        if (policy.IsModuleOwned && (string.IsNullOrEmpty(launch?.Module) || !policy.UsableByModule(launch.Module)))
        {
            _logger.LogWarning("Journey {PolicyId} of module {Module} opened without a module launch link", PolicyId, policy.OwnerModule);
            return RedirectToPage("/Error", new { message = "This form opens from the link you were sent", journey = PolicyId });
        }

        try
        {
            // Start a new journey with context properties
            var properties = new Dictionary<string, object>
            {
                ["TenantId"] = launch?.TenantId ?? policy.TenantId ?? "",
                ["ClientId"] = "" // Data collection journeys don't require a client
            };

            var uiLocales = Request.Query["ui_locales"].FirstOrDefault();
            if (!string.IsNullOrEmpty(uiLocales))
            {
                properties["UiLocales"] = uiLocales;
            }

            // A signed-in user's journey acts on their account: login steps are already met,
            // and form/profile steps save to them.
            var signedInUserId = User.Identity?.IsAuthenticated == true
                ? User.FindFirst("sub")?.Value ?? User.FindFirst(System.Security.Claims.ClaimTypes.NameIdentifier)?.Value
                : null;

            var state = await _orchestrator.StartJourneyAsync(policy, new JourneyStartContext
            {
                HttpContext = HttpContext,
                CallbackUrl = policy.SuccessRedirectUrl,
                UserId = signedInUserId,
                Launch = launch,
                Properties = properties
            });

            _logger.LogInformation("Started journey {JourneyId} for policy {PolicyId} ({PolicyName})",
                state.Id, PolicyId, policy.Name);

            // Campaign attribution capture: tagged URLs land here first, so this
            // is where UTM params live on the inbound request. Merge them into
            // the freshly-created journey state's Data bag (so DataCollection
            // steps can read them as 'event.fields.utm_*' on submission) and
            // drop a 30-day cookie for return visits.
            var captured = JourneyUtmCapture.ReadFromRequest(Request);
            if (captured.Count > 0 || launch != null)
            {
                var data = state.Data is { } existing
                    ? new Dictionary<string, object>(existing, StringComparer.OrdinalIgnoreCase)
                    : new Dictionary<string, object>(StringComparer.OrdinalIgnoreCase);
                foreach (var (k, v) in captured) data[k] = v;
                if (!string.IsNullOrEmpty(launch?.ReturnUrl)) data["success_redirect_url"] = launch.ReturnUrl;
                var updated = state with { Data = data };
                await _stateStore.SaveAsync(updated);
                JourneyUtmCapture.WriteCookie(Response, Request, captured);
            }

            // Redirect to the journey page
            return RedirectToPage("Index", new { journeyId = state.Id });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to start journey for policy {PolicyId}", PolicyId);
            return RedirectToPage("/Error", new { message = "Failed to start journey", journey = PolicyId });
        }
    }

    /// <summary>
    /// A launch link is honoured only when intact, unexpired, minted for this journey and for
    /// the tenant whose host it is opened on, and when its return address is still allowed.
    /// </summary>
    private async Task<(JourneyLaunchTicket? Ticket, string Error)> ReadLaunchAsync(string token, JourneyPolicy policy)
    {
        const string invalid = "This link is not valid";
        if (_launchTokens == null)
        {
            _logger.LogWarning("Launch link for {PolicyId} received but no launch token service is registered", PolicyId);
            return (null, invalid);
        }

        var read = _launchTokens.Read(token);
        if (read.Status == JourneyLaunchReadStatus.Expired) return (null, "This link has expired");
        if (read.Ticket is not { } ticket) return (null, invalid);

        var requestTenant = _tenantContext?.TenantId;
        if (ticket.PolicyId != policy.Id
            || (policy.TenantId != null && policy.TenantId != ticket.TenantId)
            || string.IsNullOrEmpty(requestTenant)
            || requestTenant != ticket.TenantId)
        {
            _logger.LogWarning("Launch link for policy {TicketPolicy} in tenant {TicketTenant} opened as {PolicyId} on tenant {RequestTenant}",
                ticket.PolicyId, ticket.TenantId, PolicyId, requestTenant);
            return (null, invalid);
        }

        if (!string.IsNullOrEmpty(ticket.ReturnUrl)
            && !await _launchTokens.IsAllowedReturnUrlAsync(ticket.TenantId, ticket.ReturnUrl, HttpContext.RequestAborted))
        {
            _logger.LogWarning("Launch link for {PolicyId} carries a return URL no longer registered for tenant {TenantId}", PolicyId, ticket.TenantId);
            return (null, invalid);
        }

        return (ticket, "");
    }
}
