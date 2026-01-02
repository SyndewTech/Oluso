using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Logging;
using Oluso.Core.Protocols;
using Oluso.Core.Services;
using Oluso.Enterprise.Saml.Configuration;
using Oluso.Enterprise.Saml.IdentityProvider;
using Oluso.Enterprise.Saml.ServiceProvider;

namespace Oluso.Enterprise.Saml.Protocol;

/// <summary>
/// SAML protocol service implementation for the protocol pattern.
/// Bridges SAML operations to the core protocol abstractions.
/// </summary>
public class SamlProtocolService : ISamlProtocolService
{
    private readonly ISamlServiceProvider _serviceProvider;
    private readonly ISamlIdentityProvider _identityProvider;
    private readonly IOlusoUserService _userService;
    private readonly IProtocolStateStore _stateStore;
    private readonly ILogger<SamlProtocolService> _logger;

    public SamlProtocolService(
        ISamlServiceProvider serviceProvider,
        ISamlIdentityProvider identityProvider,
        IOlusoUserService userService,
        IProtocolStateStore stateStore,
        ILogger<SamlProtocolService> logger)
    {
        _serviceProvider = serviceProvider;
        _identityProvider = identityProvider;
        _userService = userService;
        _stateStore = stateStore;
        _logger = logger;
    }

    /// <inheritdoc />
    public string ProtocolName => "saml";

    /// <inheritdoc />
    public async Task<ProtocolContext> BuildContextFromRequestAsync(
        HttpContext httpContext,
        CancellationToken cancellationToken = default)
    {
        var context = new ProtocolContext
        {
            HttpContext = httpContext,
            ProtocolName = ProtocolName,
            EndpointType = EndpointType.Authorize,
            CorrelationId = Guid.NewGuid().ToString("N")
        };

        // Extract SAML request from query or form
        string? samlRequest = null;
        string? relayState = null;

        if (httpContext.Request.Method == "GET")
        {
            samlRequest = httpContext.Request.Query["SAMLRequest"].FirstOrDefault();
            relayState = httpContext.Request.Query["RelayState"].FirstOrDefault();
        }
        else if (httpContext.Request.Method == "POST" && httpContext.Request.HasFormContentType)
        {
            var form = await httpContext.Request.ReadFormAsync(cancellationToken);
            samlRequest = form["SAMLRequest"].FirstOrDefault();
            relayState = form["RelayState"].FirstOrDefault();
        }

        if (!string.IsNullOrEmpty(samlRequest))
        {
            // Parse the AuthnRequest
            var result = await _identityProvider.ParseAuthnRequestAsync(samlRequest, relayState, cancellationToken);

            if (result.Valid)
            {
                context.Request = new SamlProtocolRequest
                {
                    RequestId = result.Id,
                    Issuer = result.Issuer,
                    AssertionConsumerServiceUrl = result.AssertionConsumerServiceUrl,
                    RelayState = relayState,
                    NameIdFormat = result.NameIdFormat,
                    ForceAuthn = result.ForceAuthn,
                    IsPassive = result.IsPassive
                };

                // Store in properties for later use
                context.Properties["saml_request_id"] = result.Id ?? "";
                context.Properties["saml_sp_issuer"] = result.Issuer ?? "";
                context.Properties["saml_acs_url"] = result.AssertionConsumerServiceUrl ?? "";
                context.Properties["saml_relay_state"] = relayState ?? "";
                context.Properties["saml_name_id_format"] = result.NameIdFormat ?? "";
            }
            else
            {
                context.Properties["saml_error"] = result.Error ?? "Invalid SAML request";
            }
        }

        return context;
    }

    /// <inheritdoc />
    public async Task<ProtocolResult> ProcessAuthnRequestAsync(
        ProtocolContext context,
        CancellationToken cancellationToken = default)
    {
        if (context.Request is not SamlProtocolRequest samlRequest)
        {
            return ProtocolResult.Failed("invalid_request", "No SAML request in context");
        }

        if (context.Properties.TryGetValue("saml_error", out var error))
        {
            return ProtocolResult.Failed("invalid_request", error?.ToString());
        }

        _logger.LogDebug("Processing SAML AuthnRequest from {Issuer}", samlRequest.Issuer);

        // Store state for after authentication
        var state = new ProtocolState
        {
            ProtocolName = ProtocolName,
            SerializedRequest = System.Text.Json.JsonSerializer.Serialize(samlRequest),
            ClientId = samlRequest.Issuer ?? "",
            EndpointType = EndpointType.Authorize,
            Properties = new Dictionary<string, string>
            {
                ["request_id"] = samlRequest.RequestId ?? "",
                ["sp_issuer"] = samlRequest.Issuer ?? "",
                ["acs_url"] = samlRequest.AssertionConsumerServiceUrl ?? "",
                ["relay_state"] = samlRequest.RelayState ?? "",
                ["name_id_format"] = samlRequest.NameIdFormat ?? ""
            }
        };
        var stateKey = await _stateStore.StoreAsync(state, TimeSpan.FromMinutes(10), cancellationToken);

        // Build auth requirement
        var authRequirement = new AuthenticationRequirement
        {
            SuggestedPolicyType = Core.UserJourneys.JourneyType.SignIn,
            Prompt = samlRequest.ForceAuthn ? "login" : null
        };

        return ProtocolResult.RequiresAuth(authRequirement, stateKey);
    }

    /// <inheritdoc />
    public async Task<IActionResult> BuildSamlResponseAsync(
        ProtocolContext context,
        AuthenticationResult authResult,
        CancellationToken cancellationToken = default)
    {
        if (!authResult.Succeeded || string.IsNullOrEmpty(authResult.UserId))
        {
            return await BuildErrorResponseAsync(context, "authentication_failed", "User authentication failed", cancellationToken);
        }

        // Get SAML request details from context or state
        var spIssuer = GetPropertyString(context, "saml_sp_issuer");
        var acsUrl = GetPropertyString(context, "saml_acs_url");
        var requestId = GetPropertyString(context, "saml_request_id");
        var relayState = GetPropertyString(context, "saml_relay_state");
        var nameIdFormat = GetPropertyString(context, "saml_name_id_format");

        if (string.IsNullOrEmpty(spIssuer) || string.IsNullOrEmpty(acsUrl))
        {
            _logger.LogError("Missing SP issuer or ACS URL in context");
            return new BadRequestObjectResult(new { error = "invalid_state", error_description = "Missing SAML request context" });
        }

        // Get user claims
        var userClaims = await _userService.GetClaimsAsync(authResult.UserId, cancellationToken);

        // Determine name ID based on format
        var nameId = DetermineNameId(nameIdFormat, authResult.UserId, authResult.Claims);

        // Create SAML response
        var response = await _identityProvider.CreateResponseAsync(new SamlAssertionParams
        {
            SpEntityId = spIssuer,
            SubjectId = nameId,
            Claims = userClaims,
            NameIdFormat = nameIdFormat,
            InResponseTo = requestId,
            Destination = acsUrl,
            SessionIndex = authResult.SessionId ?? Guid.NewGuid().ToString()
        }, relayState, cancellationToken);

        _logger.LogInformation("Issued SAML assertion for user {UserId} to SP {SpEntityId}", authResult.UserId, spIssuer);

        // Return auto-post form
        return new ContentResult
        {
            Content = GenerateAutoPostForm(response),
            ContentType = "text/html",
            StatusCode = 200
        };
    }

    /// <inheritdoc />
    public async Task<IActionResult> BuildAuthenticatedResponseAsync(
        ProtocolContext context,
        AuthenticationResult authResult,
        CancellationToken cancellationToken = default)
    {
        return await BuildSamlResponseAsync(context, authResult, cancellationToken);
    }

    /// <inheritdoc />
    public IActionResult BuildErrorResponse(ProtocolContext? context, ProtocolError error)
    {
        // If we have SP info, send a SAML error response
        if (context != null)
        {
            var spIssuer = GetPropertyString(context, "saml_sp_issuer");
            var acsUrl = GetPropertyString(context, "saml_acs_url");

            if (!string.IsNullOrEmpty(spIssuer) && !string.IsNullOrEmpty(acsUrl))
            {
                // Create error response synchronously (blocking call)
                var requestId = GetPropertyString(context, "saml_request_id");
                var errorResponse = _identityProvider.CreateErrorResponseAsync(
                    spIssuer,
                    requestId,
                    MapToSamlStatusCode(error.Code),
                    error.Description).GetAwaiter().GetResult();

                return new ContentResult
                {
                    Content = GenerateAutoPostForm(errorResponse),
                    ContentType = "text/html",
                    StatusCode = 200
                };
            }
        }

        // Fall back to JSON error
        return new BadRequestObjectResult(new
        {
            error = error.Code,
            error_description = error.Description
        });
    }

    /// <inheritdoc />
    public async Task<IActionResult> InitiateExternalAuthAsync(
        string idpName,
        string returnUrl,
        string correlationId,
        CancellationToken cancellationToken = default)
    {
        var idps = await _serviceProvider.GetConfiguredIdpsAsync(cancellationToken);
        var idp = idps.FirstOrDefault(i => i.Name.Equals(idpName, StringComparison.OrdinalIgnoreCase));

        if (idp == null || !idp.Enabled)
        {
            return new NotFoundObjectResult(new { error = "idp_not_found", error_description = $"IdP '{idpName}' not found or not enabled" });
        }

        // Store return URL in state
        var state = new ProtocolState
        {
            ProtocolName = "saml_external",
            SerializedRequest = "",
            ClientId = idpName,
            EndpointType = EndpointType.Authorize,
            Properties = new Dictionary<string, string>
            {
                ["return_url"] = returnUrl,
                ["idp_name"] = idpName
            }
        };
        var stateCorrelationId = await _stateStore.StoreAsync(state, TimeSpan.FromMinutes(10), cancellationToken);

        // Create AuthnRequest
        var authnRequest = await _serviceProvider.CreateAuthnRequestAsync(new SamlAuthnRequestParams
        {
            IdpName = idpName,
            RelayState = stateCorrelationId
        }, cancellationToken);

        _logger.LogDebug("Initiating SAML authentication with IdP {IdpName}", idpName);

        // Redirect based on binding
        if (authnRequest.Binding == "POST")
        {
            return new ContentResult
            {
                Content = GenerateAuthnRequestPostForm(authnRequest),
                ContentType = "text/html",
                StatusCode = 200
            };
        }

        // Redirect binding
        var redirectUrl = authnRequest.Url;
        if (!string.IsNullOrEmpty(authnRequest.SamlRequest))
        {
            redirectUrl += (authnRequest.Url.Contains('?') ? "&" : "?") +
                          $"SAMLRequest={Uri.EscapeDataString(authnRequest.SamlRequest)}";
            if (!string.IsNullOrEmpty(authnRequest.RelayState))
            {
                redirectUrl += $"&RelayState={Uri.EscapeDataString(authnRequest.RelayState)}";
            }
        }

        return new RedirectResult(redirectUrl);
    }

    /// <inheritdoc />
    public async Task<SamlExternalAuthResult> ProcessExternalResponseAsync(
        HttpContext httpContext,
        CancellationToken cancellationToken = default)
    {
        string? samlResponse = null;
        string? relayState = null;

        if (httpContext.Request.Method == "POST" && httpContext.Request.HasFormContentType)
        {
            var form = await httpContext.Request.ReadFormAsync(cancellationToken);
            samlResponse = form["SAMLResponse"].FirstOrDefault();
            relayState = form["RelayState"].FirstOrDefault();
        }
        else if (httpContext.Request.Method == "GET")
        {
            samlResponse = httpContext.Request.Query["SAMLResponse"].FirstOrDefault();
            relayState = httpContext.Request.Query["RelayState"].FirstOrDefault();
        }

        if (string.IsNullOrEmpty(samlResponse))
        {
            return SamlExternalAuthResult.Failed("invalid_response", "No SAML response received");
        }

        // Process the SAML response
        var result = await _serviceProvider.ProcessResponseAsync(httpContext, samlResponse, relayState, cancellationToken);

        if (!result.Succeeded)
        {
            _logger.LogWarning("SAML response validation failed: {Error}", result.Error);
            return SamlExternalAuthResult.Failed("validation_failed", result.Error);
        }

        // Get IdP name from stored state
        string? idpName = null;
        if (!string.IsNullOrEmpty(relayState))
        {
            var storedState = await _stateStore.GetAsync(relayState, cancellationToken);
            if (storedState?.Properties?.TryGetValue("idp_name", out var storedIdpName) == true)
            {
                idpName = storedIdpName;
            }
        }

        _logger.LogInformation("Successfully processed SAML response for subject {SubjectId} from IdP {IdpName}",
            result.SubjectId, idpName);

        return SamlExternalAuthResult.Success(
            result.SubjectId!,
            idpName ?? "unknown",
            result.Principal!,
            result.SessionIndex,
            relayState);
    }

    private async Task<IActionResult> BuildErrorResponseAsync(
        ProtocolContext context,
        string error,
        string? description,
        CancellationToken cancellationToken)
    {
        var spIssuer = GetPropertyString(context, "saml_sp_issuer");
        var requestId = GetPropertyString(context, "saml_request_id");

        if (!string.IsNullOrEmpty(spIssuer))
        {
            var errorResponse = await _identityProvider.CreateErrorResponseAsync(
                spIssuer,
                requestId,
                MapToSamlStatusCode(error),
                description,
                cancellationToken);

            return new ContentResult
            {
                Content = GenerateAutoPostForm(errorResponse),
                ContentType = "text/html",
                StatusCode = 200
            };
        }

        return new BadRequestObjectResult(new { error, error_description = description });
    }

    private static string GetPropertyString(ProtocolContext context, string key)
    {
        return context.Properties.TryGetValue(key, out var value) ? value?.ToString() ?? "" : "";
    }

    private static string DetermineNameId(string? nameIdFormat, string userId, IDictionary<string, object>? claims)
    {
        return nameIdFormat switch
        {
            "urn:oasis:names:tc:SAML:1.1:nameid-format:emailAddress" =>
                (claims != null && claims.TryGetValue("email", out var emailValue) ? emailValue?.ToString() : null) ?? userId,
            "urn:oasis:names:tc:SAML:2.0:nameid-format:transient" =>
                Guid.NewGuid().ToString(),
            _ => userId
        };
    }

    private static string MapToSamlStatusCode(string error)
    {
        return error switch
        {
            "authentication_failed" => "urn:oasis:names:tc:SAML:2.0:status:AuthnFailed",
            "access_denied" => "urn:oasis:names:tc:SAML:2.0:status:RequestDenied",
            "invalid_request" => "urn:oasis:names:tc:SAML:2.0:status:RequestUnsupported",
            _ => "urn:oasis:names:tc:SAML:2.0:status:Responder"
        };
    }

    private static string GenerateAutoPostForm(SamlResponseResult response)
    {
        return $"""
            <!DOCTYPE html>
            <html>
            <head>
                <title>SAML Response</title>
            </head>
            <body onload="document.forms[0].submit()">
                <noscript>
                    <p>JavaScript is disabled. Please click the button below to continue.</p>
                </noscript>
                <form method="post" action="{System.Web.HttpUtility.HtmlEncode(response.Destination)}">
                    <input type="hidden" name="SAMLResponse" value="{System.Web.HttpUtility.HtmlEncode(response.SamlResponse)}" />
                    {(string.IsNullOrEmpty(response.RelayState) ? "" : $@"<input type=""hidden"" name=""RelayState"" value=""{System.Web.HttpUtility.HtmlEncode(response.RelayState)}"" />")}
                    <noscript>
                        <button type="submit">Continue</button>
                    </noscript>
                </form>
            </body>
            </html>
            """;
    }

    private static string GenerateAuthnRequestPostForm(SamlAuthnRequest request)
    {
        return $"""
            <!DOCTYPE html>
            <html>
            <head>
                <title>SAML Authentication</title>
            </head>
            <body onload="document.forms[0].submit()">
                <noscript>
                    <p>JavaScript is disabled. Please click the button below to continue.</p>
                </noscript>
                <form method="post" action="{System.Web.HttpUtility.HtmlEncode(request.Url)}">
                    <input type="hidden" name="SAMLRequest" value="{System.Web.HttpUtility.HtmlEncode(request.SamlRequest)}" />
                    {(string.IsNullOrEmpty(request.RelayState) ? "" : $@"<input type=""hidden"" name=""RelayState"" value=""{System.Web.HttpUtility.HtmlEncode(request.RelayState)}"" />")}
                    <noscript>
                        <button type="submit">Continue</button>
                    </noscript>
                </form>
            </body>
            </html>
            """;
    }
}

/// <summary>
/// SAML protocol request
/// </summary>
public class SamlProtocolRequest : IProtocolRequest
{
    public string? RequestId { get; init; }
    public string? Issuer { get; init; }
    public string? AssertionConsumerServiceUrl { get; init; }
    public string? RelayState { get; init; }
    public string? NameIdFormat { get; init; }
    public bool ForceAuthn { get; init; }
    public bool IsPassive { get; init; }

    // IProtocolRequest implementation
    public string? ClientId => Issuer;
    public string? PolicyId => null;
    public string? UiMode => null;
}

/// <summary>
/// Protocol processing result with SAML-specific extensions
/// </summary>
public class ProtocolResult
{
    public bool RequiresAuthentication { get; init; }
    public bool Succeeded { get; init; }
    public AuthenticationRequirement? AuthRequirement { get; init; }
    public string? CorrelationId { get; init; }
    public string? Error { get; init; }
    public string? ErrorDescription { get; init; }

    public static ProtocolResult RequiresAuth(AuthenticationRequirement requirement, string correlationId) => new()
    {
        RequiresAuthentication = true,
        AuthRequirement = requirement,
        CorrelationId = correlationId
    };

    public static ProtocolResult Success() => new()
    {
        Succeeded = true
    };

    public static ProtocolResult Failed(string error, string? description = null) => new()
    {
        Succeeded = false,
        Error = error,
        ErrorDescription = description
    };
}
