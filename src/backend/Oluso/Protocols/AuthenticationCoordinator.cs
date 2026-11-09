using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Oluso.Core.Protocols;
using Oluso.Core.Protocols.Models;
using Oluso.Core.Services;
using Oluso.Core.UserJourneys;

namespace Oluso.Protocols;

/// <summary>
/// Coordinates authentication between protocol services and UI (journey or standalone)
/// </summary>
public class AuthenticationCoordinator : IAuthenticationCoordinator
{
    private readonly IJourneyOrchestrator _journeyOrchestrator;
    private readonly IJourneyPolicyStore _policyStore;
    private readonly IJourneyStateStore _journeyStateStore;
    private readonly IProtocolStateStore _protocolStateStore;
    private readonly IHttpContextAccessor _httpContextAccessor;
    private readonly IOlusoUserService _userService;
    private readonly OidcEndpointConfiguration _oidcConfig;
    private readonly ILogger<AuthenticationCoordinator> _logger;
    private readonly ISessionHintMatcher? _sessionHints;

    public AuthenticationCoordinator(
        IJourneyOrchestrator journeyOrchestrator,
        IJourneyPolicyStore policyStore,
        IJourneyStateStore journeyStateStore,
        IProtocolStateStore protocolStateStore,
        IHttpContextAccessor httpContextAccessor,
        IOlusoUserService userService,
        IOptions<OidcEndpointConfiguration> oidcConfig,
        ILogger<AuthenticationCoordinator> logger,
        ISessionHintMatcher? sessionHints = null)
    {
        _sessionHints = sessionHints;
        _journeyOrchestrator = journeyOrchestrator;
        _policyStore = policyStore;
        _journeyStateStore = journeyStateStore;
        _protocolStateStore = protocolStateStore;
        _httpContextAccessor = httpContextAccessor;
        _userService = userService;
        _oidcConfig = oidcConfig.Value;
        _logger = logger;
    }

    public async Task<IActionResult> StartAuthenticationAsync(
        ProtocolContext context,
        AuthenticationRequirement requirement,
        CancellationToken cancellationToken = default)
    {
        // Check if we should skip login because user is already authenticated
        // unless prompt=login or prompt=create forces a new authentication
        var skipLogin = await ShouldSkipLoginAsync(requirement, cancellationToken);
        var uiMode = ResolveUiMode(context);

        // A requested policy still runs for a signed-in user: its login steps are met by
        // the session and the journey continues with the remaining steps.
        var runPolicyForSession = skipLogin.ShouldSkip && uiMode == UiMode.Journey
            && (!string.IsNullOrEmpty(requirement.ExplicitPolicyId) || !string.IsNullOrEmpty(context.PolicyId));

        // The session stands in for the journey's sign-in only for the person the request
        // names. A hint naming someone else gets the normal sign-in instead.
        var reuseSession = runPolicyForSession
            && await SessionMatchesHintsAsync(context, requirement, skipLogin.UserId!, cancellationToken);

        if (skipLogin.ShouldSkip && !runPolicyForSession)
        {
            _logger.LogDebug(
                "User {UserId} already authenticated, skipping login (prompt={Prompt})",
                skipLogin.UserId, requirement.Prompt ?? "none");

            // Store protocol state for the callback
            var correlationId = await StoreProtocolStateAsync(context, cancellationToken);
            context.CorrelationId = correlationId;

            // Build callback URL and redirect directly to it
            var callbackUrl = BuildCallbackUrl(context, correlationId);
            return new RedirectResult(callbackUrl);
        }

        context.UiMode = uiMode;

        // Store protocol state for callback
        var storedCorrelationId = await StoreProtocolStateAsync(context, cancellationToken);
        context.CorrelationId = storedCorrelationId;

        _logger.LogDebug(
            "Starting authentication for protocol {Protocol}, UI mode {UiMode}, correlation {CorrelationId}",
            context.ProtocolName, uiMode, storedCorrelationId);

        return uiMode switch
        {
            UiMode.Journey => await StartJourneyAuthAsync(
                context, requirement, storedCorrelationId,
                reuseSession ? skipLogin.UserId : null,
                reuseSession ? skipLogin.AuthTime : null,
                cancellationToken),
            UiMode.Standalone => StartStandaloneAuth(context, requirement, storedCorrelationId),
            UiMode.Headless => BuildHeadlessAuthResponse(context, requirement),
            _ => throw new InvalidOperationException($"Unknown UI mode: {uiMode}")
        };
    }

    private async Task<bool> SessionMatchesHintsAsync(
        ProtocolContext context,
        AuthenticationRequirement requirement,
        string sessionUserId,
        CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(requirement.IdTokenHint) && string.IsNullOrWhiteSpace(requirement.LoginHint))
        {
            return true;
        }

        var client = context.Properties.TryGetValue("ValidatedClient", out var validated) ? validated as Oluso.Core.Domain.Entities.Client : null;
        var clientId = client?.ClientId ?? context.Request?.ClientId;
        var matches = _sessionHints != null && !string.IsNullOrEmpty(clientId)
            && await _sessionHints.MatchesAsync(
                sessionUserId, clientId, client?.PairWiseSubjectSalt,
                requirement.IdTokenHint, requirement.LoginHint, cancellationToken);

        if (!matches)
        {
            _logger.LogInformation(
                "The request's hint doesn't name signed-in user {UserId}; the journey asks them to sign in", sessionUserId);
        }
        return matches;
    }

    private async Task<(bool ShouldSkip, string? UserId, DateTimeOffset? AuthTime)> ShouldSkipLoginAsync(
        AuthenticationRequirement requirement,
        CancellationToken cancellationToken)
    {
        var httpContext = _httpContextAccessor.HttpContext;
        if (httpContext == null)
        {
            return (false, null, null);
        }

        var authResult = await httpContext.AuthenticateAsync(IdentityConstants.ApplicationScheme);
        var isAuthenticated = authResult.Succeeded && authResult.Principal != null;
        var shouldSignOut = false;
        string? signOutReason = null;

        // Check conditions that require sign out and fresh login
        if (requirement.Prompt == PromptModes.Login || requirement.Prompt == PromptModes.Create)
        {
            shouldSignOut = isAuthenticated;
            signOutReason = $"prompt={requirement.Prompt} forces new authentication";
        }
        else if (requirement.ForceFreshLogin)
        {
            shouldSignOut = isAuthenticated;
            signOutReason = "ForceFreshLogin is set";
        }
        else if (isAuthenticated && requirement.MaxAge.HasValue)
        {
            var authTimeClaim = authResult.Principal!.FindFirst("auth_time")?.Value;
            if (!string.IsNullOrEmpty(authTimeClaim) && long.TryParse(authTimeClaim, out var authTimeUnix))
            {
                var authTime = DateTimeOffset.FromUnixTimeSeconds(authTimeUnix);
                var maxAge = TimeSpan.FromSeconds(requirement.MaxAge.Value);
                if (DateTimeOffset.UtcNow - authTime > maxAge)
                {
                    shouldSignOut = true;
                    signOutReason = $"authentication older than MaxAge ({requirement.MaxAge.Value}s)";
                }
            }
        }

        // If any condition requires sign out, do it and return false
        if (shouldSignOut)
        {
            _logger.LogDebug("Signing out existing session: {Reason}", signOutReason);
            await httpContext.SignOutAsync(IdentityConstants.ApplicationScheme);
            return (false, null, null);
        }

        // No existing authentication
        if (!isAuthenticated)
        {
            _logger.LogDebug("No existing authentication found");
            return (false, null, null);
        }

        // Get user ID from the authenticated principal
        var userId = authResult.Principal!.FindFirst(System.Security.Claims.ClaimTypes.NameIdentifier)?.Value
            ?? authResult.Principal.FindFirst("sub")?.Value;

        if (string.IsNullOrEmpty(userId))
        {
            _logger.LogDebug("Authenticated principal has no user ID claim");
            return (false, null, null);
        }

        // Verify user still exists
        var user = await _userService.FindByIdAsync(userId, cancellationToken);
        if (user == null)
        {
            _logger.LogWarning("Authenticated user {UserId} no longer exists in store, signing out", userId);
            await httpContext.SignOutAsync(IdentityConstants.ApplicationScheme);
            return (false, null, null);
        }

        _logger.LogDebug("User {UserId} is already authenticated, can skip login", userId);
        var signedInAt = long.TryParse(authResult.Principal.FindFirst("auth_time")?.Value, out var signedInUnix)
            ? DateTimeOffset.FromUnixTimeSeconds(signedInUnix)
            : (DateTimeOffset?)null;
        return (true, userId, signedInAt);
    }

    public async Task<AuthenticationResult> HandleCallbackAsync(
        string correlationId,
        string? journeyId,
        CancellationToken cancellationToken = default)
    {
        // Get protocol state
        var state = await _protocolStateStore.GetAsync(correlationId, cancellationToken);
        if (state == null)
        {
            _logger.LogWarning("Protocol state not found for correlation {CorrelationId}", correlationId);
            return AuthenticationResult.Failed("invalid_request", "Session expired or invalid");
        }

        // A hosted journey ran for this request: the signed-in user's result plus what the
        // journey collected.
        if (!string.IsNullOrEmpty(journeyId))
        {
            return await HandleJourneyCallbackAsync(state, correlationId, journeyId, cancellationToken);
        }

        // Standalone callback - user must be authenticated via cookie
        return await HandleStandaloneCallbackAsync(state, cancellationToken);
    }

    private async Task<AuthenticationResult> HandleStandaloneCallbackAsync(
        ProtocolState state,
        CancellationToken cancellationToken)
    {
        var httpContext = _httpContextAccessor.HttpContext;
        if (httpContext == null)
        {
            _logger.LogError("No HTTP context available for standalone callback");
            return AuthenticationResult.Failed("server_error", "No HTTP context");
        }

        // Explicitly authenticate from cookie - this is needed because the callback
        // endpoint doesn't have [Authorize] and the authentication middleware may not
        // have populated HttpContext.User yet
        var authResult = await httpContext.AuthenticateAsync(IdentityConstants.ApplicationScheme);
        if (!authResult.Succeeded || authResult.Principal == null)
        {
            _logger.LogWarning("User not authenticated in standalone callback. Auth failed: {Failure}",
                authResult.Failure?.Message ?? "No principal");
            return AuthenticationResult.Failed("access_denied", "User not authenticated");
        }

        var user = authResult.Principal;
        var userId = user.FindFirst(System.Security.Claims.ClaimTypes.NameIdentifier)?.Value
            ?? user.FindFirst("sub")?.Value;

        if (string.IsNullOrEmpty(userId))
        {
            _logger.LogWarning("No user ID claim in authenticated user");
            return AuthenticationResult.Failed("access_denied", "User identity not found");
        }

        // Get user info for claims
        var userInfo = await _userService.FindByIdAsync(userId, cancellationToken);
        if (userInfo == null)
        {
            _logger.LogWarning("User {UserId} not found in user store", userId);
            return AuthenticationResult.Failed("access_denied", "User not found");
        }

        _logger.LogInformation("Standalone authentication completed for user {UserId}", userId);

        // Only what describes the session goes with the code; the user's claims (name, email, ...) come
        // from the profile service at the token endpoint, for the scopes granted.
        var claims = new Dictionary<string, object>();
        if (!string.IsNullOrEmpty(userInfo.TenantId))
            claims["tenant_id"] = userInfo.TenantId;
        if (user.FindFirst("idp")?.Value is { Length: > 0 } idp)
            claims["idp"] = idp;

        // The login session is the cookie's: its sid is stamped by the ticket store when
        // the cookie is issued. Tokens minted with any other id never line up with the
        // session on the account's Sessions page and cannot be revoked with it.
        var sessionId = user.FindFirst("sid")?.Value ?? Guid.NewGuid().ToString("N");

        // "Remember me" made the cookie persistent; carry that into the tokens.
        if (authResult.Properties?.IsPersistent == true)
        {
            claims[Oluso.Core.Authorization.ReservedClaimKeys.RememberMe] = "true";
        }

        // Grant the scopes validated at /authorize (carried through ProtocolState),
        // not a hardcoded set. Hardcoding dropped offline_access, which suppressed
        // refresh-token issuance for every standalone-login client. Falls back to the
        // basic OIDC set only if no validated scopes were carried (e.g. non-OIDC).
        var grantedScopes = state.Properties.TryGetValue("GrantedScopes", out var gs)
                && !string.IsNullOrWhiteSpace(gs)
            ? gs.Split(' ', StringSplitOptions.RemoveEmptyEntries).ToList()
            : new List<string> { "openid", "profile", "email" };

        // How and when the session signed in (stamped on the cookie at sign-in) go with the code, so the
        // tokens carry the same amr and auth_time as codes issued to pages (IAuthorizationCodeIssuer).
        DateTime? authTime = long.TryParse(user.FindFirst("auth_time")?.Value, out var authTimeUnix)
            ? DateTimeOffset.FromUnixTimeSeconds(authTimeUnix).UtcDateTime
            : null;

        return AuthenticationResult.Success(
            userId: userId,
            sessionId: sessionId,
            scopes: grantedScopes,
            claims: claims,
            amr: user.FindAll("amr").Select(c => c.Value).Distinct(StringComparer.Ordinal).ToList(),
            authTime: authTime,
            acr: user.FindFirst("acr")?.Value);
    }

    public async Task<IActionResult> HandleConsentRequiredAsync(
        ProtocolContext context,
        ConsentRequirement requirement,
        CancellationToken cancellationToken = default)
    {
        // Resolve UI mode to determine how to handle consent
        var uiMode = ResolveUiMode(context);

        // Add consent info to context properties before storing
        context.Properties["ClientName"] = requirement.ClientName ?? "";
        context.Properties["RequestedScopes"] = string.Join(" ", requirement.RequestedScopes);
        context.Properties["SubjectId"] = requirement.SubjectId;

        // Store protocol state for callback (if not already stored)
        if (string.IsNullOrEmpty(context.CorrelationId))
        {
            context.CorrelationId = await StoreProtocolStateAsync(context, cancellationToken);
        }

        _logger.LogDebug(
            "Handling consent required for client {ClientId}, user {UserId}, UI mode {UiMode}",
            requirement.ClientId, requirement.SubjectId, uiMode);

        // Headless mode returns JSON response for client-side handling
        if (uiMode == UiMode.Headless)
        {
            return BuildHeadlessConsentResponse(requirement);
        }

        // For journey mode, check if there's a consent policy
        // If so, start a journey with callback URL pointing back to authorize
        if (uiMode == UiMode.Journey)
        {
            var policy = await _policyStore.GetByTypeAsync(JourneyType.Consent, cancellationToken);
            if (policy != null)
            {
                return await StartConsentJourneyAsync(context, requirement, policy, cancellationToken);
            }
            // No consent policy, fall through to standalone consent
            _logger.LogDebug("No consent policy found, using standalone consent page");
        }

        // Standalone mode or no consent policy: redirect to standalone consent page
        return StartStandaloneConsent(context, requirement);
    }

    private async Task<IActionResult> StartConsentJourneyAsync(
        ProtocolContext context,
        ConsentRequirement requirement,
        JourneyPolicy policy,
        CancellationToken cancellationToken)
    {
        // Build redirect URL - after journey completes, redirect back to authorize endpoint
        // The authorize endpoint will re-run and see consent is now granted
        var authorizeUrl = context.HttpContext.Request.Path + WithoutConsentDecision(context.HttpContext.Request.Query);
        var baseUrl = $"{context.HttpContext.Request.Scheme}://{context.HttpContext.Request.Host}";
        var callbackUrl = $"{baseUrl}{authorizeUrl}";

        // Start journey with consent policy
        var journeyState = await _journeyOrchestrator.StartJourneyAsync(
            policy,
            new JourneyStartContext
            {
                HttpContext = context.HttpContext,
                ProtocolName = context.ProtocolName,
                CorrelationId = context.CorrelationId,
                CallbackUrl = callbackUrl,
                RequestedScopes = requirement.RequestedScopes,
                UserId = requirement.SubjectId,
                Properties = new Dictionary<string, object>(context.Properties)
                {
                    ["client_id"] = requirement.ClientId,
                    ["scope"] = string.Join(" ", requirement.RequestedScopes)
                }
            },
            cancellationToken);

        context.JourneyId = journeyState.Id;

        // Back at /authorize, the completed journey is the proof of this request's consent.
        var withDecision = $"{callbackUrl}{(callbackUrl.Contains('?') ? '&' : '?')}{ConsentDecisions.JourneyParameter}={Uri.EscapeDataString(journeyState.Id)}";
        await _journeyStateStore.SaveAsync(journeyState with { CallbackUrl = withDecision }, cancellationToken);
        callbackUrl = withDecision;

        _logger.LogInformation(
            "Started consent journey {JourneyId} for client {ClientId}, user {UserId}, callback: {CallbackUrl}",
            journeyState.Id, requirement.ClientId, requirement.SubjectId, callbackUrl);

        return new RedirectResult($"/journey/{journeyState.Id}");
    }

    /// <summary>The request's query without a consent decision from an earlier round.</summary>
    private static QueryString WithoutConsentDecision(IQueryCollection query) =>
        QueryString.Create(query
            .Where(kv => kv.Key is not (ConsentDecisions.DecisionParameter or ConsentDecisions.JourneyParameter))
            .SelectMany(kv => kv.Value.Select(v => new KeyValuePair<string, string?>(kv.Key, v))));

    private IActionResult StartStandaloneConsent(
        ProtocolContext context,
        ConsentRequirement requirement)
    {
        // Build return URL - after consent, redirect back to authorize
        var returnUrl = context.HttpContext.Request.Path + WithoutConsentDecision(context.HttpContext.Request.Query);

        var consentUrl = $"/account/consent?correlationId={Uri.EscapeDataString(context.CorrelationId ?? "")}";
        consentUrl += $"&returnUrl={Uri.EscapeDataString(returnUrl)}";

        _logger.LogDebug("Redirecting to standalone consent page for client {ClientId}", requirement.ClientId);

        return new RedirectResult(consentUrl);
    }

    private static IActionResult BuildHeadlessConsentResponse(ConsentRequirement requirement)
    {
        // Return consent requirement for SPA/mobile clients to handle
        return new ObjectResult(new
        {
            error = "consent_required",
            error_description = "User consent is required",
            consent_requirements = new
            {
                client_id = requirement.ClientId,
                client_name = requirement.ClientName,
                scopes = requirement.RequestedScopes
            }
        })
        {
            StatusCode = 403
        };
    }

    public UiMode ResolveUiMode(ProtocolContext context)
    {
        // Priority: Tenant setting is authoritative, client can customize if tenant allows,
        // request parameter is lowest priority (can only fine-tune)

        // 1. Check tenant setting first (authoritative)
        var tenantUseJourney = true; // Default if no tenant
        var hasTenantSetting = context.Properties.TryGetValue("TenantUseJourneyFlow", out var tenantSetting);
        if (hasTenantSetting && tenantSetting is bool tenantValue)
        {
            tenantUseJourney = tenantValue;
        }

        _logger.LogDebug(
            "ResolveUiMode: TenantUseJourneyFlow present={HasSetting}, value={Value}, effective={Effective}",
            hasTenantSetting, tenantSetting, tenantUseJourney);

        // If tenant disables journey flow, that's final - no client or request can override
        if (!tenantUseJourney)
        {
            _logger.LogDebug("ResolveUiMode: Tenant disables journey, returning Standalone");
            return UiMode.Standalone;
        }

        // 2. Tenant allows journey - check if client has an explicit preference
        // Client.UseJourneyFlow is nullable: null = inherit tenant, true/false = explicit choice
        var hasClientSetting = context.Properties.TryGetValue("ClientUseJourneyFlow", out var clientSetting);
        _logger.LogDebug(
            "ResolveUiMode: ClientUseJourneyFlow present={HasSetting}, value={Value}",
            hasClientSetting, clientSetting);

        if (hasClientSetting && clientSetting is bool clientValue)
        {
            // Client explicitly disables journey - that's final, request cannot override
            if (!clientValue)
            {
                _logger.LogDebug("ResolveUiMode: Client disables journey, returning Standalone");
                return UiMode.Standalone;
            }
            // Client explicitly wants journey - continue to check request parameter
            // (request can still choose standalone within client's allowed settings)
        }

        // 3. Check request parameter (lowest priority)
        // At this point: tenant allows journey, and client either allows or inherits
        // Request can choose between journey and standalone
        var uiModeParam = context.Request?.UiMode;
        _logger.LogDebug("ResolveUiMode: Request ui_mode={UiMode}", uiModeParam ?? "(null)");

        if (!string.IsNullOrEmpty(uiModeParam))
        {
            if (Enum.TryParse<UiMode>(uiModeParam, ignoreCase: true, out var requestedMode))
            {
                _logger.LogDebug("ResolveUiMode: Using request ui_mode, returning {Mode}", requestedMode);
                return requestedMode;
            }
        }

        // 4. Default: use journey (tenant allows it, no explicit preference to use standalone)
        _logger.LogDebug("ResolveUiMode: Defaulting to Journey");
        return UiMode.Journey;
    }

    private async Task<IActionResult> StartJourneyAuthAsync(
        ProtocolContext context,
        AuthenticationRequirement requirement,
        string correlationId,
        string? sessionUserId,
        DateTimeOffset? sessionAuthTime,
        CancellationToken cancellationToken)
    {
        // A policy the client names (by slug, ?policy=) must be the one that runs: never the
        // default in its place.
        var requestedPolicyId = !string.IsNullOrEmpty(requirement.ExplicitPolicyId)
            ? requirement.ExplicitPolicyId
            : context.PolicyId;
        if (!string.IsNullOrEmpty(requestedPolicyId))
        {
            var requested = await _policyStore.GetBySlugAsync(requestedPolicyId, cancellationToken);
            if (requested == null || !requested.Enabled)
            {
                _logger.LogWarning("Requested policy {PolicyId} is {Reason}", requestedPolicyId, requested == null ? "not found" : "disabled");
                return new RedirectResult(ErrorUrl("invalid_request", "The requested sign-in journey is not available.", context));
            }
        }

        // Resolve policy
        var policy = await ResolvePolicyAsync(context, requirement, cancellationToken);
        if (policy == null)
        {
            // No policy was asked for (a named one never falls back; see above) and the tenant
            // has none for this journey: the standard pages, if the tenant allows it.
            var fallback = context.Properties.TryGetValue("TenantJourneyFallbackToStandard", out var allowed) && allowed is true;
            if (fallback && string.IsNullOrEmpty(requestedPolicyId))
            {
                _logger.LogInformation(
                    "No enabled {PolicyType} journey policy; using the standard pages", requirement.SuggestedPolicyType);
                return StartStandaloneAuth(context, requirement, correlationId);
            }

            _logger.LogWarning("No enabled {PolicyType} journey policy and fallback to the standard pages is off", requirement.SuggestedPolicyType);
            return new RedirectResult(ErrorUrl("server_error", NoJourneyMessage, context));
        }

        // Build callback URL
        var callbackUrl = BuildCallbackUrl(context, correlationId);

        // Start journey
        var journeyState = await _journeyOrchestrator.StartJourneyAsync(
            policy,
            new JourneyStartContext
            {
                HttpContext = context.HttpContext,
                ProtocolName = context.ProtocolName,
                CorrelationId = correlationId,
                CallbackUrl = callbackUrl,
                UserId = sessionUserId,
                SessionAuthTime = sessionAuthTime,
                LoginHint = requirement.LoginHint,
                AcrValues = requirement.AcrValues,
                RequestedScopes = requirement.RequestedScopes.ToList(),
                Properties = context.Properties
            },
            cancellationToken);

        context.JourneyId = journeyState.Id;

        _logger.LogInformation(
            "Started journey {JourneyId} for protocol {Protocol}, policy {PolicyId}",
            journeyState.Id, context.ProtocolName, policy.Id);

        // Redirect to journey page
        return new RedirectResult($"/journey/{journeyState.Id}");
    }

    private IActionResult StartStandaloneAuth(
        ProtocolContext context,
        AuthenticationRequirement requirement,
        string correlationId)
    {
        var callbackUrl = BuildCallbackUrl(context, correlationId);

        // Determine which standalone page to redirect to
        var page = requirement.SuggestedPolicyType switch
        {
            JourneyType.SignUp => "/account/register",
            JourneyType.PasswordReset => "/account/forgot-password",
            JourneyType.ProfileEdit => "/account/profile",
            _ => "/account/login"
        };

        var redirectUrl = $"{page}?returnUrl={Uri.EscapeDataString(callbackUrl)}";

        if (!string.IsNullOrEmpty(requirement.LoginHint))
        {
            redirectUrl += $"&loginHint={Uri.EscapeDataString(requirement.LoginHint)}";
        }

        if (context.Properties.TryGetValue("DomainHint", out var domainHintObj)
            && domainHintObj is string domainHint
            && !string.IsNullOrEmpty(domainHint))
        {
            redirectUrl += $"&domain_hint={Uri.EscapeDataString(domainHint)}";
        }

        if (context.Properties.TryGetValue("UiLocales", out var uiLocalesObj)
            && uiLocalesObj is string uiLocales
            && !string.IsNullOrEmpty(uiLocales))
        {
            redirectUrl += $"&ui_locales={Uri.EscapeDataString(uiLocales)}";
        }

        _logger.LogDebug("Redirecting to standalone page {Page}", page);

        return new RedirectResult(redirectUrl);
    }

    private static IActionResult BuildHeadlessAuthResponse(
        ProtocolContext context,
        AuthenticationRequirement requirement)
    {
        // Return 401 with authentication requirements for SPA/mobile clients
        return new UnauthorizedObjectResult(new
        {
            error = "login_required",
            error_description = "Authentication required",
            auth_requirements = new
            {
                policy_type = requirement.SuggestedPolicyType.ToString().ToLowerInvariant(),
                policy_id = requirement.ExplicitPolicyId,
                mfa_required = requirement.ForceMfa,
                scopes = requirement.RequestedScopes
            }
        });
    }

    /// <summary>Shown when journey sign-in has no policy to run and the tenant doesn't fall back.</summary>
    public const string NoJourneyMessage = "Sign-in isn't set up here yet. Please contact your administrator.";

    /// <summary>The error page, with the way back to the requesting app's homepage.</summary>
    private static string ErrorUrl(string error, string message, ProtocolContext context)
    {
        var url = $"/error?error={Uri.EscapeDataString(error)}&message={Uri.EscapeDataString(message)}";
        var clientId = context.Request?.ClientId;
        return string.IsNullOrEmpty(clientId) ? url : $"{url}&client_id={Uri.EscapeDataString(clientId)}";
    }

    private async Task<JourneyPolicy?> ResolvePolicyAsync(
        ProtocolContext context,
        AuthenticationRequirement requirement,
        CancellationToken cancellationToken)
    {
        // 1. Check explicit policy ID
        if (!string.IsNullOrEmpty(requirement.ExplicitPolicyId))
        {
            var explicitPolicy = await _policyStore.GetBySlugAsync(requirement.ExplicitPolicyId, cancellationToken);
            if (explicitPolicy is { Enabled: true })
            {
                _logger.LogDebug("Using explicit policy {PolicyId}", requirement.ExplicitPolicyId);
                return explicitPolicy;
            }
        }

        // 2. Check context policy ID (from request)
        if (!string.IsNullOrEmpty(context.PolicyId))
        {
            var contextPolicy = await _policyStore.GetBySlugAsync(context.PolicyId, cancellationToken);
            if (contextPolicy is { Enabled: true })
            {
                _logger.LogDebug("Using context policy {PolicyId}", context.PolicyId);
                return contextPolicy;
            }
        }

        // 3. Get by policy type
        var policy = await _policyStore.GetByTypeAsync(requirement.SuggestedPolicyType, cancellationToken);
        if (policy != null)
        {
            _logger.LogDebug("Using policy by type {PolicyType}: {PolicyId}", requirement.SuggestedPolicyType, policy.Id);
            return policy;
        }

        // 4. Fall back to SignIn policy
        if (requirement.SuggestedPolicyType != JourneyType.SignIn)
        {
            policy = await _policyStore.GetByTypeAsync(JourneyType.SignIn, cancellationToken);
            if (policy != null)
            {
                _logger.LogDebug("Falling back to SignIn policy: {PolicyId}", policy.Id);
                return policy;
            }
        }

        return null;
    }

    private async Task<string> StoreProtocolStateAsync(
        ProtocolContext context,
        CancellationToken cancellationToken)
    {
        var state = new ProtocolState
        {
            ProtocolName = context.ProtocolName,
            SerializedRequest = SerializeRequest(context.Request),
            ClientId = context.Request?.ClientId ?? "",
            TenantId = context.Properties.TryGetValue("TenantId", out var tid) ? tid as string : null,
            EndpointType = context.EndpointType,
            Properties = context.Properties
                .Where(kv => kv.Value is string)
                .ToDictionary(kv => kv.Key, kv => (string)kv.Value)
        };

        return await _protocolStateStore.StoreAsync(state, cancellationToken: cancellationToken);
    }

    /// <summary>
    /// Completes a request whose sign-in ran as a hosted journey. The journey issued the
    /// session cookie, so the user comes from that cookie exactly as on the standalone path;
    /// the journey must belong to this request and this user, and its output claims (and any
    /// narrower consent) are added on top.
    /// </summary>
    private async Task<AuthenticationResult> HandleJourneyCallbackAsync(
        ProtocolState state,
        string correlationId,
        string journeyId,
        CancellationToken cancellationToken)
    {
        var journeyState = await _journeyStateStore.GetAsync(journeyId, cancellationToken);
        if (journeyState == null)
        {
            _logger.LogWarning("Journey {JourneyId} not found", journeyId);
            return AuthenticationResult.Failed("invalid_request", "Journey not found or expired");
        }

        if (journeyState.Status != JourneyStatus.Completed)
        {
            _logger.LogWarning("Journey {JourneyId} not completed, status: {Status}", journeyId, journeyState.Status);
            return AuthenticationResult.Failed("access_denied", "Authentication was not completed");
        }

        if (!string.Equals(journeyState.CorrelationId, correlationId, StringComparison.Ordinal))
        {
            _logger.LogWarning("Journey {JourneyId} does not belong to request {CorrelationId}", journeyId, correlationId);
            return AuthenticationResult.Failed("invalid_request", "Journey does not match this sign-in request");
        }

        var result = await HandleStandaloneCallbackAsync(state, cancellationToken);
        if (!result.Succeeded) return result;

        var journeyUser = journeyState.UserId ?? journeyState.AuthenticatedUserId;
        if (!string.Equals(journeyUser, result.UserId, StringComparison.Ordinal))
        {
            _logger.LogWarning("Journey {JourneyId} was completed by {JourneyUser}, not the signed-in user {UserId}",
                journeyId, journeyUser, result.UserId);
            return AuthenticationResult.Failed("access_denied", "Journey does not match the signed-in user");
        }

        foreach (var (name, value) in ReadOutputClaims(journeyState))
        {
            if (name is "sub") continue;
            result.Claims[name] = value;
        }

        var scopes = result.Scopes;
        if (journeyState.Data?.TryGetValue(ConsentDecisions.ConsentedScopesDataKey, out var consented) == true
            && consented?.ToString() is { Length: > 0 } granted)
        {
            var allowed = granted.Split(' ', StringSplitOptions.RemoveEmptyEntries);
            scopes = scopes.Where(scope => scope == "openid" || allowed.Contains(scope, StringComparer.Ordinal)).ToList();
        }

        _logger.LogInformation("Journey {JourneyId} completed for user {UserId}", journeyId, result.UserId);

        return AuthenticationResult.Success(
            userId: result.UserId!,
            sessionId: result.SessionId ?? journeyState.SessionId ?? journeyId,
            scopes: scopes.ToList(),
            claims: result.Claims,
            amr: result.Amr,
            authTime: result.AuthTime,
            acr: result.Acr);
    }

    private static IEnumerable<KeyValuePair<string, object>> ReadOutputClaims(JourneyState journeyState)
    {
        if (journeyState.Data?.TryGetValue(DefaultJourneyOrchestrator.OutputClaimsDataKey, out var raw) != true
            || raw?.ToString() is not { Length: > 0 } json)
        {
            yield break;
        }

        using var document = System.Text.Json.JsonDocument.Parse(json);
        foreach (var property in document.RootElement.EnumerateObject())
        {
            object? value = property.Value.ValueKind switch
            {
                System.Text.Json.JsonValueKind.String => property.Value.GetString(),
                System.Text.Json.JsonValueKind.True => true,
                System.Text.Json.JsonValueKind.False => false,
                System.Text.Json.JsonValueKind.Number => property.Value.GetRawText(),
                System.Text.Json.JsonValueKind.Array => property.Value.EnumerateArray()
                    .Select(JsonLookup.AsString).Where(v => v != null).Select(v => v!).ToArray(),
                _ => null,
            };
            if (value != null) yield return new(property.Name, value);
        }
    }

    private async Task<(string? SessionId, bool Persistent)> CurrentCookieSessionAsync()
    {
        var httpContext = _httpContextAccessor.HttpContext;
        if (httpContext == null) return (null, false);

        var auth = await httpContext.AuthenticateAsync(IdentityConstants.ApplicationScheme);
        if (!auth.Succeeded) return (null, false);
        return (auth.Principal?.FindFirst("sid")?.Value, auth.Properties?.IsPersistent == true);
    }

    private string BuildCallbackUrl(ProtocolContext context, string correlationId)
    {
        var request = context.HttpContext.Request;
        var baseUrl = $"{request.Scheme}://{request.Host}";

        // Protocol-specific callback path based on configured endpoints
        var callbackPath = context.ProtocolName switch
        {
            "oidc" => $"{_oidcConfig.AuthorizeEndpoint.TrimEnd('/')}/callback",
            "saml" => GetSamlCallbackPath(context),
            _ => $"/{context.ProtocolName}/callback"
        };

        return $"{baseUrl}{callbackPath}?correlation_id={Uri.EscapeDataString(correlationId)}";
    }

    private static string GetSamlCallbackPath(ProtocolContext context)
    {
        // Check if SAML config is available in context properties
        if (context.Properties.TryGetValue("SamlSsoEndpoint", out var ssoEndpoint) && ssoEndpoint is string ssoPath)
        {
            return $"{ssoPath.TrimEnd('/')}/callback";
        }

        // Default SAML callback path
        return "/saml/sso/callback";
    }

    private static string SerializeRequest(IProtocolRequest? request)
    {
        if (request == null)
            return "{}";

        return System.Text.Json.JsonSerializer.Serialize(request, request.GetType());
    }
}
