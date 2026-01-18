using Microsoft.AspNetCore.Http;
using Oluso.Core.Domain.Interfaces;
using Oluso.Core.Protocols.Models;
using Oluso.Core.Protocols.Validation;

namespace Oluso.Protocols.Validation;

/// <summary>
/// Default implementation of authorize request validator
/// </summary>
public class AuthorizeRequestValidator : IAuthorizeRequestValidator
{
    private readonly IClientStore _clientStore;
    private readonly IResourceStore _resourceStore;
    private readonly IRedirectUriValidator _redirectUriValidator;
    private readonly IScopeValidator _scopeValidator;
    private readonly IPkceValidator _pkceValidator;
    private readonly ITenantSettingsProvider _tenantSettingsProvider;

    public AuthorizeRequestValidator(
        IClientStore clientStore,
        IResourceStore resourceStore,
        IRedirectUriValidator redirectUriValidator,
        IScopeValidator scopeValidator,
        IPkceValidator pkceValidator,
        ITenantSettingsProvider tenantSettingsProvider)
    {
        _clientStore = clientStore;
        _resourceStore = resourceStore;
        _redirectUriValidator = redirectUriValidator;
        _scopeValidator = scopeValidator;
        _pkceValidator = pkceValidator;
        _tenantSettingsProvider = tenantSettingsProvider;
    }

    public async Task<AuthorizeValidationResult> ValidateAsync(
        HttpRequest request,
        CancellationToken cancellationToken = default)
    {
        // Parse parameters from query or form
        var parameters = await GetParametersAsync(request);

        var authorizeRequest = new AuthorizeRequest
        {
            Raw = parameters
        };

        // 1. Validate client_id (required)
        if (!parameters.TryGetValue("client_id", out var clientId) || string.IsNullOrEmpty(clientId))
        {
            return AuthorizeValidationResult.Failure(
                OidcConstants.Errors.InvalidRequest,
                "client_id is required");
        }
        authorizeRequest.ClientId = clientId;

        // Find client
        var client = await _clientStore.FindClientByIdAsync(clientId, cancellationToken);
        if (client == null)
        {
            return AuthorizeValidationResult.Failure(
                OidcConstants.Errors.InvalidClient,
                "Unknown client");
        }

        if (!client.Enabled)
        {
            return AuthorizeValidationResult.Failure(
                OidcConstants.Errors.InvalidClient,
                "Client is disabled");
        }

        // Get tenant protocol settings for enforcement
        var protocolSettings = await _tenantSettingsProvider.GetProtocolSettingsAsync(cancellationToken);

        // Enforce tenant-level PAR requirement (overrides client setting)
        if (protocolSettings.RequirePushedAuthorizationRequests)
        {
            if (!parameters.TryGetValue("request_uri", out var parRequestUri) ||
                string.IsNullOrEmpty(parRequestUri) ||
                !parRequestUri.StartsWith("urn:ietf:params:oauth:request_uri:", StringComparison.Ordinal))
            {
                return AuthorizeValidationResult.Failure(
                    OidcConstants.Errors.InvalidRequest,
                    "This tenant requires Pushed Authorization Requests (PAR). Use the PAR endpoint first.");
            }
        }

        // Check if client requires Pushed Authorization Request (PAR)
        // If client requires PAR, the request must come via request_uri from PAR endpoint
        if (client.RequirePushedAuthorization)
        {
            // Check if this is a PAR request (has request_uri that starts with PAR prefix)
            if (!parameters.TryGetValue("request_uri", out var parRequestUri) ||
                string.IsNullOrEmpty(parRequestUri) ||
                !parRequestUri.StartsWith("urn:ietf:params:oauth:request_uri:", StringComparison.Ordinal))
            {
                return AuthorizeValidationResult.Failure(
                    OidcConstants.Errors.InvalidRequest,
                    "This client requires Pushed Authorization Requests (PAR). Use the PAR endpoint first.");
            }
        }

        // 2. Validate redirect_uri
        parameters.TryGetValue("redirect_uri", out var redirectUri);
        authorizeRequest.RedirectUri = redirectUri;

        var allowedRedirectUris = client.RedirectUris.Select(r => r.RedirectUri).ToList();

        // If only one redirect URI is registered and none provided, use it
        if (string.IsNullOrEmpty(redirectUri) && allowedRedirectUris.Count == 1)
        {
            redirectUri = allowedRedirectUris.First();
            authorizeRequest.RedirectUri = redirectUri;
        }

        // 3. Validate response_type (required)
        if (!parameters.TryGetValue("response_type", out var responseType) || string.IsNullOrEmpty(responseType))
        {
            return AuthorizeValidationResult.Failure(
                OidcConstants.Errors.InvalidRequest,
                "response_type is required");
        }
        authorizeRequest.ResponseType = responseType;
        authorizeRequest.RequestedResponseTypes = responseType.Split(' ', StringSplitOptions.RemoveEmptyEntries).ToList();

        // Validate response type is allowed by tenant
        if (protocolSettings.AllowedResponseTypes?.Count > 0)
        {
            var tenantAllowedResponseTypes = new HashSet<string>(protocolSettings.AllowedResponseTypes);
            foreach (var rt in authorizeRequest.RequestedResponseTypes)
            {
                if (!tenantAllowedResponseTypes.Contains(rt))
                {
                    return AuthorizeValidationResult.Failure(
                        OidcConstants.Errors.UnsupportedResponseType,
                        $"Response type '{rt}' is not allowed for this tenant");
                }
            }
        }

        // Validate response type is allowed based on client's grant types and tenant-level restrictions
        var responseTypeValidation = ValidateResponseTypeForClient(
            authorizeRequest.RequestedResponseTypes,
            client.AllowedGrantTypes.Select(g => g.GrantType).ToList(),
            protocolSettings.AllowedGrantTypes);
        if (!responseTypeValidation.IsValid)
        {
            return AuthorizeValidationResult.Failure(
                responseTypeValidation.Error!,
                responseTypeValidation.ErrorDescription);
        }

        // Validate response type is allowed for client
        var isImplicitOrHybrid = authorizeRequest.RequestedResponseTypes.Any(rt =>
            rt == ResponseTypes.Token || rt == ResponseTypes.IdToken);

        var redirectValidation = await _redirectUriValidator.ValidateAsync(
            redirectUri,
            allowedRedirectUris,
            isImplicitOrHybrid);

        if (!redirectValidation.IsValid)
        {
            return AuthorizeValidationResult.Failure(
                redirectValidation.Error!,
                redirectValidation.ErrorDescription);
        }

        // At this point, redirect_uri is validated - errors from here can be redirected to client
        var validatedRedirectUri = redirectUri;

        // 4. Validate scope
        parameters.TryGetValue("scope", out var scope);
        authorizeRequest.Scope = scope;
        var requestedScopes = _scopeValidator.ParseScopes(scope).ToList();
        authorizeRequest.RequestedScopes = requestedScopes;

        var allowedScopes = client.AllowedScopes.Select(s => s.Scope).ToList();
        var scopeValidation = await _scopeValidator.ValidateAsync(requestedScopes, allowedScopes, cancellationToken);
        if (!scopeValidation.IsValid)
        {
            return AuthorizeValidationResult.Failure(
                scopeValidation.Error!,
                scopeValidation.ErrorDescription,
                redirectUriValidated: true,
                validatedRedirectUri: validatedRedirectUri);
        }

        // 5. Validate PKCE
        parameters.TryGetValue("code_challenge", out var codeChallenge);
        parameters.TryGetValue("code_challenge_method", out var codeChallengeMethod);
        authorizeRequest.CodeChallenge = codeChallenge;
        authorizeRequest.CodeChallengeMethod = codeChallengeMethod ?? CodeChallengeMethods.Plain;

        // PKCE is required if tenant or client requires it
        var isCodeFlow = authorizeRequest.RequestedResponseTypes.Contains(ResponseTypes.Code);
        var requirePkce = isCodeFlow && (protocolSettings.RequirePkce || client.RequirePkce);
        // Tenant can disallow plain PKCE even if client allows it
        var allowPlainPkce = client.AllowPlainTextPkce && protocolSettings.AllowPlainPkce;
        var pkceValidation = _pkceValidator.ValidateCodeChallenge(
            codeChallenge,
            codeChallengeMethod,
            requirePkce,
            allowPlainPkce);

        if (!pkceValidation.IsValid)
        {
            return AuthorizeValidationResult.Failure(
                pkceValidation.Error!,
                pkceValidation.ErrorDescription,
                redirectUriValidated: true,
                validatedRedirectUri: validatedRedirectUri);
        }

        // 6. Parse additional OIDC parameters
        parameters.TryGetValue("state", out var state);
        authorizeRequest.State = state;

        parameters.TryGetValue("nonce", out var nonce);
        authorizeRequest.Nonce = nonce;

        // Nonce is required for implicit flow with id_token
        if (authorizeRequest.RequestedResponseTypes.Contains(ResponseTypes.IdToken) && string.IsNullOrEmpty(nonce))
        {
            return AuthorizeValidationResult.Failure(
                OidcConstants.Errors.InvalidRequest,
                "nonce is required for implicit flow with id_token",
                redirectUriValidated: true,
                validatedRedirectUri: validatedRedirectUri);
        }

        parameters.TryGetValue("response_mode", out var responseMode);
        authorizeRequest.ResponseMode = responseMode;

        parameters.TryGetValue("prompt", out var prompt);
        authorizeRequest.Prompt = prompt;

        parameters.TryGetValue("max_age", out var maxAge);
        authorizeRequest.MaxAge = maxAge;

        parameters.TryGetValue("ui_locales", out var uiLocales);
        authorizeRequest.UiLocales = uiLocales;

        parameters.TryGetValue("id_token_hint", out var idTokenHint);
        authorizeRequest.IdTokenHint = idTokenHint;

        parameters.TryGetValue("login_hint", out var loginHint);
        authorizeRequest.LoginHint = loginHint;

        parameters.TryGetValue("acr_values", out var acrValues);
        authorizeRequest.AcrValues = acrValues;

        parameters.TryGetValue("domain_hint", out var domainHint);
        authorizeRequest.DomainHint = domainHint;

        // Custom extensions
        parameters.TryGetValue("ui_mode", out var uiMode);
        authorizeRequest.UiMode = uiMode;

        parameters.TryGetValue("policy", out var policy);
        authorizeRequest.Policy = policy;

        parameters.TryGetValue("display", out var display);
        authorizeRequest.Display = display;

        // 7. Request objects (JAR)
        parameters.TryGetValue("request", out var requestObject);
        authorizeRequest.Request = requestObject;

        parameters.TryGetValue("request_uri", out var requestUri);
        authorizeRequest.RequestUri = requestUri;

        if (client.RequireRequestObject && string.IsNullOrEmpty(requestObject) && string.IsNullOrEmpty(requestUri))
        {
            return AuthorizeValidationResult.Failure(
                OidcConstants.Errors.InvalidRequest,
                "request or request_uri is required for this client",
                redirectUriValidated: true,
                validatedRedirectUri: validatedRedirectUri);
        }

        // 8. Resource indicators (RFC 8707)
        if (parameters.TryGetValue("resource", out var resource) && !string.IsNullOrEmpty(resource))
        {
            authorizeRequest.Resource = resource.Split(' ', StringSplitOptions.RemoveEmptyEntries).ToList();
        }

        // Validate resource parameter based on tenant settings
        var resourceValidation = await ValidateResourceParameterAsync(
            authorizeRequest.Resource,
            protocolSettings,
            cancellationToken);
        if (!resourceValidation.IsValid)
        {
            return AuthorizeValidationResult.Failure(
                resourceValidation.Error!,
                resourceValidation.ErrorDescription,
                redirectUriValidated: true,
                validatedRedirectUri: validatedRedirectUri);
        }

        return AuthorizeValidationResult.Success(authorizeRequest, client, scopeValidation.ValidScopes);
    }

    /// <summary>
    /// Validates an already-parsed authorize request (for PAR)
    /// </summary>
    public async Task<AuthorizeValidationResult> ValidateAsync(
        AuthorizeRequest authorizeRequest,
        CancellationToken cancellationToken = default)
    {
        // 1. Validate client_id (required)
        if (string.IsNullOrEmpty(authorizeRequest.ClientId))
        {
            return AuthorizeValidationResult.Failure(
                OidcConstants.Errors.InvalidRequest,
                "client_id is required");
        }

        // Find client
        var client = await _clientStore.FindClientByIdAsync(authorizeRequest.ClientId, cancellationToken);
        if (client == null)
        {
            return AuthorizeValidationResult.Failure(
                OidcConstants.Errors.InvalidClient,
                "Unknown client");
        }

        if (!client.Enabled)
        {
            return AuthorizeValidationResult.Failure(
                OidcConstants.Errors.InvalidClient,
                "Client is disabled");
        }

        // Get tenant protocol settings for enforcement
        var protocolSettings = await _tenantSettingsProvider.GetProtocolSettingsAsync(cancellationToken);

        // 2. Validate redirect_uri
        var redirectUri = authorizeRequest.RedirectUri;
        var allowedRedirectUris = client.RedirectUris.Select(r => r.RedirectUri).ToList();

        // If only one redirect URI is registered and none provided, use it
        if (string.IsNullOrEmpty(redirectUri) && allowedRedirectUris.Count == 1)
        {
            redirectUri = allowedRedirectUris.First();
            authorizeRequest.RedirectUri = redirectUri;
        }

        // 3. Validate response_type (required)
        if (string.IsNullOrEmpty(authorizeRequest.ResponseType))
        {
            return AuthorizeValidationResult.Failure(
                OidcConstants.Errors.InvalidRequest,
                "response_type is required");
        }
        authorizeRequest.RequestedResponseTypes = authorizeRequest.ResponseType.Split(' ', StringSplitOptions.RemoveEmptyEntries).ToList();

        // Validate response type is allowed by tenant
        if (protocolSettings.AllowedResponseTypes?.Count > 0)
        {
            var tenantAllowedResponseTypes = new HashSet<string>(protocolSettings.AllowedResponseTypes);
            foreach (var rt in authorizeRequest.RequestedResponseTypes)
            {
                if (!tenantAllowedResponseTypes.Contains(rt))
                {
                    return AuthorizeValidationResult.Failure(
                        OidcConstants.Errors.UnsupportedResponseType,
                        $"Response type '{rt}' is not allowed for this tenant");
                }
            }
        }

        // Validate response type is allowed based on client's grant types and tenant-level restrictions
        var responseTypeValidation = ValidateResponseTypeForClient(
            authorizeRequest.RequestedResponseTypes,
            client.AllowedGrantTypes.Select(g => g.GrantType).ToList(),
            protocolSettings.AllowedGrantTypes);
        if (!responseTypeValidation.IsValid)
        {
            return AuthorizeValidationResult.Failure(
                responseTypeValidation.Error!,
                responseTypeValidation.ErrorDescription);
        }

        // Validate response type is allowed for client
        var isImplicitOrHybrid = authorizeRequest.RequestedResponseTypes.Any(rt =>
            rt == ResponseTypes.Token || rt == ResponseTypes.IdToken);

        var redirectValidation = await _redirectUriValidator.ValidateAsync(
            redirectUri,
            allowedRedirectUris,
            isImplicitOrHybrid);

        if (!redirectValidation.IsValid)
        {
            return AuthorizeValidationResult.Failure(
                redirectValidation.Error!,
                redirectValidation.ErrorDescription);
        }

        // At this point, redirect_uri is validated - errors from here can be redirected to client
        var validatedRedirectUriPar = redirectUri;

        // 4. Validate scope
        var requestedScopes = _scopeValidator.ParseScopes(authorizeRequest.Scope).ToList();
        authorizeRequest.RequestedScopes = requestedScopes;

        var allowedScopes = client.AllowedScopes.Select(s => s.Scope).ToList();
        var scopeValidation = await _scopeValidator.ValidateAsync(requestedScopes, allowedScopes, cancellationToken);
        if (!scopeValidation.IsValid)
        {
            return AuthorizeValidationResult.Failure(
                scopeValidation.Error!,
                scopeValidation.ErrorDescription,
                redirectUriValidated: true,
                validatedRedirectUri: validatedRedirectUriPar);
        }

        // 5. Validate PKCE
        var codeChallenge = authorizeRequest.CodeChallenge;
        var codeChallengeMethod = authorizeRequest.CodeChallengeMethod ?? CodeChallengeMethods.Plain;
        authorizeRequest.CodeChallengeMethod = codeChallengeMethod;

        // PKCE is required if tenant or client requires it
        var isCodeFlow = authorizeRequest.RequestedResponseTypes.Contains(ResponseTypes.Code);
        var requirePkce = isCodeFlow && (protocolSettings.RequirePkce || client.RequirePkce);
        // Tenant can disallow plain PKCE even if client allows it
        var allowPlainPkce = client.AllowPlainTextPkce && protocolSettings.AllowPlainPkce;
        var pkceValidation = _pkceValidator.ValidateCodeChallenge(
            codeChallenge,
            codeChallengeMethod,
            requirePkce,
            allowPlainPkce);

        if (!pkceValidation.IsValid)
        {
            return AuthorizeValidationResult.Failure(
                pkceValidation.Error!,
                pkceValidation.ErrorDescription,
                redirectUriValidated: true,
                validatedRedirectUri: validatedRedirectUriPar);
        }

        // 6. Nonce is required for implicit flow with id_token
        if (authorizeRequest.RequestedResponseTypes.Contains(ResponseTypes.IdToken) && string.IsNullOrEmpty(authorizeRequest.Nonce))
        {
            return AuthorizeValidationResult.Failure(
                OidcConstants.Errors.InvalidRequest,
                "nonce is required for implicit flow with id_token",
                redirectUriValidated: true,
                validatedRedirectUri: validatedRedirectUriPar);
        }

        // 7. Request objects (JAR) - for PAR, request_uri is not allowed (checked in controller)
        if (client.RequireRequestObject && string.IsNullOrEmpty(authorizeRequest.Request) && string.IsNullOrEmpty(authorizeRequest.RequestUri))
        {
            return AuthorizeValidationResult.Failure(
                OidcConstants.Errors.InvalidRequest,
                "request or request_uri is required for this client",
                redirectUriValidated: true,
                validatedRedirectUri: validatedRedirectUriPar);
        }

        // 8. Validate resource parameter (RFC 8707)
        var resourceValidation = await ValidateResourceParameterAsync(
            authorizeRequest.Resource,
            protocolSettings,
            cancellationToken);
        if (!resourceValidation.IsValid)
        {
            return AuthorizeValidationResult.Failure(
                resourceValidation.Error!,
                resourceValidation.ErrorDescription,
                redirectUriValidated: true,
                validatedRedirectUri: validatedRedirectUriPar);
        }

        return AuthorizeValidationResult.Success(authorizeRequest, client, scopeValidation.ValidScopes);
    }

    private static async Task<Dictionary<string, string>> GetParametersAsync(HttpRequest request)
    {
        var parameters = new Dictionary<string, string>(StringComparer.Ordinal);

        // From query string
        foreach (var param in request.Query)
        {
            parameters[param.Key] = param.Value.ToString();
        }

        // From form (POST) - overrides query
        if (request.HasFormContentType)
        {
            var form = await request.ReadFormAsync();
            foreach (var param in form)
            {
                parameters[param.Key] = param.Value.ToString();
            }
        }

        return parameters;
    }

    /// <summary>
    /// Validates that the requested response type is compatible with both the client's and tenant's allowed grant types.
    /// Maps response types to required grant types per OAuth 2.0 and OpenID Connect specs.
    /// </summary>
    private static AuthorizeValidationResult ValidateResponseTypeForClient(
        ICollection<string> responseTypes,
        ICollection<string> clientAllowedGrantTypes,
        ICollection<string>? tenantAllowedGrantTypes)
    {
        var hasCode = responseTypes.Contains(ResponseTypes.Code);
        var hasToken = responseTypes.Contains(ResponseTypes.Token);
        var hasIdToken = responseTypes.Contains(ResponseTypes.IdToken);

        // Determine required grant type based on response type combination
        // See: https://openid.net/specs/openid-connect-core-1_0.html#Authentication
        string requiredGrantType;
        string flowName;

        if (hasCode && (hasToken || hasIdToken))
        {
            // Hybrid flow: code + (token and/or id_token)
            requiredGrantType = OidcConstants.GrantTypes.Hybrid;
            flowName = "hybrid flow";
        }
        else if (hasCode)
        {
            // Authorization code flow: code only
            requiredGrantType = OidcConstants.GrantTypes.AuthorizationCode;
            flowName = "authorization_code grant";
        }
        else if (hasToken || hasIdToken)
        {
            // Implicit flow: token and/or id_token (without code)
            requiredGrantType = OidcConstants.GrantTypes.Implicit;
            flowName = "implicit grant";
        }
        else
        {
            // Unknown/unsupported response type
            return AuthorizeValidationResult.Failure(
                OidcConstants.Errors.UnsupportedResponseType,
                "Unsupported response_type");
        }

        // Check tenant-level grant type restrictions first
        if (tenantAllowedGrantTypes?.Count > 0 && !tenantAllowedGrantTypes.Contains(requiredGrantType))
        {
            return AuthorizeValidationResult.Failure(
                OidcConstants.Errors.UnauthorizedClient,
                $"Grant type '{requiredGrantType}' is not allowed for this tenant");
        }

        // Check client-level grant type restrictions
        if (!clientAllowedGrantTypes.Contains(requiredGrantType))
        {
            return AuthorizeValidationResult.Failure(
                OidcConstants.Errors.UnauthorizedClient,
                $"Client is not authorized for {flowName}");
        }

        return AuthorizeValidationResult.Success(null!, null!, Array.Empty<string>());
    }

    /// <summary>
    /// Validates the resource parameter per RFC 8707 based on tenant settings.
    /// </summary>
    private async Task<ResourceValidationResult> ValidateResourceParameterAsync(
        ICollection<string> resources,
        TenantProtocolSettings protocolSettings,
        CancellationToken cancellationToken)
    {
        var hasResources = resources != null && resources.Count > 0;

        // Check if resource is required
        if (protocolSettings.ResourceValidationMode == ResourceValidationMode.Strict ||
            protocolSettings.RequireResourceParameter)
        {
            if (!hasResources)
            {
                return ResourceValidationResult.Failure(
                    OidcConstants.Errors.InvalidRequest,
                    "The resource parameter is required");
            }
        }

        // If no resources provided, nothing to validate
        if (!hasResources)
        {
            return ResourceValidationResult.Success();
        }

        // Validate each resource URI format per RFC 8707
        foreach (var resourceUri in resources)
        {
            // RFC 8707: Resource indicators MUST be absolute URIs
            if (!Uri.TryCreate(resourceUri, UriKind.Absolute, out var uri))
            {
                return ResourceValidationResult.Failure(
                    OidcConstants.Errors.InvalidTarget,
                    $"Invalid resource URI: '{resourceUri}'. Must be an absolute URI.");
            }

            // RFC 8707: Resource indicators MUST NOT contain a fragment component
            if (!string.IsNullOrEmpty(uri.Fragment))
            {
                return ResourceValidationResult.Failure(
                    OidcConstants.Errors.InvalidTarget,
                    $"Invalid resource URI: '{resourceUri}'. Fragment component is not allowed.");
            }

            // Only HTTP(S) schemes are allowed for security
            if (uri.Scheme != "https" && uri.Scheme != "http")
            {
                return ResourceValidationResult.Failure(
                    OidcConstants.Errors.InvalidTarget,
                    $"Invalid resource URI: '{resourceUri}'. Only HTTP(S) schemes are allowed.");
            }
        }

        // Validate against registered resources if required
        if (protocolSettings.ResourceValidationMode == ResourceValidationMode.ValidateAgainstRegistered ||
            protocolSettings.ResourceValidationMode == ResourceValidationMode.Strict)
        {
            var registeredResources = await _resourceStore.FindResourcesByUrisAsync(resources, cancellationToken);
            var registeredUris = new HashSet<string>(registeredResources.Select(r => r.Uri), StringComparer.OrdinalIgnoreCase);

            foreach (var resourceUri in resources)
            {
                if (!registeredUris.Contains(resourceUri))
                {
                    return ResourceValidationResult.Failure(
                        OidcConstants.Errors.InvalidTarget,
                        $"Unknown resource: '{resourceUri}'. Resource is not registered.");
                }

                // Check if the registered resource is enabled
                var resource = registeredResources.FirstOrDefault(r =>
                    string.Equals(r.Uri, resourceUri, StringComparison.OrdinalIgnoreCase));
                if (resource != null && !resource.Enabled)
                {
                    return ResourceValidationResult.Failure(
                        OidcConstants.Errors.InvalidTarget,
                        $"Resource '{resourceUri}' is disabled.");
                }
            }
        }

        return ResourceValidationResult.Success();
    }
}

/// <summary>
/// Result of resource parameter validation
/// </summary>
internal class ResourceValidationResult
{
    public bool IsValid { get; private set; }
    public string? Error { get; private set; }
    public string? ErrorDescription { get; private set; }

    public static ResourceValidationResult Success() => new() { IsValid = true };

    public static ResourceValidationResult Failure(string error, string? description = null) => new()
    {
        IsValid = false,
        Error = error,
        ErrorDescription = description
    };
}
