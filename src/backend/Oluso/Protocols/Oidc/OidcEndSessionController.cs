using System.IdentityModel.Tokens.Jwt;
using System.Web;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Logging;
using Oluso.Core.Domain.Interfaces;
using Oluso.Core.Events;
using Oluso.Core.Protocols.Models;
using Oluso.Core.Protocols.Validation;
using Oluso.Core.Services;

namespace Oluso.Protocols.Oidc;

/// <summary>
/// OpenID Connect End Session Endpoint (RP-Initiated Logout).
/// Route is configured via OidcEndpointRouteConvention.
/// </summary>
public class OidcEndSessionController : OidcControllerBase
{
    private readonly IClientStore _clientStore;
    private readonly IPersistedGrantStore _grantStore;
    private readonly IRedirectUriValidator _redirectUriValidator;
    private readonly IServerSideSessionStore? _sessionStore;
    private readonly IBackchannelLogoutService? _backchannelLogoutService;
    private readonly ITenantContext _tenantContext;
    private readonly IOlusoEventService? _eventService;
    private readonly ILogger<OidcEndSessionController> _logger;

    public OidcEndSessionController(
        IClientStore clientStore,
        IPersistedGrantStore grantStore,
        IRedirectUriValidator redirectUriValidator,
        ITenantContext tenantContext,
        ILogger<OidcEndSessionController> logger,
        IServerSideSessionStore? sessionStore = null,
        IBackchannelLogoutService? backchannelLogoutService = null,
        IOlusoEventService? eventService = null)
    {
        _clientStore = clientStore;
        _grantStore = grantStore;
        _redirectUriValidator = redirectUriValidator;
        _sessionStore = sessionStore;
        _backchannelLogoutService = backchannelLogoutService;
        _tenantContext = tenantContext;
        _eventService = eventService;
        _logger = logger;
    }

    [HttpGet]
    [HttpPost]
    public async Task<IActionResult> EndSession(CancellationToken cancellationToken)
    {
        string? idTokenHint = null;
        string? postLogoutRedirectUri = null;
        string? state = null;
        string? clientId = null;

        if (Request.Method == HttpMethods.Post)
        {
            var form = await Request.ReadFormAsync(cancellationToken);
            idTokenHint = form["id_token_hint"].FirstOrDefault();
            postLogoutRedirectUri = form["post_logout_redirect_uri"].FirstOrDefault();
            state = form["state"].FirstOrDefault();
            clientId = form["client_id"].FirstOrDefault();
        }
        else
        {
            idTokenHint = Request.Query["id_token_hint"].FirstOrDefault();
            postLogoutRedirectUri = Request.Query["post_logout_redirect_uri"].FirstOrDefault();
            state = Request.Query["state"].FirstOrDefault();
            clientId = Request.Query["client_id"].FirstOrDefault();
        }

        // Try to determine client from id_token_hint
        if (!string.IsNullOrEmpty(idTokenHint))
        {
            clientId ??= ExtractClientIdFromIdToken(idTokenHint);
        }

        // Validate post_logout_redirect_uri if provided
        if (!string.IsNullOrEmpty(postLogoutRedirectUri))
        {
            if (string.IsNullOrEmpty(clientId))
            {
                _logger.LogWarning("End session request with post_logout_redirect_uri but no client_id or id_token_hint");
                return Redirect($"/error?error=invalid_request&message={Uri.EscapeDataString("client_id or id_token_hint is required when post_logout_redirect_uri is specified")}");
            }

            var client = await _clientStore.FindClientByIdAsync(clientId, cancellationToken);
            if (client == null)
            {
                _logger.LogWarning("End session request for unknown client {ClientId}", clientId);
                return Redirect($"/error?error=invalid_request&message={Uri.EscapeDataString("The application that initiated logout could not be found")}");
            }

            var postLogoutValidation = await _redirectUriValidator.ValidatePostLogoutAsync(
                postLogoutRedirectUri,
                client.PostLogoutRedirectUris.Select(u => u.PostLogoutRedirectUri!));

            if (!postLogoutValidation.IsValid)
            {
                _logger.LogWarning("Invalid post_logout_redirect_uri {Uri} for client {ClientId}. Registered URIs: {RegisteredUris}",
                    postLogoutRedirectUri, clientId,
                    string.Join(", ", client.PostLogoutRedirectUris.Select(u => u.PostLogoutRedirectUri)));
                postLogoutRedirectUri = null;
            }
        }

        // Explicitly authenticate from cookie to get user claims
        // (this endpoint doesn't have [Authorize] so User may not be populated)
        var authResult = await HttpContext.AuthenticateAsync(IdentityConstants.ApplicationScheme);
        var principal = authResult.Principal;
        var isAuthenticated = authResult.Succeeded && principal?.Identity?.IsAuthenticated == true;

        // Get subject and session from authenticated user
        var subjectId = principal?.FindFirst("sub")?.Value;
        var sessionId = principal?.FindFirst("sid")?.Value;

        // Sign out the user from the authentication system
        if (isAuthenticated)
        {
            _logger.LogInformation("Signing out user {SubjectId}", subjectId);

            // Sign out from Identity cookie authentication
            await HttpContext.SignOutAsync(IdentityConstants.ApplicationScheme);
        }

        // Revoke tokens for this user/client combination
        if (!string.IsNullOrEmpty(subjectId) && !string.IsNullOrEmpty(clientId))
        {
            await _grantStore.RemoveAllBySubjectAndClientAsync(subjectId, clientId, cancellationToken);
            _logger.LogInformation("Revoked tokens for user {SubjectId}, client {ClientId}", subjectId, clientId);
        }

        // Send backchannel logout notifications to all clients with active sessions
        if (!string.IsNullOrEmpty(subjectId) && _backchannelLogoutService != null)
        {
            try
            {
                var logoutResult = await _backchannelLogoutService.SendLogoutNotificationsAsync(
                    subjectId, sessionId, cancellationToken);

                if (logoutResult.FailedClients.Count > 0)
                {
                    _logger.LogWarning(
                        "Backchannel logout completed with {FailedCount} failures for user {SubjectId}",
                        logoutResult.FailedClients.Count, subjectId);
                }
            }
            catch (Exception ex)
            {
                // Don't fail the logout if backchannel notifications fail
                _logger.LogError(ex, "Error sending backchannel logout notifications for user {SubjectId}", subjectId);
            }
        }

        // Remove server-side session
        if (!string.IsNullOrEmpty(sessionId) && _sessionStore != null)
        {
            await _sessionStore.DeleteBySessionIdAsync(sessionId, cancellationToken);
            _logger.LogInformation("Removed server-side session {SessionId} for user {SubjectId}", sessionId, subjectId);
        }

        // Raise user signed out event
        if (!string.IsNullOrEmpty(subjectId) && _eventService != null)
        {
            await _eventService.RaiseAsync(new UserSignedOutEvent
            {
                TenantId = _tenantContext.TenantId,
                SubjectId = subjectId,
                SessionId = sessionId
            }, cancellationToken);
        }

        // Build redirect URI
        if (!string.IsNullOrEmpty(postLogoutRedirectUri))
        {
            var redirectUri = postLogoutRedirectUri;
            if (!string.IsNullOrEmpty(state))
            {
                var uriBuilder = new UriBuilder(redirectUri);
                var query = HttpUtility.ParseQueryString(uriBuilder.Query);
                query["state"] = state;
                uriBuilder.Query = query.ToString();
                redirectUri = uriBuilder.ToString();
            }

            return Redirect(redirectUri);
        }

        // Default logout page
        return Redirect("/account/logout?loggedOut=true");
    }

    [HttpGet("callback")]
    public IActionResult LogoutCallback([FromQuery] string? postLogoutRedirectUri, [FromQuery] string? state)
    {
        if (!string.IsNullOrEmpty(postLogoutRedirectUri))
        {
            var redirectUri = postLogoutRedirectUri;
            if (!string.IsNullOrEmpty(state))
            {
                var uriBuilder = new UriBuilder(redirectUri);
                var query = HttpUtility.ParseQueryString(uriBuilder.Query);
                query["state"] = state;
                uriBuilder.Query = query.ToString();
                redirectUri = uriBuilder.ToString();
            }
            return Redirect(redirectUri);
        }

        return Redirect("/account/logout?loggedOut=true");
    }

    private string? ExtractClientIdFromIdToken(string idToken)
    {
        try
        {
            var handler = new JwtSecurityTokenHandler();
            if (!handler.CanReadToken(idToken))
            {
                return null;
            }

            var jwt = handler.ReadJwtToken(idToken);
            // Try azp first (authorized party), then client_id, then aud (audience)
            return jwt.Claims.FirstOrDefault(c => c.Type == "azp")?.Value
                ?? jwt.Claims.FirstOrDefault(c => c.Type == "client_id")?.Value
                ?? jwt.Audiences.FirstOrDefault();
        }
        catch
        {
            return null;
        }
    }
}
