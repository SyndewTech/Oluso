using System.IdentityModel.Tokens.Jwt;
using System.Text.Json;
using Microsoft.AspNetCore.Antiforgery;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Logging;
using Oluso.Core.Domain.Interfaces;
using Oluso.Core.Events;
using Oluso.Core.Protocols;
using Oluso.Core.Protocols.Models;
using Oluso.Core.Protocols.Validation;
using Oluso.Core.Services;

namespace Oluso.Protocols.Oidc;

/// <summary>
/// OpenID Connect End Session Endpoint (RP-Initiated Logout 1.0).
/// Route is configured via OidcEndpointRouteConvention.
/// <list type="bullet">
/// <item>An <c>id_token_hint</c> must be an ID token this server issued (section 2: the OP MUST validate it was the
/// issuer); it then identifies the client, and a <c>client_id</c> sent with it must be that client.</item>
/// <item>A signed-in user is signed out without asking only when a verified hint names them. Otherwise the user
/// confirms on <see cref="ConfirmationPage"/> (section 2: the OP SHOULD ask), which posts the request back with an
/// anti-forgery token, so another site cannot sign people out or revoke their grants.</item>
/// <item><c>post_logout_redirect_uri</c> is used only when it is registered for the identified client.</item>
/// </list>
/// </summary>
public class OidcEndSessionController : OidcControllerBase
{
    /// <summary>The page that asks the user to confirm signing out.</summary>
    public const string ConfirmationPage = "/account/signout";

    /// <summary>The form field carrying the protected logout request back from <see cref="ConfirmationPage"/>.</summary>
    public const string LogoutIdField = "logout_id";

    /// <summary>The Data Protection purpose of a pending logout request.</summary>
    public const string LogoutRequestPurpose = "Oluso.EndSession.PendingLogout";

    /// <summary>The Data Protection purpose of the signed-out page's content (a different payload, never interchangeable).</summary>
    public const string SignedOutPurpose = "Oluso.EndSession.SignedOut";

    /// <summary>How long the user has to confirm.</summary>
    public static readonly TimeSpan ConfirmationLifetime = TimeSpan.FromMinutes(10);

    /// <summary>The page that loads the clients' front-channel logout URIs after the user is signed out.</summary>
    public const string SignedOutPagePath = "/account/signedout";

    /// <summary>Where a sign-out ends when the client named no post_logout_redirect_uri.</summary>
    public const string DefaultSignedOutPath = "/account/logout?loggedOut=true";

    private readonly IClientStore _clientStore;
    private readonly IPersistedGrantStore _grantStore;
    private readonly IRedirectUriValidator _redirectUriValidator;
    private readonly IServerSideSessionStore? _sessionStore;
    private readonly IBackchannelLogoutService? _backchannelLogoutService;
    private readonly ITenantContext _tenantContext;
    private readonly IOlusoEventService? _eventService;
    private readonly ISessionHintMatcher _sessionHints;
    private readonly IIssuerResolver _issuerResolver;
    private readonly IAntiforgery _antiforgery;
    private readonly ITimeLimitedDataProtector _pendingLogouts;
    private readonly ITimeLimitedDataProtector _signedOutPages;
    private readonly ILogger<OidcEndSessionController> _logger;

    public OidcEndSessionController(
        IClientStore clientStore,
        IPersistedGrantStore grantStore,
        IRedirectUriValidator redirectUriValidator,
        ITenantContext tenantContext,
        ISessionHintMatcher sessionHints,
        IIssuerResolver issuerResolver,
        IAntiforgery antiforgery,
        IDataProtectionProvider dataProtectionProvider,
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
        _sessionHints = sessionHints;
        _issuerResolver = issuerResolver;
        _antiforgery = antiforgery;
        _pendingLogouts = dataProtectionProvider.CreateProtector(LogoutRequestPurpose).ToTimeLimitedDataProtector();
        _signedOutPages = dataProtectionProvider.CreateProtector(SignedOutPurpose).ToTimeLimitedDataProtector();
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
        string? logoutId = null;

        if (Request.Method == HttpMethods.Post)
        {
            var form = await Request.ReadFormAsync(cancellationToken);
            idTokenHint = form["id_token_hint"].FirstOrDefault();
            postLogoutRedirectUri = form["post_logout_redirect_uri"].FirstOrDefault();
            state = form["state"].FirstOrDefault();
            clientId = form["client_id"].FirstOrDefault();
            logoutId = form[LogoutIdField].FirstOrDefault();
        }
        else
        {
            idTokenHint = Request.Query["id_token_hint"].FirstOrDefault();
            postLogoutRedirectUri = Request.Query["post_logout_redirect_uri"].FirstOrDefault();
            state = Request.Query["state"].FirstOrDefault();
            clientId = Request.Query["client_id"].FirstOrDefault();
        }

        // Explicitly authenticate from cookie to get user claims
        // (this endpoint doesn't have [Authorize] so User may not be populated)
        var authResult = await HttpContext.AuthenticateAsync(IdentityConstants.ApplicationScheme);
        var principal = authResult.Principal;
        var isAuthenticated = authResult.Succeeded && principal?.Identity?.IsAuthenticated == true;

        // Get subject and session from authenticated user
        var subjectId = principal?.FindFirst("sub")?.Value;
        var sessionId = principal?.FindFirst("sid")?.Value;

        if (!string.IsNullOrEmpty(logoutId))
        {
            // The user confirmed on the confirmation page: the post must carry its anti-forgery token, and the
            // request is the one validated before the user was asked, for the user who was asked.
            if (!await _antiforgery.IsRequestValidAsync(HttpContext))
            {
                return LogoutError("The sign-out request could not be verified. Please try again.");
            }

            var pending = ReadPendingLogout(logoutId);
            // A session that has meanwhile ended has nothing left to sign out, but a different user's must not be.
            if (pending == null || (subjectId != null && !string.Equals(pending.SubjectId, subjectId, StringComparison.Ordinal)))
            {
                return LogoutError("The sign-out request has expired. Please try again.");
            }

            clientId = pending.ClientId;
            postLogoutRedirectUri = pending.PostLogoutRedirectUri;
            state = pending.State;
        }
        else
        {
            Core.Domain.Entities.Client? client = null;
            string? hintSubject = null;

            // An id_token_hint must be an ID token this server issued; it then names the client.
            if (!string.IsNullOrEmpty(idTokenHint))
            {
                var hintClientId = clientId ?? UnverifiedAudience(idTokenHint);
                hintSubject = hintClientId == null
                    ? null
                    : await _sessionHints.ReadIdTokenHintSubjectAsync(idTokenHint, hintClientId, cancellationToken);
                if (hintSubject == null)
                {
                    _logger.LogWarning("End session request with an id_token_hint this server did not issue to {ClientId}", hintClientId);
                    return LogoutError("The sign-out request is not valid.");
                }
                clientId = hintClientId;
            }

            if (!string.IsNullOrEmpty(clientId))
            {
                client = await _clientStore.FindClientByIdAsync(clientId, cancellationToken);
                if (client == null)
                {
                    _logger.LogWarning("End session request for unknown client {ClientId}", clientId);
                    return LogoutError("The application that initiated logout could not be found");
                }
            }

            // Validate post_logout_redirect_uri if provided
            if (!string.IsNullOrEmpty(postLogoutRedirectUri))
            {
                if (client == null)
                {
                    _logger.LogWarning("End session request with post_logout_redirect_uri but no client_id or id_token_hint");
                    return LogoutError("client_id or id_token_hint is required when post_logout_redirect_uri is specified");
                }

                var postLogoutValidation = await _redirectUriValidator.ValidatePostLogoutAsync(
                    postLogoutRedirectUri,
                    client.PostLogoutRedirectUris.Select(u => u.PostLogoutRedirectUri!));

                // An unregistered URI makes the request invalid: never redirected to, and nobody is signed out by a
                // request the client got wrong (RP-Initiated Logout 1.0 section 3).
                if (!postLogoutValidation.IsValid)
                {
                    _logger.LogWarning("Invalid post_logout_redirect_uri {Uri} for client {ClientId}. Registered URIs: {RegisteredUris}",
                        postLogoutRedirectUri, clientId,
                        string.Join(", ", client.PostLogoutRedirectUris.Select(u => u.PostLogoutRedirectUri)));
                    return LogoutError("The post_logout_redirect_uri is not registered for this application");
                }
            }

            // Sign a user out without asking only when a verified hint names them (the sub their client was issued,
            // pairwise for some clients). Anyone else confirms first.
            var hintNamesSessionUser = hintSubject != null && client != null && subjectId != null &&
                string.Equals(hintSubject, SubjectIdentifiers.ForClient(subjectId, client.ClientId, client.PairWiseSubjectSalt), StringComparison.Ordinal);
            if (isAuthenticated && !hintNamesSessionUser)
            {
                var pending = new PendingLogout(subjectId, clientId, postLogoutRedirectUri, state);
                var protectedRequest = _pendingLogouts.Protect(JsonSerializer.Serialize(pending), ConfirmationLifetime);
                return Redirect($"{ConfirmationPage}?{LogoutIdField}={Uri.EscapeDataString(protectedRequest)}");
            }
        }

        // The clients that took part in this sign-in session, read before any of its grants are removed: they are
        // the ones told (back-channel now, front-channel on the signed-out page) and whose session grants end.
        var sessionGrants = !string.IsNullOrEmpty(subjectId) && !string.IsNullOrEmpty(sessionId)
            ? (await _grantStore.GetAllAsync(new PersistedGrantFilter { SubjectId = subjectId, SessionId = sessionId }, cancellationToken)).ToList()
            : new List<Core.Domain.Entities.PersistedGrant>();
        var frontChannelFrames = await FrontChannelFramesAsync(sessionGrants, sessionId, cancellationToken);

        // Back-channel logout to this session's clients only: another sign-in of the same user has another sid.
        if (!string.IsNullOrEmpty(subjectId) && !string.IsNullOrEmpty(sessionId) && _backchannelLogoutService != null)
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

        // End the session's grants: everything of the client that asked to sign out, and every other client's
        // session-bound grants. A refresh token another client was granted with offline_access is kept: it exists to
        // work while the user is not signed in (OpenID Connect Core 1.0 section 11). Other sessions are untouched.
        foreach (var grant in sessionGrants)
        {
            var initiatingClient = !string.IsNullOrEmpty(clientId) && grant.ClientId == clientId;
            var offline = grant.Type == OidcConstants.PersistedGrantTypes.RefreshToken &&
                global::Oluso.Services.BackchannelLogoutService.ScopesOf(grant).Contains(OidcConstants.Scopes.OfflineAccess);
            if (initiatingClient || !offline)
            {
                await _grantStore.RemoveAsync(grant.Key, cancellationToken);
            }
        }
        if (sessionGrants.Count > 0)
        {
            _logger.LogInformation("Ended {Count} grants of session {SessionId} for user {SubjectId}",
                sessionGrants.Count, sessionId, subjectId);
        }

        // Sign out the user from the authentication system
        if (isAuthenticated)
        {
            _logger.LogInformation("Signing out user {SubjectId}", subjectId);

            // Sign out from Identity cookie authentication
            await HttpContext.SignOutAsync(IdentityConstants.ApplicationScheme);
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

        // Where the user ends up: the registered URI exactly as registered, with state appended (RP-Initiated Logout
        // 1.0 section 3), or the default signed-out page.
        var finalRedirect = string.IsNullOrEmpty(postLogoutRedirectUri)
            ? DefaultSignedOutPath
            : string.IsNullOrEmpty(state)
                ? postLogoutRedirectUri
                : Microsoft.AspNetCore.WebUtilities.QueryHelpers.AddQueryString(postLogoutRedirectUri, "state", state);

        // Front-Channel Logout 1.0: the signed-out page loads each client's frontchannel_logout_uri, then goes on.
        if (frontChannelFrames.Count > 0)
        {
            var signedOut = new SignedOutPage(frontChannelFrames, finalRedirect);
            var protectedPage = _signedOutPages.Protect(JsonSerializer.Serialize(signedOut), ConfirmationLifetime);
            return Redirect($"{SignedOutPagePath}?{LogoutIdField}={Uri.EscapeDataString(protectedPage)}");
        }

        return Redirect(finalRedirect);
    }

    /// <summary>
    /// The client an ID token says it was issued to (its azp, else its single audience), read without verifying it:
    /// only to pick the audience the hint is then verified against.
    /// </summary>
    private static string? UnverifiedAudience(string idToken)
    {
        var handler = new JwtSecurityTokenHandler { MapInboundClaims = false };
        if (!handler.CanReadToken(idToken))
        {
            return null;
        }

        try
        {
            var jwt = handler.ReadJwtToken(idToken);
            return jwt.Claims.FirstOrDefault(c => c.Type == JwtRegisteredClaimNames.Azp)?.Value
                ?? (jwt.Audiences.Count() == 1 ? jwt.Audiences.First() : null);
        }
        catch (ArgumentException)
        {
            return null;
        }
    }

    private PendingLogout? ReadPendingLogout(string logoutId)
    {
        try
        {
            return JsonSerializer.Deserialize<PendingLogout>(_pendingLogouts.Unprotect(logoutId));
        }
        catch (Exception ex) when (ex is System.Security.Cryptography.CryptographicException or JsonException or FormatException)
        {
            return null;
        }
    }

    private IActionResult LogoutError(string message) =>
        Redirect($"/error?error=invalid_request&message={Uri.EscapeDataString(message)}");

    /// <summary>
    /// Front-Channel Logout 1.0 section 2: the frontchannel_logout_uri of each client that took part in the session,
    /// with iss and sid when the client asked for them. A URI is used only if its scheme, host and port are those of
    /// one of the client's registered redirect URIs, as the specification requires of it.
    /// </summary>
    private async Task<List<string>> FrontChannelFramesAsync(
        IReadOnlyCollection<Core.Domain.Entities.PersistedGrant> sessionGrants,
        string? sessionId,
        CancellationToken cancellationToken)
    {
        var frames = new List<string>();
        if (sessionGrants.Count == 0 || string.IsNullOrEmpty(sessionId))
        {
            return frames;
        }

        string? issuer = null;
        foreach (var participant in sessionGrants.Select(g => g.ClientId).Where(c => !string.IsNullOrEmpty(c)).Distinct())
        {
            var client = await _clientStore.FindClientByIdAsync(participant!, cancellationToken);
            if (client == null || string.IsNullOrEmpty(client.FrontChannelLogoutUri) ||
                !Uri.TryCreate(client.FrontChannelLogoutUri, UriKind.Absolute, out var logoutUri) ||
                (logoutUri.Scheme != Uri.UriSchemeHttps && !logoutUri.IsLoopback) ||
                !client.RedirectUris.Any(r => Uri.TryCreate(r.RedirectUri, UriKind.Absolute, out var registered) &&
                    string.Equals(registered.Scheme, logoutUri.Scheme, StringComparison.OrdinalIgnoreCase) &&
                    string.Equals(registered.Host, logoutUri.Host, StringComparison.OrdinalIgnoreCase) &&
                    registered.Port == logoutUri.Port))
            {
                if (!string.IsNullOrEmpty(client?.FrontChannelLogoutUri))
                {
                    _logger.LogWarning("Front-channel logout URI of client {ClientId} does not match a registered redirect URI; not used", participant);
                }
                continue;
            }

            var frame = client.FrontChannelLogoutUri;
            if (client.FrontChannelLogoutSessionRequired)
            {
                issuer ??= await _issuerResolver.GetIssuerAsync(cancellationToken);
                frame = Microsoft.AspNetCore.WebUtilities.QueryHelpers.AddQueryString(frame, new Dictionary<string, string?>
                {
                    ["iss"] = issuer,
                    ["sid"] = sessionId
                });
            }
            frames.Add(frame);
        }

        return frames;
    }

    /// <summary>The signed-out page's content: the front-channel logout URIs to load, then where to go.</summary>
    public sealed record SignedOutPage(IReadOnlyList<string> Frames, string Continue);

    /// <summary>
    /// A logout request validated before the user was asked to confirm it: who was signed in, the client, and the
    /// already-validated post_logout_redirect_uri and state.
    /// </summary>
    public sealed record PendingLogout(string? SubjectId, string? ClientId, string? PostLogoutRedirectUri, string? State);
}
