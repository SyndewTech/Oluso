using System.Net;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Oluso.Core.Domain.Entities;
using Oluso.Core.Domain.Interfaces;
using Oluso.Core.Protocols;
using Oluso.Core.Protocols.Grants;
using Oluso.Core.Protocols.Models;
using Oluso.Core.Protocols.Validation;
using Oluso.Core.Services;
using Oluso.Middleware;
using Oluso.Protocols.Services;

namespace Oluso.Protocols.Oidc;

/// <summary>
/// OIDC protocol service implementation that integrates with IdentityServer core services
/// </summary>
public class OidcProtocolService : IOidcProtocolService
{
    private readonly OidcEndpointConfiguration _config;
    private readonly IProtocolStateStore _stateStore;
    private readonly IAuthorizeRequestValidator _authorizeValidator;
    private readonly IAuthorizationCodeStore _authorizationCodeStore;
    private readonly IConsentStore _consentStore;
    private readonly ITokenRequestValidator _tokenValidator;
    private readonly IGrantHandlerRegistry _grantRegistry;
    private readonly ITokenService _tokenService;
    private readonly IProfileService _profileService;
    private readonly IPushedAuthorizationStore _parStore;
    private readonly ITenantContext _tenantContext;
    private readonly IIssuerResolver _issuerResolver;
    private readonly IAudienceResolver _audiences;
    private readonly IClientUsageRecorder _usage;
    private readonly ILogger<OidcProtocolService> _logger;

    public string ProtocolName => "oidc";

    /// <summary>The error description when the signed-in user has no access to the routed tenant.</summary>
    public const string NoTenantAccessDescription = "Your account has no access to this organization";

    private readonly IConsentDecisions? _consentDecisions;
    private readonly ITokenTenantResolver? _tokenTenants;
    private readonly IClientUserAccess _clientUserAccess;

    public OidcProtocolService(
        IOptions<OidcEndpointConfiguration> config,
        IProtocolStateStore stateStore,
        IAuthorizeRequestValidator authorizeValidator,
        IAuthorizationCodeStore authorizationCodeStore,
        IConsentStore consentStore,
        ITokenRequestValidator tokenValidator,
        IGrantHandlerRegistry grantRegistry,
        ITokenService tokenService,
        IProfileService profileService,
        IPushedAuthorizationStore parStore,
        ITenantContext tenantContext,
        IIssuerResolver issuerResolver,
        IAudienceResolver audiences,
        IClientUsageRecorder usage,
        IClientUserAccess clientUserAccess,
        ILogger<OidcProtocolService> logger,
        IConsentDecisions? consentDecisions = null,
        ITokenTenantResolver? tokenTenants = null)
    {
        _consentDecisions = consentDecisions;
        _tokenTenants = tokenTenants;
        _clientUserAccess = clientUserAccess;
        _usage = usage;
        _audiences = audiences;
        _config = config.Value;
        _stateStore = stateStore;
        _authorizeValidator = authorizeValidator;
        _authorizationCodeStore = authorizationCodeStore;
        _consentStore = consentStore;
        _tokenValidator = tokenValidator;
        _grantRegistry = grantRegistry;
        _tokenService = tokenService;
        _profileService = profileService;
        _parStore = parStore;
        _tenantContext = tenantContext;
        _issuerResolver = issuerResolver;
        _logger = logger;
    }

    public async Task<ProtocolContext> BuildContextAsync(HttpContext http, CancellationToken cancellationToken = default)
    {
        var request = await ParseAuthorizeRequestAsync(http);

        var context = new ProtocolContext
        {
            HttpContext = http,
            ProtocolName = ProtocolName,
            EndpointType = EndpointType.Authorize,
            Request = request,
            PolicyId = request.PolicyId
        };

        return context;
    }

    public async Task<ProtocolRequestResult> ProcessAuthorizeAsync(ProtocolContext context, CancellationToken cancellationToken = default)
    {
        if (context.Request is not OidcAuthorizeRequest oidcRequest)
        {
            return ProtocolRequestResult.Failed("invalid_request", "Invalid request");
        }

        // Convert to IdentityServer AuthorizeRequest for validation
        var authorizeRequest = ConvertToAuthorizeRequest(oidcRequest);

        // Validate request using existing validator
        var validationResult = await _authorizeValidator.ValidateAsync(authorizeRequest, cancellationToken);
        if (!validationResult.IsValid)
        {
            _logger.LogWarning("Authorize request validation failed: {Error} - {Description}",
                validationResult.Error, validationResult.ErrorDescription);
            return ProtocolRequestResult.Failed(
                validationResult.Error!,
                validationResult.ErrorDescription,
                validationResult.RedirectUriValidated,
                validationResult.ValidatedRedirectUri);
        }

        // Store validated client in context for later use
        var client = validationResult.Client!;
        context.Properties["ValidatedClient"] = client;
        context.Properties["ValidScopes"] = validationResult.ValidScopes;
        // String form survives ProtocolState serialization (the List<string> above is
        // filtered out, as only string-valued properties are persisted). The standalone
        // login callback reads this to grant the validated scopes — including
        // offline_access — instead of a hardcoded set, so refresh tokens are issued.
        context.Properties["GrantedScopes"] = string.Join(" ", validationResult.ValidScopes);
        context.Properties["ClientId"] = client.ClientId;

        // Pass client settings that affect authentication flow
        context.Properties["EnableLocalLogin"] = client.EnableLocalLogin;
        context.Properties["IdentityProviderRestrictions"] = client.IdentityProviderRestrictions
            .Select(r => r.Provider).ToList();

        // Set journey flow settings from client and tenant
        // Client setting overrides tenant setting if explicitly set
        if (client.UseJourneyFlow.HasValue)
        {
            context.Properties["ClientUseJourneyFlow"] = client.UseJourneyFlow.Value;
        }

        // Set tenant journey flow setting
        if (_tenantContext.HasTenant && _tenantContext.Tenant != null)
        {
            context.Properties["TenantId"] = _tenantContext.Tenant.Id;
            context.Properties["TenantUseJourneyFlow"] = _tenantContext.Tenant.UseJourneyFlow;
            context.Properties["TenantJourneyFallbackToStandard"] = _tenantContext.Tenant.JourneyFallbackToStandard;
        }

        // Pass domain_hint if present in the request
        if (!string.IsNullOrEmpty(oidcRequest.DomainHint))
        {
            context.Properties["DomainHint"] = oidcRequest.DomainHint;
        }

        // Pass ui_locales if present (OIDC §3.1.2.1 — space-separated locale tags)
        if (!string.IsNullOrEmpty(oidcRequest.UiLocales))
        {
            context.Properties["UiLocales"] = oidcRequest.UiLocales;
        }

        // prompt=none (OIDC Core §3.1.2.1): never show UI; an unmet requirement goes back to the
        // client as login_required / consent_required on its redirect URI. It cannot be combined.
        var promptValues = oidcRequest.Prompt?.Split(' ', StringSplitOptions.RemoveEmptyEntries) ?? Array.Empty<string>();
        var promptNone = promptValues.Contains(PromptModes.None);
        if (promptNone && promptValues.Length > 1)
        {
            return ProtocolRequestResult.Failed(OidcConstants.Errors.InvalidRequest, "prompt=none cannot be combined with other prompt values");
        }

        // dpop_jkt (RFC 9449 §10): the code is bound to this DPoP key; the token request must prove it.
        if (oidcRequest.Raw != null && oidcRequest.Raw.TryGetValue(DPoPJktParameter, out var dpopJkt)
            && !string.IsNullOrEmpty(dpopJkt) && !IsJwkThumbprint(dpopJkt))
        {
            return ProtocolRequestResult.Failed(OidcConstants.Errors.InvalidRequest, "dpop_jkt must be a base64url SHA-256 JWK thumbprint");
        }

        // Check if user is authenticated
        var user = context.HttpContext.User;
        if (user.Identity?.IsAuthenticated != true)
        {
            if (promptNone)
            {
                return ProtocolRequestResult.Failed(OidcConstants.Errors.LoginRequired, "The user is not signed in");
            }

            // Need authentication
            return ProtocolRequestResult.RequiresAuth(new AuthenticationRequirement
            {
                SuggestedPolicyType = DeterminePolicyType(oidcRequest),
                ExplicitPolicyId = oidcRequest.PolicyId,
                LoginHint = oidcRequest.LoginHint,
                AcrValues = oidcRequest.AcrValues,
                RequestedScopes = validationResult.ValidScopes.ToList(),
                MaxAge = !string.IsNullOrEmpty(oidcRequest.MaxAge) && int.TryParse(oidcRequest.MaxAge, out var maxAge) ? maxAge : null,
                Prompt = oidcRequest.Prompt
            });
        }

        // User is authenticated - check prompt=login
        if (oidcRequest.Prompt?.Contains("login") == true)
        {
            return ProtocolRequestResult.RequiresAuth(new AuthenticationRequirement
            {
                SuggestedPolicyType = Core.UserJourneys.JourneyType.SignIn,
                ForceFreshLogin = true,
                LoginHint = oidcRequest.LoginHint,
                RequestedScopes = validationResult.ValidScopes.ToList()
            });
        }

        // Check max_age
        if (!string.IsNullOrEmpty(oidcRequest.MaxAge) && int.TryParse(oidcRequest.MaxAge, out var maxAgeSeconds))
        {
            var authTimeClaim = user.FindFirst("auth_time");
            if (authTimeClaim != null && long.TryParse(authTimeClaim.Value, out var authTime))
            {
                var authDateTime = DateTimeOffset.FromUnixTimeSeconds(authTime).UtcDateTime;
                if (DateTime.UtcNow.Subtract(authDateTime).TotalSeconds > maxAgeSeconds)
                {
                    if (promptNone)
                    {
                        return ProtocolRequestResult.Failed(OidcConstants.Errors.LoginRequired, "The sign-in is older than max_age");
                    }
                    return ProtocolRequestResult.RequiresAuth(new AuthenticationRequirement
                    {
                        SuggestedPolicyType = Core.UserJourneys.JourneyType.SignIn,
                        ForceFreshLogin = true,
                        LoginHint = oidcRequest.LoginHint,
                        RequestedScopes = validationResult.ValidScopes.ToList()
                    });
                }
            }
        }

        // Check consent
        var subjectId = user.FindFirst("sub")?.Value ?? user.FindFirst(System.Security.Claims.ClaimTypes.NameIdentifier)?.Value;
        if (string.IsNullOrEmpty(subjectId))
        {
            return ProtocolRequestResult.Failed("server_error", "Unable to determine user identity");
        }

        // The user just answered a consent screen for this request: that answer stands,
        // including for one-time consent and prompt=consent, and limits what is granted.
        var decided = _consentDecisions == null
            ? null
            : await _consentDecisions.TakeAsync(oidcRequest.Raw, subjectId, client.ClientId, cancellationToken);
        if (decided != null)
        {
            var granted = validationResult.ValidScopes
                .Where(scope => scope == "openid" || decided.Contains(scope, StringComparer.Ordinal))
                .ToList();
            context.Properties["ValidScopes"] = granted;
            context.Properties["GrantedScopes"] = string.Join(" ", granted);
        }

        var needsConsent = decided == null && await CheckConsentRequiredAsync(
            subjectId,
            client,
            validationResult.ValidScopes,
            oidcRequest.Prompt,
            cancellationToken);

        if (needsConsent)
        {
            if (promptNone)
            {
                return ProtocolRequestResult.Failed(OidcConstants.Errors.ConsentRequired, "The user has not consented to the requested scopes");
            }

            // Return consent required - the journey/UI will handle the consent step
            return ProtocolRequestResult.RequiresConsent(new ConsentRequirement
            {
                ClientId = client.ClientId,
                ClientName = client.ClientName ?? client.ClientId,
                RequestedScopes = validationResult.ValidScopes.ToList(),
                SubjectId = subjectId
            });
        }

        // A requested policy still runs its steps for the signed-in user; the session meets
        // its login steps when it is the user the hints name. prompt=none must not show UI,
        // so it keeps the silent response.
        if (!string.IsNullOrEmpty(oidcRequest.PolicyId) && oidcRequest.Prompt?.Contains(PromptModes.None) != true)
        {
            return ProtocolRequestResult.RequiresAuth(new AuthenticationRequirement
            {
                SuggestedPolicyType = DeterminePolicyType(oidcRequest),
                ExplicitPolicyId = oidcRequest.PolicyId,
                LoginHint = oidcRequest.LoginHint,
                IdTokenHint = oidcRequest.IdTokenHint,
                AcrValues = oidcRequest.AcrValues,
                RequestedScopes = validationResult.ValidScopes.ToList()
            });
        }

        // User authenticated and consented - ready to issue code
        // This will be completed by BuildAuthenticatedResponseAsync
        return ProtocolRequestResult.Success();
    }

    public async Task<ProtocolContext> RestoreContextAsync(string correlationId, CancellationToken cancellationToken = default)
    {
        var state = await _stateStore.GetAsync(correlationId, cancellationToken);
        if (state == null)
        {
            _logger.LogError("Protocol state not found for correlation ID {CorrelationId}", correlationId);
            throw new InvalidOperationException($"Protocol state not found for correlation {correlationId}");
        }

        _logger.LogDebug("Restored protocol state for correlation ID {CorrelationId}", correlationId);
        var request = JsonSerializer.Deserialize<OidcAuthorizeRequest>(state.SerializedRequest);

        return new ProtocolContext
        {
            ProtocolName = ProtocolName,
            EndpointType = state.EndpointType,
            Request = request,
            CorrelationId = correlationId,
            Properties = state.Properties.ToDictionary(kv => kv.Key, kv => (object)kv.Value)
        };
    }

    public async Task<IActionResult> BuildAuthenticatedResponseAsync(
        ProtocolContext context,
        AuthenticationResult authResult,
        CancellationToken cancellationToken = default)
    {
        _logger.LogInformation("Building authenticated response for protocol {Protocol} and correlation ID {CorrelationId}",
            context.ProtocolName, context.CorrelationId);
        var request = context.Request as OidcAuthorizeRequest;
        if (request == null)
        {
            _logger.LogError("Invalid protocol context request type");
            return BuildErrorResponse(context, new ProtocolError { Code = "server_error", Description = "Invalid request state" });
        }

        if (string.IsNullOrEmpty(request.RedirectUri))
        {
            return BuildErrorResponse(context, new ProtocolError { Code = "invalid_request", Description = "Missing redirect_uri" });
        }

        // A tenant's host issues only its own tokens: a user signed in (say, by a session from another tenant) who
        // has no access to the routed tenant gets access_denied here rather than a code the token endpoint can't
        // honour.
        if (_tokenTenants != null && !string.IsNullOrEmpty(authResult.UserId)
            && !await _tokenTenants.CanIssueOnRoutedTenantAsync(authResult.UserId, cancellationToken))
        {
            _logger.LogWarning("User {UserId} has no access to tenant {TenantId}; no code for client {ClientId}",
                authResult.UserId, _tenantContext.TenantId, request.ClientId);
            return BuildErrorResponse(context, new ProtocolError
            {
                Code = OidcConstants.Errors.AccessDenied,
                Description = NoTenantAccessDescription,
                // The request's redirect URI was validated at /authorize (it is where the code would have gone).
                RedirectUriValidated = true,
                ValidatedRedirectUri = request.RedirectUri
            });
        }

        // The client's user restrictions, before a code or token leaves: against the tenant the code is stored for
        // (its tenant_id claim), which is the tenant the token endpoint checks again.
        if (!string.IsNullOrEmpty(authResult.UserId)
            && context.Properties.TryGetValue("ValidatedClient", out var restrictedObj) && restrictedObj is Client restrictedClient)
        {
            var codeTenant = authResult.Claims.TryGetValue(OidcConstants.StandardClaims.TenantId, out var tenantClaim)
                ? tenantClaim?.ToString()
                : null;
            var denial = await _clientUserAccess.FindDenialAsync(
                restrictedClient.ClientId, ClientUserRestrictions.From(restrictedClient), authResult.UserId, codeTenant, cancellationToken);
            if (denial != null)
            {
                return BuildErrorResponse(context, new ProtocolError
                {
                    Code = OidcConstants.Errors.AccessDenied,
                    Description = denial,
                    RedirectUriValidated = true,
                    ValidatedRedirectUri = request.RedirectUri
                });
            }
        }

        // Determine what the response type combination requires
        var responseTypes = request.RequestedResponseTypes;
        var wantsCode = responseTypes.Contains(ResponseTypes.Code);
        var wantsToken = responseTypes.Contains(ResponseTypes.Token);
        var wantsIdToken = responseTypes.Contains(ResponseTypes.IdToken);
        var isImplicitOrHybrid = wantsToken || wantsIdToken;

        // Get validated client
        Client? validatedClient = null;
        if (context.Properties.TryGetValue("ValidatedClient", out var clientObj) && clientObj is Client c)
        {
            validatedClient = c;
        }

        string? code = null;
        string? accessToken = null;
        string? idToken = null;
        int? accessTokenLifetime = null;

        // --- Authorization code ---
        if (wantsCode)
        {
            var codeClaims = new Dictionary<string, object>(authResult.Claims);

            if (!string.IsNullOrEmpty(request.MaxAge))
            {
                codeClaims["max_age_requested"] = "true";
            }

            // Carry allowlisted custom authorize parameters through to the token
            // endpoint under a transport prefix. The AuthorizeParameterClaimsProvider
            // maps + validates them into real claims at issuance; the raw prefixed
            // keys are stripped before they can reach a token.
            foreach (var captured in CaptureAllowedAuthorizeParameters(validatedClient, request.Raw))
            {
                codeClaims[ClientAuthorizeParameter.TransportClaimPrefix + captured.Key] = captured.Value;
            }

            // The same code shape as every other sign-in (AuthorizationCodes): session, methods and time
            // travel with it to the token endpoint.
            var authCode = AuthorizationCodes.Create(new NewAuthorizationCode
            {
                ClientId = request.ClientId!,
                SubjectId = authResult.UserId!,
                RedirectUri = request.RedirectUri,
                Scopes = authResult.Scopes,
                CodeChallenge = request.CodeChallenge,
                CodeChallengeMethod = request.CodeChallengeMethod,
                Nonce = request.Nonce,
                SessionId = authResult.SessionId,
                AuthTime = authResult.AuthTime,
                Amr = authResult.Amr,
                Acr = authResult.Acr,
                Claims = codeClaims,
                // RFC 8707 §2.1: resources named at sign-in apply to the whole grant.
                Resources = request.Resource,
                LifetimeSeconds = validatedClient?.AuthorizationCodeLifetime ?? 0
            });
            // RFC 9449 §10: the code is redeemable only with a DPoP proof by this key (kept beside amr/acr).
            if (request.Raw != null && request.Raw.TryGetValue(DPoPJktParameter, out var codeJkt) && !string.IsNullOrEmpty(codeJkt))
            {
                authCode.Properties ??= new Dictionary<string, string>();
                authCode.Properties[AuthorizationCodeDPoPJktProperty] = codeJkt;
            }
            code = authCode.Code;

            await _authorizationCodeStore.StoreAsync(authCode, cancellationToken);
        }

        // --- Implicit/hybrid token creation ---
        if (wantsToken || wantsIdToken)
        {
            // Build profile claims for tokens issued from the authorize endpoint
            var scopes = authResult.Scopes.ToList();
            var claims = new Dictionary<string, object>();

            if (validatedClient != null && !string.IsNullOrEmpty(authResult.UserId))
            {
                // Tokens minted directly at /authorize (implicit/hybrid) never round-trip
                // through a code, so pass the captured authorize parameters straight to the
                // claims providers as AdditionalData (keyed by parameter name).
                var authorizeParams = CaptureAllowedAuthorizeParameters(validatedClient, request.Raw);
                var additionalData = authorizeParams.Count > 0
                    ? authorizeParams.ToDictionary(kv => kv.Key, kv => (object)kv.Value)
                    : null;

                var profileClaims = await _profileService.GetProfileClaimsAsync(
                    authResult.UserId, validatedClient, scopes, "AuthorizeEndpoint", ProtocolName, null, additionalData, cancellationToken);
                foreach (var claim in profileClaims)
                {
                    claims[claim.Key] = claim.Value;
                }
            }

            // Get permissions if the scope is requested
            ICollection<string> permissions = Array.Empty<string>();
            if (scopes.Contains(OidcConstants.Scopes.Permissions) && !string.IsNullOrEmpty(authResult.UserId))
            {
                var tenantId = _tenantContext.HasTenant ? _tenantContext.TenantId : null;
                permissions = await _profileService.GetUserPermissionsAsync(authResult.UserId, tenantId, cancellationToken);
            }

            accessTokenLifetime = validatedClient?.AccessTokenLifetime > 0
                ? validatedClient.AccessTokenLifetime
                : 3600;

            var tokenRequest = new TokenCreationRequest
            {
                SubjectId = authResult.UserId,
                ClientId = request.ClientId!,
                ClientName = validatedClient?.ClientName,
                Scopes = scopes,
                Claims = claims,
                Permissions = permissions.ToList(),
                Lifetime = accessTokenLifetime.Value,
                IdentityTokenLifetime = validatedClient?.IdentityTokenLifetime ?? 300,
                Nonce = request.Nonce,
                AuthTime = authResult.AuthTime,
                Amr = authResult.Amr.ToList(),
                Acr = authResult.Acr,
                SessionId = authResult.SessionId,
                PairWiseSubjectSalt = validatedClient?.PairWiseSubjectSalt,
                IncludeJwtId = validatedClient?.IncludeJwtId ?? true,
                AlwaysIncludeUserClaimsInIdToken = validatedClient?.AlwaysIncludeUserClaimsInIdToken ?? false,
                // Same audience rule as the token endpoint (RFC 8707 / RFC 9068 §3).
                Audiences = await _audiences.ResolveAsync(
                    request.Resource, scopes, request.ClientId!,
                    validatedClient?.AllowedResources ?? new List<string>(), cancellationToken)
            };

            // Create access token if requested
            if (wantsToken)
            {
                accessToken = await _tokenService.CreateAccessTokenAsync(tokenRequest, cancellationToken);
            }

            // Create ID token if requested
            if (wantsIdToken)
            {
                var atHash = accessToken != null ? TokenService.ComputeHash(accessToken) : null;
                var cHash = code != null ? TokenService.ComputeHash(code) : null;
                idToken = await _tokenService.CreateIdTokenAsync(tokenRequest, atHash, cHash, cancellationToken);
            }
        }

        // Determine default response mode: fragment for implicit/hybrid, query for code
        var responseMode = request.ResponseMode;
        if (string.IsNullOrEmpty(responseMode))
        {
            responseMode = isImplicitOrHybrid ? "fragment" : "query";
        }

        _logger.LogInformation(
            "Authorization response issued for client {ClientId}, user {UserId}, scopes {Scopes}, responseTypes {ResponseTypes}",
            request.ClientId, authResult.UserId, string.Join(" ", authResult.Scopes),
            string.Join(" ", responseTypes));

        await _usage.RecordAsync(request.ClientId ?? "", cancellationToken);

        // Clean up protocol state
        if (!string.IsNullOrEmpty(context.CorrelationId))
        {
            await _stateStore.RemoveAsync(context.CorrelationId, cancellationToken);
        }

        // RFC 9207: the authorization server identifies itself with the iss
        // response parameter so the client can detect mix-up attacks.
        var issuer = await _issuerResolver.GetIssuerAsync(cancellationToken);

        // RFC 6749 §4.2.2: an access token from this endpoint comes with the scope it was granted, which can be
        // narrower than the request (scopes the server left out). A code's scope is reported at the token endpoint.
        var grantedScope = accessToken != null ? string.Join(" ", authResult.Scopes) : null;

        // Build response with all tokens
        // Both modes answer with a page a client may load in a hidden frame (silent renewal); its own origin may frame it.
        if (responseMode is "form_post" or "web_message")
            FramePolicy.AllowOriginOf(context.HttpContext, request.RedirectUri);

        if (responseMode == "form_post")
        {
            var html = BuildFormPostResponse(
                request.RedirectUri, code, accessToken, accessTokenLifetime, idToken, request.State, issuer, grantedScope);

            if (isImplicitOrHybrid)
            {
                return new DeprecatedContentResult(html);
            }

            return new ContentResult
            {
                Content = html,
                ContentType = "text/html",
                StatusCode = 200
            };
        }

        // Web Message Response Mode (OAuth 2.0 Web Message) — the SPA opens
        // /authorize in a hidden iframe (typically prompt=none silent renewal)
        // and the result is delivered via window.postMessage to the opener,
        // targeted at the validated redirect_uri's origin (never "*").
        if (responseMode == "web_message")
        {
            var html = BuildWebMessageResponse(
                request.RedirectUri, code, accessToken, accessTokenLifetime, idToken, request.State, issuer, grantedScope);

            return new ContentResult
            {
                Content = html,
                ContentType = "text/html",
                StatusCode = 200
            };
        }

        var responseUrl = BuildAuthorizationResponse(
            request.RedirectUri, code, accessToken, accessTokenLifetime, idToken, request.State, responseMode, issuer, grantedScope);

        if (isImplicitOrHybrid)
        {
            return new DeprecatedRedirectResult(responseUrl);
        }

        // Native clients use a custom-scheme redirect URI (e.g. oluso.auth://callback).
        // A 302 to a custom scheme renders no document, so the browser is left showing
        // the login page (credentials still on screen) while the OS hands off to the app.
        // Render an interstitial that fires the deep link via location.replace — which
        // also evicts the login page from history — with a manual fallback link.
        if (IsCustomScheme(request.RedirectUri))
        {
            return new ContentResult
            {
                Content = BuildNativeRedirectInterstitial(responseUrl),
                ContentType = "text/html",
                StatusCode = 200
            };
        }

        return new RedirectResult(responseUrl);
    }

    public IActionResult BuildErrorResponse(ProtocolContext? context, ProtocolError error)
    {
        var request = context?.Request as OidcAuthorizeRequest;

        // Determine the redirect URI to use:
        // 1. If the error has a validated redirect URI (from validation), use that
        // 2. Otherwise, check if context has a validated client (meaning redirect_uri was validated)
        var hasValidatedRedirectUri = error.RedirectUriValidated && !string.IsNullOrEmpty(error.ValidatedRedirectUri);
        var hasValidatedClient = context?.Properties.ContainsKey("ValidatedClient") == true;
        var redirectUri = hasValidatedRedirectUri
            ? error.ValidatedRedirectUri
            : (hasValidatedClient ? request?.RedirectUri : null);

        // RFC 9207: include the iss parameter on error responses sent to the
        // client too. This method is on the synchronous IProtocolService
        // contract (shared with SAML), so the issuer is resolved inline only on
        // these client-facing error branches — an error path, not the hot path.
        // ASP.NET Core has no synchronization context, so this does not deadlock.
        string? issuer = null;
        if (!string.IsNullOrEmpty(redirectUri) && IsSafeError(error.Code))
        {
            issuer = _issuerResolver.GetIssuerAsync().GetAwaiter().GetResult();
        }

        // If we have a validated redirect URI and it's a safe error, deliver the
        // error to the client. For web_message (silent iframe renewal) a redirect
        // would just hang in the hidden iframe — post the error to the opener
        // instead. login_required / interaction_required arrive here when a
        // prompt=none request can't be satisfied without UI.
        if (!string.IsNullOrEmpty(redirectUri) && IsSafeError(error.Code) && request?.ResponseMode == "web_message")
        {
            _logger.LogInformation("Posting web_message error {Error} to {RedirectUri}", error.Code, redirectUri);
            if (context?.HttpContext != null) FramePolicy.AllowOriginOf(context.HttpContext, redirectUri);
            var html = BuildWebMessageErrorResponse(redirectUri, error.Code, error.Description, request.State, issuer);
            return new ContentResult { Content = html, ContentType = "text/html", StatusCode = 200 };
        }

        // If we have a validated redirect URI and it's a safe error, redirect with error to client
        if (!string.IsNullOrEmpty(redirectUri) && IsSafeError(error.Code))
        {
            _logger.LogInformation("Redirecting to {RedirectUri} with error {Error}", redirectUri, error.Code);
            var errorUrl = BuildErrorResponse(redirectUri, error.Code, error.Description, request?.State, request?.ResponseMode, issuer);
            return new RedirectResult(errorUrl);
        }

        // For errors where we can't trust the redirect_uri (invalid_client, no validated client),
        // redirect to our error page instead of returning raw JSON
        _logger.LogInformation("Showing error page for error {Error}: {Description}", error.Code, error.Description);
        var errorPageUrl = $"/error?error={Uri.EscapeDataString(error.Code)}";
        if (!string.IsNullOrEmpty(error.Description))
        {
            errorPageUrl += $"&error_description={Uri.EscapeDataString(error.Description)}";
        }
        if (!string.IsNullOrEmpty(request?.ClientId))
        {
            // The error page offers the way back to this app's registered homepage, if it has one.
            errorPageUrl += $"&client_id={Uri.EscapeDataString(request.ClientId)}";
        }
        return new RedirectResult(errorPageUrl);
    }

    private async Task<OidcAuthorizeRequest> ParseAuthorizeRequestAsync(HttpContext http)
    {
        var parameters = new Dictionary<string, string>(StringComparer.Ordinal);

        // From query string
        foreach (var param in http.Request.Query)
        {
            parameters[param.Key] = OidcResourceParameters.Flatten(param.Key, param.Value);
        }

        // From form (POST) - overrides query
        if (http.Request.HasFormContentType)
        {
            var form = await http.Request.ReadFormAsync();
            foreach (var param in form)
            {
                parameters[param.Key] = OidcResourceParameters.Flatten(param.Key, param.Value);
            }
        }

        // Check for PAR request_uri - RFC 9126
        if (parameters.TryGetValue("request_uri", out var requestUri) &&
            requestUri.StartsWith("urn:ietf:params:oauth:request_uri:", StringComparison.Ordinal))
        {
            parameters = await ResolveParRequestAsync(requestUri, parameters);
        }

        var request = new OidcAuthorizeRequest
        {
            Raw = parameters
        };

        // Parse standard parameters
        parameters.TryGetValue("client_id", out var clientId);
        request.ClientId = clientId;

        parameters.TryGetValue("redirect_uri", out var redirectUri);
        request.RedirectUri = redirectUri;

        parameters.TryGetValue("response_type", out var responseType);
        request.ResponseType = responseType;
        request.RequestedResponseTypes = responseType?.Split(' ', StringSplitOptions.RemoveEmptyEntries).ToList() ?? new List<string>();

        parameters.TryGetValue("scope", out var scope);
        request.Scope = scope;
        request.RequestedScopes = scope?.Split(' ', StringSplitOptions.RemoveEmptyEntries).ToList() ?? new List<string>();

        parameters.TryGetValue("state", out var state);
        request.State = state;

        parameters.TryGetValue("nonce", out var nonce);
        request.Nonce = nonce;

        parameters.TryGetValue("response_mode", out var responseMode);
        request.ResponseMode = responseMode;

        // OIDC parameters
        parameters.TryGetValue("prompt", out var prompt);
        request.Prompt = prompt;

        parameters.TryGetValue("max_age", out var maxAge);
        request.MaxAge = maxAge;

        parameters.TryGetValue("ui_locales", out var uiLocales);
        request.UiLocales = uiLocales;

        parameters.TryGetValue("id_token_hint", out var idTokenHint);
        request.IdTokenHint = idTokenHint;

        parameters.TryGetValue("login_hint", out var loginHint);
        request.LoginHint = loginHint;

        parameters.TryGetValue("acr_values", out var acrValues);
        request.AcrValues = acrValues;

        // Domain hint for IdP selection
        parameters.TryGetValue("domain_hint", out var domainHint);
        request.DomainHint = domainHint;

        // PKCE
        parameters.TryGetValue("code_challenge", out var codeChallenge);
        request.CodeChallenge = codeChallenge;

        parameters.TryGetValue("code_challenge_method", out var codeChallengeMethod);
        request.CodeChallengeMethod = codeChallengeMethod;

        // Policy ID (custom parameter)
        if (parameters.TryGetValue(_config.PolicyQueryParam, out var policyId))
        {
            request.PolicyId = policyId;
        }
        else if (!string.IsNullOrEmpty(_config.PolicyQueryParamAlternate) &&
                 parameters.TryGetValue(_config.PolicyQueryParamAlternate, out var altPolicyId))
        {
            request.PolicyId = altPolicyId;
        }

        // UI mode
        if (parameters.TryGetValue(_config.UiModeQueryParam, out var uiMode))
        {
            request.UiMode = uiMode;
        }

        // Resource indicators (RFC 8707)
        if (parameters.TryGetValue(OidcResourceParameters.Name, out var resource))
        {
            request.Resource = OidcResourceParameters.Split(resource);
        }

        return request;
    }

    private static AuthorizeRequest ConvertToAuthorizeRequest(OidcAuthorizeRequest oidcRequest)
    {
        return new AuthorizeRequest
        {
            Raw = oidcRequest.Raw ?? new Dictionary<string, string>(),
            ClientId = oidcRequest.ClientId ?? "",
            RedirectUri = oidcRequest.RedirectUri,
            ResponseType = oidcRequest.ResponseType ?? "",
            RequestedResponseTypes = oidcRequest.RequestedResponseTypes,
            Scope = oidcRequest.Scope,
            RequestedScopes = oidcRequest.RequestedScopes,
            State = oidcRequest.State,
            Nonce = oidcRequest.Nonce,
            ResponseMode = oidcRequest.ResponseMode,
            Prompt = oidcRequest.Prompt,
            MaxAge = oidcRequest.MaxAge,
            UiLocales = oidcRequest.UiLocales,
            IdTokenHint = oidcRequest.IdTokenHint,
            LoginHint = oidcRequest.LoginHint,
            AcrValues = oidcRequest.AcrValues,
            CodeChallenge = oidcRequest.CodeChallenge,
            CodeChallengeMethod = oidcRequest.CodeChallengeMethod,
            Resource = oidcRequest.Resource?.ToList() ?? new List<string>()
        };
    }

    private async Task<bool> CheckConsentRequiredAsync(
        string subjectId,
        Client client,
        ICollection<string> requestedScopes,
        string? prompt,
        CancellationToken cancellationToken)
    {
        // Check prompt=consent
        if (prompt?.Contains("consent") == true)
        {
            return true;
        }

        // Check if client requires consent
        if (!client.RequireConsent)
        {
            return false;
        }

        // Check if user has previously consented to all requested scopes
        var hasConsent = await _consentStore.HasConsentAsync(subjectId, client.ClientId, requestedScopes, cancellationToken);
        return !hasConsent;
    }

    /// <summary>
    /// Returns the subset of the raw authorize parameters that this client has
    /// allowlisted via <see cref="Client.AllowedAuthorizeParameters"/>, keyed by
    /// parameter name. Anything not on the allowlist is ignored — the OP never
    /// forwards unknown authorize parameters. Empty values are skipped.
    /// </summary>
    private static Dictionary<string, string> CaptureAllowedAuthorizeParameters(
        Client? client, IDictionary<string, string>? raw)
    {
        var result = new Dictionary<string, string>(StringComparer.Ordinal);
        if (client?.AllowedAuthorizeParameters == null || raw == null)
        {
            return result;
        }

        foreach (var allowed in client.AllowedAuthorizeParameters)
        {
            if (raw.TryGetValue(allowed.Parameter, out var value) && !string.IsNullOrEmpty(value))
            {
                result[allowed.Parameter] = value;
            }
        }

        return result;
    }

    private static string BuildAuthorizationResponse(
        string redirectUri, string? code, string? accessToken, int? expiresIn,
        string? idToken, string? state, string? responseMode, string? issuer, string? scope = null)
    {
        var isFragment = responseMode == "fragment";
        var separator = isFragment ? "#" : (redirectUri.Contains('?') ? "&" : "?");

        var parts = new List<string>();

        if (!string.IsNullOrEmpty(code))
            parts.Add($"code={Uri.EscapeDataString(code)}");
        if (!string.IsNullOrEmpty(accessToken))
        {
            parts.Add($"access_token={Uri.EscapeDataString(accessToken)}");
            parts.Add("token_type=Bearer");
            if (expiresIn.HasValue)
                parts.Add($"expires_in={expiresIn.Value}");
            if (scope != null)
                parts.Add($"scope={Uri.EscapeDataString(scope)}");
        }
        if (!string.IsNullOrEmpty(idToken))
            parts.Add($"id_token={Uri.EscapeDataString(idToken)}");
        if (!string.IsNullOrEmpty(state))
            parts.Add($"state={Uri.EscapeDataString(state)}");
        if (!string.IsNullOrEmpty(issuer))
            parts.Add($"iss={Uri.EscapeDataString(issuer)}");

        return $"{redirectUri}{separator}{string.Join("&", parts)}";
    }

    private static string BuildFormPostResponse(
        string redirectUri, string? code, string? accessToken, int? expiresIn,
        string? idToken, string? state, string? issuer, string? scope = null)
    {
        var sb = new StringBuilder();
        sb.Append("<!DOCTYPE html><html><head><title>Submit</title></head><body onload=\"document.forms[0].submit()\">");
        sb.Append($"<form method=\"post\" action=\"{WebUtility.HtmlEncode(redirectUri)}\">");

        if (!string.IsNullOrEmpty(code))
            sb.Append($"<input type=\"hidden\" name=\"code\" value=\"{WebUtility.HtmlEncode(code)}\" />");
        if (!string.IsNullOrEmpty(accessToken))
        {
            sb.Append($"<input type=\"hidden\" name=\"access_token\" value=\"{WebUtility.HtmlEncode(accessToken)}\" />");
            sb.Append("<input type=\"hidden\" name=\"token_type\" value=\"Bearer\" />");
            if (expiresIn.HasValue)
                sb.Append($"<input type=\"hidden\" name=\"expires_in\" value=\"{expiresIn.Value}\" />");
            if (scope != null)
                sb.Append($"<input type=\"hidden\" name=\"scope\" value=\"{WebUtility.HtmlEncode(scope)}\" />");
        }
        if (!string.IsNullOrEmpty(idToken))
            sb.Append($"<input type=\"hidden\" name=\"id_token\" value=\"{WebUtility.HtmlEncode(idToken)}\" />");
        if (!string.IsNullOrEmpty(state))
            sb.Append($"<input type=\"hidden\" name=\"state\" value=\"{WebUtility.HtmlEncode(state)}\" />");
        if (!string.IsNullOrEmpty(issuer))
            sb.Append($"<input type=\"hidden\" name=\"iss\" value=\"{WebUtility.HtmlEncode(issuer)}\" />");

        sb.Append("<noscript><button type=\"submit\">Continue</button></noscript>");
        sb.Append("</form></body></html>");
        return sb.ToString();
    }

    private static string BuildWebMessageResponse(
        string redirectUri, string? code, string? accessToken, int? expiresIn,
        string? idToken, string? state, string? issuer, string? scope = null)
    {
        var response = new Dictionary<string, object?>(StringComparer.Ordinal);
        if (!string.IsNullOrEmpty(code))
            response["code"] = code;
        if (!string.IsNullOrEmpty(accessToken))
        {
            response["access_token"] = accessToken;
            response["token_type"] = "Bearer";
            if (expiresIn.HasValue)
                response["expires_in"] = expiresIn.Value;
            if (scope != null)
                response["scope"] = scope;
        }
        if (!string.IsNullOrEmpty(idToken))
            response["id_token"] = idToken;
        if (!string.IsNullOrEmpty(state))
            response["state"] = state;
        if (!string.IsNullOrEmpty(issuer))
            response["iss"] = issuer;

        return RenderWebMessageHtml(redirectUri, response);
    }

    private static string BuildWebMessageErrorResponse(
        string redirectUri, string error, string? description, string? state, string? issuer)
    {
        var response = new Dictionary<string, object?>(StringComparer.Ordinal) { ["error"] = error };
        if (!string.IsNullOrEmpty(description))
            response["error_description"] = description;
        if (!string.IsNullOrEmpty(state))
            response["state"] = state;
        if (!string.IsNullOrEmpty(issuer))
            response["iss"] = issuer;

        return RenderWebMessageHtml(redirectUri, response);
    }

    /// <summary>
    /// Renders the OAuth Web Message page that delivers the authorization
    /// response (or error) to the opener via window.postMessage. The message is
    /// targeted at the validated redirect_uri's origin — never "*" — so the
    /// code/tokens can only reach the registered client's own page. Falls back
    /// to "null" (an origin no document matches) when the redirect_uri is not an
    /// absolute http(s) URI. JSON is HTML-escaped by the serializer, so neither
    /// the payload nor the origin can break out of the &lt;script&gt; element.
    /// </summary>
    private static string RenderWebMessageHtml(string redirectUri, IDictionary<string, object?> response)
    {
        var targetOrigin = "null";
        if (Uri.TryCreate(redirectUri, UriKind.Absolute, out var uri)
            && (uri.Scheme == "http" || uri.Scheme == "https"))
        {
            targetOrigin = uri.GetLeftPart(UriPartial.Authority);
        }

        var payload = JsonSerializer.Serialize(new Dictionary<string, object?>(StringComparer.Ordinal)
        {
            ["type"] = "authorization_response",
            ["response"] = response
        });
        var jsTargetOrigin = JsonSerializer.Serialize(targetOrigin);

        return $@"<!DOCTYPE html><html><head><title>Completing sign-in…</title></head><body>
<script>
(function(){{
  var data = {payload};
  var origin = {jsTargetOrigin};
  var target = window.opener || (window.parent !== window ? window.parent : null);
  if (target) {{ target.postMessage(data, origin); }}
}})();
</script>
</body></html>";
    }

    private static bool IsCustomScheme(string redirectUri) =>
        Uri.TryCreate(redirectUri, UriKind.Absolute, out var uri)
        && uri.Scheme != "http" && uri.Scheme != "https" && uri.Scheme != "urn";

    private static string BuildNativeRedirectInterstitial(string responseUrl)
    {
        // location.replace target as a safely-escaped JS string literal; the same
        // URL HTML-attribute-encoded for the no-JS fallback link.
        var jsTarget = JsonSerializer.Serialize(responseUrl);
        var hrefTarget = WebUtility.HtmlEncode(responseUrl);

        return $@"<!DOCTYPE html>
<html lang=""en"">
<head>
<meta charset=""utf-8"" />
<meta name=""viewport"" content=""width=device-width, initial-scale=1"" />
<title>Returning to app…</title>
<style>
body{{font-family:-apple-system,BlinkMacSystemFont,'Segoe UI',Roboto,sans-serif;margin:0;min-height:100vh;display:flex;align-items:center;justify-content:center;background:#f8fafc;color:#0f172a}}
.card{{text-align:center;padding:2rem}}
.btn{{display:inline-block;margin-top:1rem;padding:.75rem 1.25rem;border-radius:.5rem;background:#0f172a;color:#fff;text-decoration:none;font-weight:600}}
</style>
</head>
<body>
<div class=""card"">
<p>Completing sign-in…</p>
<a class=""btn"" href=""{hrefTarget}"">Return to app</a>
</div>
<script>(function(){{window.location.replace({jsTarget});}})();</script>
</body>
</html>";
    }


    private static string BuildErrorResponse(string redirectUri, string error, string? description, string? state, string? responseMode, string? issuer)
    {
        var isFragment = responseMode == "fragment";
        var separator = isFragment ? "#" : (redirectUri.Contains('?') ? "&" : "?");

        var url = $"{redirectUri}{separator}error={Uri.EscapeDataString(error)}";

        if (!string.IsNullOrEmpty(description))
        {
            url += $"&error_description={Uri.EscapeDataString(description)}";
        }

        if (!string.IsNullOrEmpty(state))
        {
            url += $"&state={Uri.EscapeDataString(state)}";
        }

        if (!string.IsNullOrEmpty(issuer))
        {
            url += $"&iss={Uri.EscapeDataString(issuer)}";
        }

        return url;
    }

    private static Core.UserJourneys.JourneyType DeterminePolicyType(OidcAuthorizeRequest request)
    {
        // Check prompt parameter
        if (request.Prompt?.Contains("create") == true)
        {
            return Core.UserJourneys.JourneyType.SignUp;
        }

        // Default to sign-in
        return Core.UserJourneys.JourneyType.SignIn;
    }

    /// <summary>
    /// Resolve PAR request_uri to the stored authorization request parameters.
    /// Per RFC 9126, the request_uri replaces the authorization request parameters.
    /// </summary>
    private async Task<Dictionary<string, string>> ResolveParRequestAsync(
        string requestUri,
        Dictionary<string, string> originalParameters)
    {
        var parRequest = await _parStore.GetAsync(requestUri);

        if (parRequest == null)
        {
            _logger.LogWarning("PAR request_uri not found or expired: {RequestUri}", requestUri);
            throw new InvalidOperationException("Invalid or expired request_uri");
        }

        // Check if expired
        if (parRequest.ExpiresAtUtc < DateTime.UtcNow)
        {
            _logger.LogWarning("PAR request_uri expired: {RequestUri}", requestUri);
            await _parStore.RemoveAsync(requestUri);
            throw new InvalidOperationException("Expired request_uri");
        }

        // Deserialize stored parameters
        var storedRequest = JsonSerializer.Deserialize<AuthorizeRequest>(parRequest.Parameters);
        if (storedRequest == null)
        {
            _logger.LogError("Failed to deserialize PAR parameters for: {RequestUri}", requestUri);
            throw new InvalidOperationException("Invalid PAR request data");
        }

        // Validate client_id matches if provided in the authorize request
        if (originalParameters.TryGetValue("client_id", out var providedClientId) &&
            !string.IsNullOrEmpty(providedClientId) &&
            providedClientId != parRequest.ClientId)
        {
            _logger.LogWarning(
                "PAR client_id mismatch: authorize={ProvidedClientId}, PAR={ParClientId}",
                providedClientId, parRequest.ClientId);
            throw new InvalidOperationException("client_id mismatch");
        }

        // Remove the PAR after use (one-time use per RFC 9126)
        await _parStore.RemoveAsync(requestUri);

        _logger.LogDebug("Resolved PAR request_uri {RequestUri} for client {ClientId}", requestUri, parRequest.ClientId);

        // Build parameters from stored request, allowing override of state from authorize request
        var resolvedParameters = new Dictionary<string, string>(StringComparer.Ordinal)
        {
            ["client_id"] = parRequest.ClientId
        };

        if (!string.IsNullOrEmpty(storedRequest.RedirectUri))
            resolvedParameters["redirect_uri"] = storedRequest.RedirectUri;
        if (!string.IsNullOrEmpty(storedRequest.ResponseType))
            resolvedParameters["response_type"] = storedRequest.ResponseType;
        if (!string.IsNullOrEmpty(storedRequest.Scope))
            resolvedParameters["scope"] = storedRequest.Scope;
        if (!string.IsNullOrEmpty(storedRequest.Nonce))
            resolvedParameters["nonce"] = storedRequest.Nonce;
        if (!string.IsNullOrEmpty(storedRequest.CodeChallenge))
            resolvedParameters["code_challenge"] = storedRequest.CodeChallenge;
        if (!string.IsNullOrEmpty(storedRequest.CodeChallengeMethod))
            resolvedParameters["code_challenge_method"] = storedRequest.CodeChallengeMethod;
        if (!string.IsNullOrEmpty(storedRequest.ResponseMode))
            resolvedParameters["response_mode"] = storedRequest.ResponseMode;
        if (!string.IsNullOrEmpty(storedRequest.Prompt))
            resolvedParameters["prompt"] = storedRequest.Prompt;
        if (!string.IsNullOrEmpty(storedRequest.MaxAge))
            resolvedParameters["max_age"] = storedRequest.MaxAge;
        if (!string.IsNullOrEmpty(storedRequest.IdTokenHint))
            resolvedParameters["id_token_hint"] = storedRequest.IdTokenHint;
        if (!string.IsNullOrEmpty(storedRequest.LoginHint))
            resolvedParameters["login_hint"] = storedRequest.LoginHint;
        if (!string.IsNullOrEmpty(storedRequest.AcrValues))
            resolvedParameters["acr_values"] = storedRequest.AcrValues;
        if (!string.IsNullOrEmpty(storedRequest.UiLocales))
            resolvedParameters["ui_locales"] = storedRequest.UiLocales;
        if (storedRequest.Resource is { Count: > 0 })
            resolvedParameters[OidcResourceParameters.Name] = string.Join(' ', storedRequest.Resource);
        if (!string.IsNullOrEmpty(storedRequest.DPoPKeyThumbprint))
            resolvedParameters[DPoPJktParameter] = storedRequest.DPoPKeyThumbprint;

        // State can be provided in the authorize request (not required to be in PAR)
        if (originalParameters.TryGetValue("state", out var state) && !string.IsNullOrEmpty(state))
        {
            resolvedParameters["state"] = state;
        }
        else if (!string.IsNullOrEmpty(storedRequest.State))
        {
            resolvedParameters["state"] = storedRequest.State;
        }

        return resolvedParameters;
    }

    /// <summary>The authorize parameter binding the code to a DPoP key (RFC 9449 §10).</summary>
    public const string DPoPJktParameter = "dpop_jkt";

    /// <summary>The authorization-code property holding the <see cref="DPoPJktParameter"/> value.</summary>
    public const string AuthorizationCodeDPoPJktProperty = "dpop_jkt";

    /// <summary>A base64url (unpadded) SHA-256 value: 43 characters of the URL-safe alphabet.</summary>
    internal static bool IsJwkThumbprint(string value) =>
        value.Length == 43 && value.All(c => char.IsAsciiLetterOrDigit(c) || c is '-' or '_');

    private static bool IsSafeError(string error)
    {
        // Errors that are safe to redirect back to client
        return error switch
        {
            "access_denied" => true,
            "login_required" => true,
            "consent_required" => true,
            "interaction_required" => true,
            "account_selection_required" => true,
            "invalid_request" => true,
            "unauthorized_client" => true,
            "unsupported_response_type" => true,
            "invalid_scope" => true,
            "temporarily_unavailable" => true,
            _ => false
        };
    }
}

/// <summary>
/// A redirect result that adds deprecation headers for implicit/hybrid flows.
/// Per RFC 8594, signals that the response type is deprecated.
/// </summary>
internal class DeprecatedRedirectResult : IActionResult
{
    private readonly string _url;

    public DeprecatedRedirectResult(string url)
    {
        _url = url;
    }

    public Task ExecuteResultAsync(ActionContext context)
    {
        context.HttpContext.Response.Headers["Deprecation"] = "true";
        context.HttpContext.Response.Headers["Sunset"] = "2027-01-01";
        context.HttpContext.Response.Redirect(_url);
        return Task.CompletedTask;
    }
}

/// <summary>
/// An HTML content result that adds deprecation headers for implicit/hybrid flows.
/// Used for form_post response mode.
/// </summary>
internal class DeprecatedContentResult : IActionResult
{
    private readonly string _html;

    public DeprecatedContentResult(string html)
    {
        _html = html;
    }

    public async Task ExecuteResultAsync(ActionContext context)
    {
        var response = context.HttpContext.Response;
        response.Headers["Deprecation"] = "true";
        response.Headers["Sunset"] = "2027-01-01";
        response.ContentType = "text/html";
        response.StatusCode = 200;
        await response.WriteAsync(_html);
    }
}
