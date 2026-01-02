using System.Security.Cryptography;
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
    private readonly ILogger<OidcProtocolService> _logger;

    public string ProtocolName => "oidc";

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
        ILogger<OidcProtocolService> logger)
    {
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
        }

        // Pass domain_hint if present in the request
        if (!string.IsNullOrEmpty(oidcRequest.DomainHint))
        {
            context.Properties["DomainHint"] = oidcRequest.DomainHint;
        }

        // Check if user is authenticated
        var user = context.HttpContext.User;
        if (user.Identity?.IsAuthenticated != true)
        {
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

        var needsConsent = await CheckConsentRequiredAsync(
            subjectId,
            client,
            validationResult.ValidScopes,
            oidcRequest.Prompt,
            cancellationToken);

        if (needsConsent)
        {
            // Return consent required - the journey/UI will handle the consent step
            return ProtocolRequestResult.RequiresConsent(new ConsentRequirement
            {
                ClientId = client.ClientId,
                ClientName = client.ClientName ?? client.ClientId,
                RequestedScopes = validationResult.ValidScopes.ToList(),
                SubjectId = subjectId
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

        // Generate authorization code
        var code = GenerateAuthorizationCode();

        // Get authorization code lifetime from client (default to 300 seconds if not set)
        var codeLifetime = 300; // Default 5 minutes
        if (context.Properties.TryGetValue("ValidatedClient", out var clientObj) && clientObj is Client validatedClient)
        {
            codeLifetime = validatedClient.AuthorizationCodeLifetime > 0
                ? validatedClient.AuthorizationCodeLifetime
                : 300;
        }

        // Store authorization code
        var authCode = new AuthorizationCode
        {
            Code = code,
            ClientId = request.ClientId!,
            SubjectId = authResult.UserId,
            RedirectUri = request.RedirectUri,
            Scopes = authResult.Scopes.ToList(),
            CodeChallenge = request.CodeChallenge,
            CodeChallengeMethod = request.CodeChallengeMethod,
            Nonce = request.Nonce,
            SessionId = authResult.SessionId,
            CreationTime = DateTime.UtcNow,
            Expiration = DateTime.UtcNow.AddSeconds(codeLifetime),
            Claims = authResult.Claims
                .Where(c => c.Value is string)
                .ToDictionary(c => c.Key, c => (string)c.Value)
        };

        await _authorizationCodeStore.StoreAsync(authCode, cancellationToken);

        // Build redirect URL
        var responseUrl = BuildAuthorizationResponse(request.RedirectUri, code, request.State, request.ResponseMode);

        _logger.LogInformation(
            "Authorization code issued for client {ClientId}, user {UserId}, scopes {Scopes}",
            request.ClientId, authResult.UserId, string.Join(" ", authResult.Scopes));

        // Clean up protocol state
        if (!string.IsNullOrEmpty(context.CorrelationId))
        {
            await _stateStore.RemoveAsync(context.CorrelationId, cancellationToken);
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

        // If we have a validated redirect URI and it's a safe error, redirect with error to client
        if (!string.IsNullOrEmpty(redirectUri) && IsSafeError(error.Code))
        {
            _logger.LogInformation("Redirecting to {RedirectUri} with error {Error}", redirectUri, error.Code);
            var errorUrl = BuildErrorResponse(redirectUri, error.Code, error.Description, request?.State, request?.ResponseMode);
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
        return new RedirectResult(errorPageUrl);
    }

    private async Task<OidcAuthorizeRequest> ParseAuthorizeRequestAsync(HttpContext http)
    {
        var parameters = new Dictionary<string, string>(StringComparer.Ordinal);

        // From query string
        foreach (var param in http.Request.Query)
        {
            parameters[param.Key] = param.Value.ToString();
        }

        // From form (POST) - overrides query
        if (http.Request.HasFormContentType)
        {
            var form = await http.Request.ReadFormAsync();
            foreach (var param in form)
            {
                parameters[param.Key] = param.Value.ToString();
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

        return request;
    }

    private static AuthorizeRequest ConvertToAuthorizeRequest(OidcAuthorizeRequest oidcRequest)
    {
        return new AuthorizeRequest
        {
            Raw = oidcRequest.Raw,
            ClientId = oidcRequest.ClientId,
            RedirectUri = oidcRequest.RedirectUri,
            ResponseType = oidcRequest.ResponseType,
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
            CodeChallengeMethod = oidcRequest.CodeChallengeMethod
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

    private static string GenerateAuthorizationCode()
    {
        var bytes = new byte[32];
        RandomNumberGenerator.Fill(bytes);
        return Convert.ToBase64String(bytes)
            .Replace("+", "-")
            .Replace("/", "_")
            .TrimEnd('=');
    }

    private static string BuildAuthorizationResponse(string redirectUri, string code, string? state, string? responseMode)
    {
        var isFragment = responseMode == "fragment";
        var separator = isFragment ? "#" : (redirectUri.Contains('?') ? "&" : "?");

        var url = $"{redirectUri}{separator}code={Uri.EscapeDataString(code)}";

        if (!string.IsNullOrEmpty(state))
        {
            url += $"&state={Uri.EscapeDataString(state)}";
        }

        return url;
    }

    private static string BuildErrorResponse(string redirectUri, string error, string? description, string? state, string? responseMode)
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
