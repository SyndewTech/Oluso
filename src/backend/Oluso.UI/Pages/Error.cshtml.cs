using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Oluso.Core.Domain.Interfaces;
using Oluso.Core.Services;
using Oluso.Core.UserJourneys;
using Oluso.UI.Pages.Journey;
using System.Diagnostics;

namespace Oluso.UI.Pages;

/// <summary>
/// Error page model. The way back is a local <c>returnUrl</c>, the registered homepage of the
/// application named by <c>client_id</c>, or the success address configured on the journey named
/// by <c>journey</c>; it is never an address taken from the query as-is. The page wears the
/// same branding as the journey: that journey's UI settings, else the tenant's branding.
/// </summary>
public class ErrorModel : PageModel
{
    private readonly IClientStore? _clients;
    private readonly IJourneyPolicyStore? _journeys;
    private readonly ITenantSettingsProvider? _settingsProvider;
    private readonly ITenantContext? _tenantContext;
    private readonly IConfiguration? _configuration;
    private readonly ILogger<ErrorModel> _logger;

    public ErrorModel(
        IClientStore? clients = null,
        IJourneyPolicyStore? journeys = null,
        ITenantSettingsProvider? settingsProvider = null,
        ITenantContext? tenantContext = null,
        IConfiguration? configuration = null,
        ILogger<ErrorModel>? logger = null)
    {
        _clients = clients;
        _journeys = journeys;
        _settingsProvider = settingsProvider;
        _tenantContext = tenantContext;
        _configuration = configuration;
        _logger = logger ?? NullLogger<ErrorModel>.Instance;
    }

    public JourneyBrandView Brand { get; private set; } = JourneyBrandView.Default;

    [BindProperty(SupportsGet = true)]
    public string? Error { get; set; }

    [BindProperty(SupportsGet = true)]
    public string? Message { get; set; }

    [BindProperty(SupportsGet = true, Name = "error_description")]
    public string? ErrorDescription { get; set; }

    [BindProperty(SupportsGet = true)]
    public string? ReturnUrl { get; set; }

    /// <summary>The application the user came from; its registered homepage is the way back.</summary>
    [BindProperty(SupportsGet = true, Name = "client_id")]
    public string? ClientId { get; set; }

    /// <summary>The journey (by slug) the user was on; its configured success address is the way back.</summary>
    [BindProperty(SupportsGet = true, Name = "journey")]
    public string? Journey { get; set; }

    public string? ErrorCode { get; set; }
    public string? RequestId { get; set; }
    public bool ShowRetry { get; set; } = true;

    /// <summary>Where "Go back" leads, or null for the home link.</summary>
    public string? BackUrl { get; set; }

    public async Task OnGetAsync(CancellationToken cancellationToken)
    {
        RequestId = Activity.Current?.Id ?? HttpContext.TraceIdentifier;

        // Map common OAuth error codes to friendly messages
        ErrorCode = Error;
        if (string.IsNullOrEmpty(Message))
        {
            Message = ErrorDescription;
        }
        if (string.IsNullOrEmpty(Message))
        {
            Message = Error switch
            {
                "access_denied" => "Access was denied. You may have cancelled the authentication or don't have permission.",
                "invalid_request" => "The request was invalid. Please try again.",
                "unauthorized_client" => "The application is not authorized to request authentication.",
                "unsupported_response_type" => "The authentication type is not supported.",
                "invalid_scope" => "The requested permissions are invalid.",
                "server_error" => "A server error occurred. Please try again later.",
                "temporarily_unavailable" => "The service is temporarily unavailable. Please try again later.",
                "login_required" => "Please sign in to continue.",
                "consent_required" => "Your consent is required to continue.",
                "interaction_required" => "User interaction is required to continue.",
                _ => Error != null ? $"An error occurred: {Error}" : "An unexpected error occurred."
            };
        }

        // Don't show retry for certain error types
        ShowRetry = Error switch
        {
            "access_denied" => false,
            "unauthorized_client" => false,
            _ => true
        };

        var policy = string.IsNullOrWhiteSpace(Journey) || _journeys == null
            ? null
            : await _journeys.GetBySlugAsync(Journey, cancellationToken);

        Brand = await JourneyBrandView.ResolveAsync(policy?.Ui, _settingsProvider, _tenantContext, _configuration, _logger);

        BackUrl = !string.IsNullOrEmpty(ReturnUrl) && Url.IsLocalUrl(ReturnUrl)
            ? ReturnUrl
            : await ClientHomeAsync(cancellationToken) ?? JourneyHome(policy);
    }

    /// <summary>The address an admin configured for the journey to send people after they finish.</summary>
    private string? JourneyHome(JourneyPolicy? policy)
    {
        if (policy == null) return null;
        if (!string.IsNullOrEmpty(policy.SuccessRedirectUrl) && Url.IsLocalUrl(policy.SuccessRedirectUrl)) return policy.SuccessRedirectUrl;
        return IsWebAddress(policy.SuccessRedirectUrl, out var uri) ? uri.ToString() : null;
    }

    /// <summary>
    /// The named client's homepage (its client URI), else the address its first redirect URI is
    /// on. Only http(s) addresses registered for the client qualify.
    /// </summary>
    private async Task<string?> ClientHomeAsync(CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(ClientId) || _clients == null) return null;

        var client = await _clients.FindClientByIdAsync(ClientId, cancellationToken);
        if (client is not { Enabled: true }) return null;

        if (IsWebAddress(client.ClientUri, out var home)) return home.ToString();

        foreach (var redirect in client.RedirectUris)
        {
            if (IsWebAddress(redirect.RedirectUri, out var uri)) return uri.GetLeftPart(UriPartial.Authority) + "/";
        }
        return null;
    }

    private static bool IsWebAddress(string? value, out Uri uri)
    {
        uri = null!;
        return !string.IsNullOrWhiteSpace(value)
            && Uri.TryCreate(value, UriKind.Absolute, out uri!)
            && (uri.Scheme == Uri.UriSchemeHttps || uri.Scheme == Uri.UriSchemeHttp);
    }
}
