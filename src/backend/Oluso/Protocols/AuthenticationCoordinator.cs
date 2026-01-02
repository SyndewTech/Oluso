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

    public AuthenticationCoordinator(
        IJourneyOrchestrator journeyOrchestrator,
        IJourneyPolicyStore policyStore,
        IJourneyStateStore journeyStateStore,
        IProtocolStateStore protocolStateStore,
        IHttpContextAccessor httpContextAccessor,
        IOlusoUserService userService,
        IOptions<OidcEndpointConfiguration> oidcConfig,
        ILogger<AuthenticationCoordinator> logger)
    {
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
        if (skipLogin.ShouldSkip)
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

        // Resolve UI mode
        var uiMode = ResolveUiMode(context);
        context.UiMode = uiMode;

        // Store protocol state for callback
        var storedCorrelationId = await StoreProtocolStateAsync(context, cancellationToken);
        context.CorrelationId = storedCorrelationId;

        _logger.LogDebug(
            "Starting authentication for protocol {Protocol}, UI mode {UiMode}, correlation {CorrelationId}",
            context.ProtocolName, uiMode, storedCorrelationId);

        return uiMode switch
        {
            UiMode.Journey => await StartJourneyAuthAsync(context, requirement, storedCorrelationId, cancellationToken),
            UiMode.Standalone => StartStandaloneAuth(context, requirement, storedCorrelationId),
            UiMode.Headless => BuildHeadlessAuthResponse(context, requirement),
            _ => throw new InvalidOperationException($"Unknown UI mode: {uiMode}")
        };
    }

    private async Task<(bool ShouldSkip, string? UserId)> ShouldSkipLoginAsync(
        AuthenticationRequirement requirement,
        CancellationToken cancellationToken)
    {
        var httpContext = _httpContextAccessor.HttpContext;
        if (httpContext == null)
        {
            return (false, null);
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
            return (false, null);
        }

        // No existing authentication
        if (!isAuthenticated)
        {
            _logger.LogDebug("No existing authentication found");
            return (false, null);
        }

        // Get user ID from the authenticated principal
        var userId = authResult.Principal!.FindFirst(System.Security.Claims.ClaimTypes.NameIdentifier)?.Value
            ?? authResult.Principal.FindFirst("sub")?.Value;

        if (string.IsNullOrEmpty(userId))
        {
            _logger.LogDebug("Authenticated principal has no user ID claim");
            return (false, null);
        }

        // Verify user still exists
        var user = await _userService.FindByIdAsync(userId, cancellationToken);
        if (user == null)
        {
            _logger.LogWarning("Authenticated user {UserId} no longer exists in store, signing out", userId);
            await httpContext.SignOutAsync(IdentityConstants.ApplicationScheme);
            return (false, null);
        }

        _logger.LogDebug("User {UserId} is already authenticated, can skip login", userId);
        return (true, userId);
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

        // If journey ID provided, get journey result
        if (!string.IsNullOrEmpty(journeyId))
        {
            return await HandleJourneyCallbackAsync(journeyId, cancellationToken);
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

        // Build claims from user
        var claims = new Dictionary<string, object>
        {
            ["sub"] = userId,
            ["name"] = userInfo.DisplayName ?? userInfo.Username ?? "",
            ["email"] = userInfo.Email ?? "",
            ["email_verified"] = userInfo.EmailVerified
        };

        if (!string.IsNullOrEmpty(userInfo.FirstName))
            claims["given_name"] = userInfo.FirstName;
        if (!string.IsNullOrEmpty(userInfo.LastName))
            claims["family_name"] = userInfo.LastName;

        // Session ID - generate new for standalone flow
        var sessionId = Guid.NewGuid().ToString("N");

        return AuthenticationResult.Success(
            userId: userId,
            sessionId: sessionId,
            scopes: new List<string> { "openid", "profile", "email" },
            claims: claims);
    }

    public async Task<IActionResult> HandleConsentRequiredAsync(
        ProtocolContext context,
        ConsentRequirement requirement,
        CancellationToken cancellationToken = default)
    {
        // Resolve UI mode to determine how to handle consent
        var uiMode = ResolveUiMode(context);

        // Add consent info to context properties before storing
        context.Properties["ClientName"] = requirement.ClientName;
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
        var authorizeUrl = context.HttpContext.Request.Path + context.HttpContext.Request.QueryString;
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
                Properties = new Dictionary<string, object>(context.Properties)
                {
                    ["client_id"] = requirement.ClientId,
                    ["scope"] = string.Join(" ", requirement.RequestedScopes)
                }
            },
            cancellationToken);

        context.JourneyId = journeyState.Id;

        _logger.LogInformation(
            "Started consent journey {JourneyId} for client {ClientId}, user {UserId}, callback: {CallbackUrl}",
            journeyState.Id, requirement.ClientId, requirement.SubjectId, callbackUrl);

        return new RedirectResult($"/journey/{journeyState.Id}");
    }

    private IActionResult StartStandaloneConsent(
        ProtocolContext context,
        ConsentRequirement requirement)
    {
        // Build return URL - after consent, redirect back to authorize
        var returnUrl = context.HttpContext.Request.Path + context.HttpContext.Request.QueryString;

        var consentUrl = $"/account/consent?correlationId={Uri.EscapeDataString(context.CorrelationId)}";
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
        if (context.Properties.TryGetValue("TenantUseJourneyFlow", out var tenantSetting) && tenantSetting is bool tenantValue)
        {
            tenantUseJourney = tenantValue;
        }

        // If tenant disables journey flow, that's final - no client or request can override
        if (!tenantUseJourney)
        {
            return UiMode.Standalone;
        }

        // 2. Tenant allows journey - check if client has an explicit preference
        // Client.UseJourneyFlow is nullable: null = inherit tenant, true/false = explicit choice
        if (context.Properties.TryGetValue("ClientUseJourneyFlow", out var clientSetting) && clientSetting is bool clientValue)
        {
            // Client explicitly disables journey - that's final, request cannot override
            if (!clientValue)
            {
                return UiMode.Standalone;
            }
            // Client explicitly wants journey - continue to check request parameter
            // (request can still choose standalone within client's allowed settings)
        }

        // 3. Check request parameter (lowest priority)
        // At this point: tenant allows journey, and client either allows or inherits
        // Request can choose between journey and standalone
        var uiModeParam = context.Request?.UiMode;
        if (!string.IsNullOrEmpty(uiModeParam))
        {
            if (Enum.TryParse<UiMode>(uiModeParam, ignoreCase: true, out var requestedMode))
            {
                return requestedMode;
            }
        }

        // 4. Default: use journey (tenant allows it, no explicit preference to use standalone)
        return UiMode.Journey;
    }

    private async Task<IActionResult> StartJourneyAuthAsync(
        ProtocolContext context,
        AuthenticationRequirement requirement,
        string correlationId,
        CancellationToken cancellationToken)
    {
        // Resolve policy
        var policy = await ResolvePolicyAsync(context, requirement, cancellationToken);
        if (policy == null)
        {
            _logger.LogError("No journey policy found for request");
            return new BadRequestObjectResult(new { error = "server_error", error_description = "No authentication policy configured" });
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

    private async Task<JourneyPolicy?> ResolvePolicyAsync(
        ProtocolContext context,
        AuthenticationRequirement requirement,
        CancellationToken cancellationToken)
    {
        // 1. Check explicit policy ID
        if (!string.IsNullOrEmpty(requirement.ExplicitPolicyId))
        {
            var explicitPolicy = await _policyStore.GetAsync(requirement.ExplicitPolicyId, cancellationToken);
            if (explicitPolicy != null)
            {
                _logger.LogDebug("Using explicit policy {PolicyId}", requirement.ExplicitPolicyId);
                return explicitPolicy;
            }
            _logger.LogWarning("Explicit policy {PolicyId} not found", requirement.ExplicitPolicyId);
        }

        // 2. Check context policy ID (from request)
        if (!string.IsNullOrEmpty(context.PolicyId))
        {
            var contextPolicy = await _policyStore.GetAsync(context.PolicyId, cancellationToken);
            if (contextPolicy != null)
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

    private async Task<AuthenticationResult> HandleJourneyCallbackAsync(
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

        if (string.IsNullOrEmpty(journeyState.AuthenticatedUserId))
        {
            _logger.LogWarning("Journey {JourneyId} completed but no user ID", journeyId);
            return AuthenticationResult.Failed("access_denied", "User authentication failed");
        }

        // Build result from journey state
        var scopes = journeyState.ClaimsBag.Get("consented_scopes")?.Split(' ', StringSplitOptions.RemoveEmptyEntries)
            ?? Array.Empty<string>();

        var claims = new Dictionary<string, object>();
        foreach (var claim in journeyState.ClaimsBag.GetAll())
        {
            claims[claim.Key] = claim.Value;
        }

        _logger.LogInformation("Journey {JourneyId} completed successfully for user {UserId}", journeyId, journeyState.AuthenticatedUserId);

        return AuthenticationResult.Success(
            userId: journeyState.AuthenticatedUserId,
            sessionId: journeyState.SessionId,
            scopes: scopes.ToList(),
            claims: claims);
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
