using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.Extensions.Logging;
using Oluso.Core.Domain.Entities;
using Oluso.Core.Domain.Interfaces;
using Oluso.Core.Protocols;
using Oluso.Core.UserJourneys;
using Oluso.UI.ViewModels;

namespace Oluso.UI.Pages.Account;

/// <summary>
/// Standalone consent page for OAuth/OIDC authorization
/// </summary>
[Authorize]
public class ConsentModel : PageModel
{
    private readonly IProtocolStateStore _protocolStateStore;
    private readonly IConsentStore _consentStore;
    private readonly IClientStore _clientStore;
    private readonly ITenantContext _tenantContext;
    private readonly ILogger<ConsentModel> _logger;

    public ConsentModel(
        IProtocolStateStore protocolStateStore,
        IConsentStore consentStore,
        IClientStore clientStore,
        ITenantContext tenantContext,
        ILogger<ConsentModel> logger)
    {
        _protocolStateStore = protocolStateStore;
        _consentStore = consentStore;
        _clientStore = clientStore;
        _tenantContext = tenantContext;
        _logger = logger;
    }

    [BindProperty(SupportsGet = true)]
    public string? ReturnUrl { get; set; }

    [BindProperty(SupportsGet = true)]
    public string? CorrelationId { get; set; }

    public string? ErrorMessage { get; set; }
    public string? ClientName { get; set; }
    public string? ClientLogoUrl { get; set; }
    public string? UserEmail { get; set; }
    public IList<ScopeViewModel>? RequestedScopes { get; set; }
    public bool ShowRememberConsent { get; set; } = true;
    public JourneyUiConfiguration? UiConfig { get; set; }

    public async Task<IActionResult> OnGetAsync()
    {
        if (string.IsNullOrEmpty(CorrelationId))
        {
            ErrorMessage = "Invalid consent request";
            return Page();
        }

        // Get protocol state
        var state = await _protocolStateStore.GetAsync(CorrelationId);
        if (state == null)
        {
            ErrorMessage = "Session expired. Please try again.";
            return Page();
        }

        // Load client info
        ClientName = state.Properties.TryGetValue("ClientName", out var name) ? name : state.ClientId;
        ClientLogoUrl = state.Properties.TryGetValue("ClientLogoUrl", out var logo) ? logo : null;

        // Get user info
        UserEmail = User.FindFirst("email")?.Value ?? User.Identity?.Name;

        // Parse requested scopes
        var scopesRaw = state.Properties.TryGetValue("RequestedScopes", out var scopes) ? scopes : "";
        RequestedScopes = ParseScopes(scopesRaw);

        return Page();
    }

    public async Task<IActionResult> OnPostAsync(string button, string[] grantedScopes, bool rememberConsent)
    {
        if (string.IsNullOrEmpty(CorrelationId))
        {
            ErrorMessage = "Invalid consent request";
            return Page();
        }

        var state = await _protocolStateStore.GetAsync(CorrelationId);
        if (state == null)
        {
            ErrorMessage = "Session expired. Please try again.";
            return Page();
        }

        if (button == "deny")
        {
            _logger.LogInformation("User denied consent for client {ClientId}", state.ClientId);

            // Redirect back to protocol with error
            if (!string.IsNullOrEmpty(ReturnUrl))
            {
                var separator = ReturnUrl.Contains("?") ? "&" : "?";
                return Redirect($"{ReturnUrl}{separator}error=access_denied&error_description=User+denied+consent");
            }

            return RedirectToPage("/Error", new { message = "Access was denied" });
        }

        // User allowed - store consent
        var userId = User.FindFirst("sub")?.Value ?? User.FindFirst(System.Security.Claims.ClaimTypes.NameIdentifier)?.Value;

        _logger.LogInformation(
            "User {UserId} granted consent for client {ClientId}, scopes: {Scopes}, remember: {Remember}",
            userId, state.ClientId, string.Join(" ", grantedScopes), rememberConsent);

        // Store consent grant if rememberConsent is true and we have a valid user
        if (rememberConsent && !string.IsNullOrEmpty(userId) && !string.IsNullOrEmpty(state.ClientId))
        {
            // Get client to check consent lifetime setting
            var client = await _clientStore.FindClientByIdAsync(state.ClientId);
            DateTime? expiresAt = null;
            if (client?.ConsentLifetime is > 0)
            {
                expiresAt = DateTime.UtcNow.AddSeconds(client.ConsentLifetime.Value);
            }

            var consent = new Consent
            {
                SubjectId = userId,
                ClientId = state.ClientId,
                TenantId = _tenantContext.TenantId,
                CreatedAt = DateTime.UtcNow,
                ExpiresAt = expiresAt
            };
            consent.SetScopes(grantedScopes);

            await _consentStore.StoreConsentAsync(consent);

            _logger.LogInformation(
                "Persisted consent for user {UserId}, client {ClientId}, expires: {Expires}",
                userId, state.ClientId, expiresAt?.ToString("o") ?? "never");
        }

        // Redirect back to protocol callback
        if (!string.IsNullOrEmpty(ReturnUrl))
        {
            var separator = ReturnUrl.Contains("?") ? "&" : "?";
            var scopeParam = Uri.EscapeDataString(string.Join(" ", grantedScopes));
            return Redirect($"{ReturnUrl}{separator}consented_scopes={scopeParam}");
        }

        return RedirectToPage("/Index");
    }

    private IList<ScopeViewModel> ParseScopes(string scopesRaw)
    {
        if (string.IsNullOrEmpty(scopesRaw))
            return new List<ScopeViewModel>();

        var scopes = scopesRaw.Split(' ', StringSplitOptions.RemoveEmptyEntries);
        return scopes.Select(s => new ScopeViewModel
        {
            Name = s,
            DisplayName = GetScopeDisplayName(s),
            Description = GetScopeDescription(s),
            Required = s == "openid",
            Checked = true
        }).ToList();
    }

    private static string GetScopeDisplayName(string scope) => scope switch
    {
        "openid" => "OpenID",
        "profile" => "Profile Information",
        "email" => "Email Address",
        "address" => "Physical Address",
        "phone" => "Phone Number",
        "offline_access" => "Offline Access",
        _ => scope
    };

    private static string GetScopeDescription(string scope) => scope switch
    {
        "openid" => "Required for authentication",
        "profile" => "Your name and profile details",
        "email" => "Your email address",
        "address" => "Your physical address",
        "phone" => "Your phone number",
        "offline_access" => "Access your data when you're not using the app",
        _ => $"Access to {scope}"
    };
}
