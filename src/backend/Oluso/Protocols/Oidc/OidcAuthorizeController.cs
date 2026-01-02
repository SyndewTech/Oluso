using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Logging;
using Oluso.Core.Protocols;

namespace Oluso.Protocols.Oidc;

/// <summary>
/// OIDC Authorization endpoint controller.
/// Route is configured via OidcEndpointRouteConvention.
/// </summary>
public class OidcAuthorizeController : ControllerBase
{
    private readonly IOidcProtocolService _protocol;
    private readonly IAuthenticationCoordinator _coordinator;
    private readonly ILogger<OidcAuthorizeController> _logger;

    public OidcAuthorizeController(
        IOidcProtocolService protocol,
        IAuthenticationCoordinator coordinator,
        ILogger<OidcAuthorizeController> logger)
    {
        _protocol = protocol;
        _coordinator = coordinator;
        _logger = logger;
    }

    /// <summary>
    /// Authorization endpoint - GET and POST
    /// Policy ID comes from query parameter (e.g., ?policy=signin or ?p=signin)
    /// </summary>
    [HttpGet]
    [HttpPost]
    public async Task<IActionResult> Authorize(CancellationToken cancellationToken)
    {
        try
        {
            var context = await _protocol.BuildContextAsync(HttpContext, cancellationToken);
            var result = await _protocol.ProcessAuthorizeAsync(context, cancellationToken);

            _logger.LogDebug("Authorize result: {Type}, Error: {Error}",
                result.Type, result.Error?.Code ?? "none");

            return result.Type switch
            {
                ProtocolResultType.RequiresAuthentication
                    => await _coordinator.StartAuthenticationAsync(context, result.AuthRequirement!, cancellationToken),
                ProtocolResultType.RequiresConsent
                    => await HandleConsentRequiredAsync(context, result.ConsentRequirement!, cancellationToken),
                ProtocolResultType.Success
                    => await HandleAlreadyAuthenticatedAsync(context, cancellationToken),
                ProtocolResultType.DirectResponse
                    => result.Response!,
                ProtocolResultType.Failed
                    => _protocol.BuildErrorResponse(context, result.Error!),
                _ => _protocol.BuildErrorResponse(context, new ProtocolError { Code = "server_error" })
            };
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Unhandled exception in Authorize endpoint");
            return RedirectToErrorPage("server_error", "An unexpected error occurred. Please try again.");
        }
    }

    /// <summary>
    /// Handles the case where consent is required.
    /// Routes to journey with consent step or standalone consent page based on UI mode.
    /// </summary>
    private Task<IActionResult> HandleConsentRequiredAsync(
        ProtocolContext context,
        ConsentRequirement requirement,
        CancellationToken cancellationToken)
    {
        return _coordinator.HandleConsentRequiredAsync(context, requirement, cancellationToken);
    }

    /// <summary>
    /// Handles the case where user is already authenticated and doesn't need consent.
    /// Builds authentication result from current session and issues authorization code.
    /// </summary>
    private async Task<IActionResult> HandleAlreadyAuthenticatedAsync(
        ProtocolContext context,
        CancellationToken cancellationToken)
    {
        var user = HttpContext.User;
        var subjectId = user.FindFirst("sub")?.Value
            ?? user.FindFirst(System.Security.Claims.ClaimTypes.NameIdentifier)?.Value;

        if (string.IsNullOrEmpty(subjectId))
        {
            _logger.LogError("Cannot build authenticated response: no subject ID in claims");
            return _protocol.BuildErrorResponse(context, new ProtocolError
            {
                Code = "server_error",
                Description = "Unable to determine user identity"
            });
        }

        // Get scopes from context (set during validation)
        var scopes = context.Properties.TryGetValue("ValidScopes", out var scopesObj)
            ? (scopesObj as ICollection<string>)?.ToList() ?? new List<string>()
            : new List<string>();

        // Get session ID from claims
        var sessionId = user.FindFirst("sid")?.Value;

        // Build claims dictionary from user's claims
        var claims = new Dictionary<string, object>();
        foreach (var claim in user.Claims)
        {
            // Skip claims that are handled separately or are internal
            if (claim.Type is "sub" or "sid" or "auth_time" or "amr" or "acr" or "iat" or "exp" or "nbf" or "iss" or "aud")
                continue;

            claims[claim.Type] = claim.Value;
        }

        // Get auth_time from claims
        DateTime? authTime = null;
        var authTimeClaim = user.FindFirst("auth_time");
        if (authTimeClaim != null && long.TryParse(authTimeClaim.Value, out var authTimeUnix))
        {
            authTime = DateTimeOffset.FromUnixTimeSeconds(authTimeUnix).UtcDateTime;
        }

        // Get AMR from claims
        var amr = user.FindAll("amr").Select(c => c.Value).ToList();

        // Get ACR from claims
        var acr = user.FindFirst("acr")?.Value;

        var authResult = new AuthenticationResult
        {
            Succeeded = true,
            UserId = subjectId,
            SessionId = sessionId,
            Scopes = scopes,
            Claims = claims,
            AuthTime = authTime ?? DateTime.UtcNow,
            Amr = amr,
            Acr = acr
        };

        _logger.LogInformation(
            "Building authenticated response for already-authenticated user {UserId}, scopes: {Scopes}",
            subjectId, string.Join(" ", scopes));

        return await _protocol.BuildAuthenticatedResponseAsync(context, authResult, cancellationToken);
    }

    /// <summary>
    /// Callback from journey/standalone authentication
    /// </summary>
    [HttpGet("callback")]
    public async Task<IActionResult> Callback(
        [FromQuery(Name = "correlation_id")] string correlationId,
        [FromQuery(Name = "journey_id")] string? journeyId,
        CancellationToken cancellationToken)
    {
        if (string.IsNullOrEmpty(correlationId))
        {
            return RedirectToErrorPage("invalid_request", "Missing correlation_id");
        }

        var result = await _coordinator.HandleCallbackAsync(correlationId, journeyId, cancellationToken: cancellationToken);

        if (!result.Succeeded)
        {
            return RedirectToErrorPage(result.Error?.Code ?? "server_error", result.Error?.Description);
        }

        try
        {
            var context = await _protocol.RestoreContextAsync(correlationId, cancellationToken);
            return await _protocol.BuildAuthenticatedResponseAsync(context, result, cancellationToken);
        }
        catch (InvalidOperationException ex)
        {
            // Protocol state not found - session expired or invalid
            return RedirectToErrorPage("invalid_request", ex.Message.Contains("not found")
                ? "Session expired or invalid. Please try signing in again."
                : ex.Message);
        }
    }

    /// <summary>
    /// Redirects to the error page with error details
    /// </summary>
    private IActionResult RedirectToErrorPage(string error, string? message = null)
    {
        var errorUrl = $"/error?error={Uri.EscapeDataString(error)}";
        if (!string.IsNullOrEmpty(message))
        {
            errorUrl += $"&message={Uri.EscapeDataString(message)}";
        }
        return Redirect(errorUrl);
    }
}
