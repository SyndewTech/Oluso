using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Logging;
using Oluso.Core.Domain.Interfaces;
using Oluso.Core.Events;
using Oluso.Core.Services;
using Oluso.Enterprise.Saml.IdentityProvider;
using Oluso.Enterprise.Saml.Services;

namespace Oluso.Enterprise.Saml.Endpoints;

/// <summary>
/// SAML Identity Provider endpoints.
/// Handles SSO, SLO, and metadata when Oluso acts as an IdP.
/// </summary>
[ApiController]
[Route("saml/idp")]
public class SamlIdpController : ControllerBase
{
    private readonly ISamlIdentityProvider _samlIdp;
    private readonly IOlusoUserService _userService;
    private readonly IOlusoEventService _eventService;
    private readonly ITenantContext _tenantContext;
    private readonly ISamlTenantSettingsService _samlSettings;
    private readonly ILogger<SamlIdpController> _logger;

    public SamlIdpController(
        ISamlIdentityProvider samlIdp,
        IOlusoUserService userService,
        IOlusoEventService eventService,
        ITenantContext tenantContext,
        ISamlTenantSettingsService samlSettings,
        ILogger<SamlIdpController> logger)
    {
        _samlIdp = samlIdp;
        _userService = userService;
        _eventService = eventService;
        _tenantContext = tenantContext;
        _samlSettings = samlSettings;
        _logger = logger;
    }

    /// <summary>
    /// IdP Metadata endpoint
    /// </summary>
    [HttpGet("metadata")]
    public async Task<IActionResult> Metadata(CancellationToken cancellationToken)
    {
        if (!await IsIdpEnabledAsync(cancellationToken))
        {
            return NotFound(new { error = "idp_not_enabled", error_description = "SAML IdP functionality is not enabled" });
        }

        // Use tenant-specific metadata if tenant is present
        if (_tenantContext.TenantId != null)
        {
            var tenantMetadata = await _samlIdp.GenerateMetadataForTenantAsync(_tenantContext.TenantId, cancellationToken);
            return Content(tenantMetadata, "application/xml");
        }

        var metadata = await _samlIdp.GenerateMetadataAsync(cancellationToken);
        return Content(metadata, "application/xml");
    }

    /// <summary>
    /// Single Sign-On endpoint (receives AuthnRequests)
    /// Supports both GET (Redirect binding) and POST bindings
    /// </summary>
    [HttpGet("sso")]
    [HttpPost("sso")]
    public async Task<IActionResult> SingleSignOn(
        [FromQuery(Name = "SAMLRequest")] string? samlRequestQuery,
        [FromForm(Name = "SAMLRequest")] string? samlRequestForm,
        [FromQuery(Name = "RelayState")] string? relayStateQuery,
        [FromForm(Name = "RelayState")] string? relayStateForm,
        CancellationToken cancellationToken)
    {
        if (!await IsIdpEnabledAsync(cancellationToken))
        {
            return NotFound(new { error = "idp_not_enabled" });
        }

        var samlRequest = samlRequestQuery ?? samlRequestForm;
        var relayState = relayStateQuery ?? relayStateForm;
        var isPostBinding = HttpContext.Request.Method.Equals("POST", StringComparison.OrdinalIgnoreCase);

        if (string.IsNullOrEmpty(samlRequest))
        {
            return BadRequest(new { error = "invalid_request", error_description = "Missing SAMLRequest" });
        }

        // Parse the AuthnRequest (detect binding from HTTP method)
        var result = await _samlIdp.ParseAuthnRequestAsync(samlRequest, relayState, isPostBinding, cancellationToken);

        if (!result.Valid)
        {
            _logger.LogWarning("Invalid SAML AuthnRequest: {Error}", result.Error);

            await _eventService.RaiseAsync(new SamlSsoFailedEvent
            {
                TenantId = _tenantContext.TenantId,
                SpEntityId = result.Issuer,
                Error = "invalid_request",
                ErrorDescription = result.Error
            }, cancellationToken);

            return BadRequest(new { error = "invalid_request", error_description = result.Error });
        }

        _logger.LogDebug("Received SAML AuthnRequest from SP {Issuer}, ID {Id}", result.Issuer, result.Id);

        // Raise SSO request received event
        await _eventService.RaiseAsync(new SamlSsoRequestReceivedEvent
        {
            TenantId = _tenantContext.TenantId,
            SpEntityId = result.Issuer!,
            RequestId = result.Id,
            AcsUrl = result.AssertionConsumerServiceUrl,
            ForceAuthn = result.ForceAuthn
        }, cancellationToken);

        // Store the request info in session/temp data for after authentication
        HttpContext.Session.SetString("saml_authn_request_id", result.Id ?? "");
        HttpContext.Session.SetString("saml_sp_issuer", result.Issuer ?? "");
        HttpContext.Session.SetString("saml_acs_url", result.AssertionConsumerServiceUrl ?? "");
        HttpContext.Session.SetString("saml_relay_state", relayState ?? "");
        HttpContext.Session.SetString("saml_name_id_format", result.NameIdFormat ?? "");

        // Check if user is already authenticated
        if (User.Identity?.IsAuthenticated == true)
        {
            // User is authenticated, issue response directly
            return await IssueResponseAsync(result, cancellationToken);
        }

        // Build return URL for after authentication
        var returnUrl = $"/saml/idp/continue?relay={Uri.EscapeDataString(relayState ?? "")}";

        // Check if tenant uses journey-based login
        var samlIdpSettings = await GetTenantSettingsAsync(cancellationToken);
        string loginUrl;

        if (!string.IsNullOrEmpty(samlIdpSettings.LoginJourneyName))
        {
            // Use journey-based authentication
            loginUrl = $"/journey/{samlIdpSettings.LoginJourneyName}?returnUrl={Uri.EscapeDataString(returnUrl)}";
            _logger.LogDebug("Using journey {JourneyName} for SAML IdP authentication", samlIdpSettings.LoginJourneyName);
        }
        else
        {
            // Use standalone login page
            loginUrl = $"/account/login?returnUrl={Uri.EscapeDataString(returnUrl)}";
        }

        if (result.ForceAuthn)
        {
            loginUrl += "&prompt=login";
        }

        return Redirect(loginUrl);
    }

    /// <summary>
    /// Continue SSO after user authentication
    /// </summary>
    [HttpGet("continue")]
    [Authorize]
    public async Task<IActionResult> ContinueSso(
        [FromQuery] string? relay,
        CancellationToken cancellationToken)
    {
        if (!await IsIdpEnabledAsync(cancellationToken))
        {
            return NotFound(new { error = "idp_not_enabled" });
        }

        // Retrieve stored AuthnRequest info
        var requestId = HttpContext.Session.GetString("saml_authn_request_id");
        var spIssuer = HttpContext.Session.GetString("saml_sp_issuer");
        var acsUrl = HttpContext.Session.GetString("saml_acs_url");
        var relayState = HttpContext.Session.GetString("saml_relay_state") ?? relay;
        var nameIdFormat = HttpContext.Session.GetString("saml_name_id_format");

        if (string.IsNullOrEmpty(spIssuer))
        {
            return BadRequest(new { error = "invalid_session", error_description = "No pending SAML request" });
        }

        var result = new SamlAuthnRequestResult
        {
            Valid = true,
            Id = requestId,
            Issuer = spIssuer,
            AssertionConsumerServiceUrl = acsUrl,
            RelayState = relayState,
            NameIdFormat = nameIdFormat
        };

        return await IssueResponseAsync(result, cancellationToken);
    }

    private async Task<IActionResult> IssueResponseAsync(SamlAuthnRequestResult authnRequest, CancellationToken cancellationToken)
    {
        // Get user info
        var userId = User.FindFirst("sub")?.Value ?? User.FindFirst(System.Security.Claims.ClaimTypes.NameIdentifier)?.Value;
        if (string.IsNullOrEmpty(userId))
        {
            return Unauthorized();
        }

        var userClaims = await _userService.GetClaimsAsync(userId, cancellationToken);

        // Determine name ID based on format
        var nameId = authnRequest.NameIdFormat switch
        {
            "urn:oasis:names:tc:SAML:1.1:nameid-format:emailAddress" =>
                User.FindFirst("email")?.Value ?? User.FindFirst(System.Security.Claims.ClaimTypes.Email)?.Value ?? userId,
            "urn:oasis:names:tc:SAML:2.0:nameid-format:persistent" => userId,
            "urn:oasis:names:tc:SAML:2.0:nameid-format:transient" => Guid.NewGuid().ToString(),
            _ => userId
        };

        // Create SAML response
        var sessionIndex = Guid.NewGuid().ToString();
        var response = await _samlIdp.CreateResponseAsync(new SamlAssertionParams
        {
            SpEntityId = authnRequest.Issuer!,
            SubjectId = nameId,
            Claims = userClaims,
            NameIdFormat = authnRequest.NameIdFormat,
            InResponseTo = authnRequest.Id,
            Destination = authnRequest.AssertionConsumerServiceUrl,
            SessionIndex = sessionIndex
        }, authnRequest.RelayState, cancellationToken);

        _logger.LogInformation("Issued SAML assertion for user {UserId} to SP {SpEntityId}", userId, authnRequest.Issuer);

        // Raise assertion issued event
        await _eventService.RaiseAsync(new SamlAssertionIssuedEvent
        {
            TenantId = _tenantContext.TenantId,
            SubjectId = userId,
            SpEntityId = authnRequest.Issuer!,
            NameId = nameId,
            SessionIndex = sessionIndex
        }, CancellationToken.None);

        // Clear session data
        HttpContext.Session.Remove("saml_authn_request_id");
        HttpContext.Session.Remove("saml_sp_issuer");
        HttpContext.Session.Remove("saml_acs_url");
        HttpContext.Session.Remove("saml_relay_state");
        HttpContext.Session.Remove("saml_name_id_format");

        // Return auto-submit form for POST binding
        return Content(GenerateAutoPostForm(response), "text/html");
    }

    /// <summary>
    /// Single Logout endpoint
    /// </summary>
    [HttpGet("slo")]
    [HttpPost("slo")]
    public async Task<IActionResult> SingleLogout(
        [FromQuery(Name = "SAMLRequest")] string? samlRequestQuery,
        [FromForm(Name = "SAMLRequest")] string? samlRequestForm,
        [FromQuery(Name = "SAMLResponse")] string? samlResponseQuery,
        [FromForm(Name = "SAMLResponse")] string? samlResponseForm,
        [FromQuery(Name = "RelayState")] string? relayStateQuery,
        [FromForm(Name = "RelayState")] string? relayStateForm,
        CancellationToken cancellationToken)
    {
        if (!await IsIdpEnabledAsync(cancellationToken))
        {
            return NotFound(new { error = "idp_not_enabled" });
        }

        var samlRequest = samlRequestQuery ?? samlRequestForm;
        var samlResponse = samlResponseQuery ?? samlResponseForm;
        var relayState = relayStateQuery ?? relayStateForm;

        if (!string.IsNullOrEmpty(samlRequest))
        {
            // SP-initiated logout
            var result = await _samlIdp.ParseLogoutRequestAsync(samlRequest, relayState, cancellationToken);

            if (!result.Valid)
            {
                _logger.LogWarning("Invalid SAML LogoutRequest: {Error}", result.Error);
                return BadRequest(new { error = "invalid_request", error_description = result.Error });
            }

            _logger.LogInformation("Received logout request for {NameId} from SP {Issuer}", result.NameId, result.Issuer);

            // Raise logout request received event
            await _eventService.RaiseAsync(new SamlLogoutRequestReceivedEvent
            {
                TenantId = _tenantContext.TenantId,
                SpEntityId = result.Issuer!,
                NameId = result.NameId,
                SessionIndex = result.SessionIndex
            }, cancellationToken);

            // Perform local logout
            await HttpContext.SignOutAsync(IdentityConstants.ApplicationScheme);

            // Send logout response
            var response = await _samlIdp.CreateLogoutResponseAsync(
                result.Issuer!,
                result.Id,
                success: true,
                cancellationToken);

            // Raise logout completed event
            await _eventService.RaiseAsync(new SamlLogoutCompletedEvent
            {
                TenantId = _tenantContext.TenantId,
                SpEntityId = result.Issuer!,
                SubjectId = User.FindFirst("sub")?.Value,
                Success = true
            }, cancellationToken);

            return Content(GenerateAutoPostForm(response), "text/html");
        }

        if (!string.IsNullOrEmpty(samlResponse))
        {
            // Logout response from SP (IdP-initiated logout)
            _logger.LogInformation("Received logout response");
            return Redirect(relayState ?? "/");
        }

        return BadRequest(new { error = "invalid_request" });
    }

    /// <summary>
    /// Lists registered Service Providers
    /// </summary>
    [HttpGet("sps")]
    public async Task<IActionResult> ListServiceProviders(CancellationToken cancellationToken)
    {
        if (!await IsIdpEnabledAsync(cancellationToken))
        {
            return NotFound(new { error = "idp_not_enabled" });
        }

        var sps = _samlIdp.GetRegisteredServiceProviders()
            .Where(sp => sp.Enabled)
            .Select(sp => new
            {
                entityId = sp.EntityId,
                displayName = sp.DisplayName
            });

        return Ok(sps);
    }

    private async Task<bool> IsIdpEnabledAsync(CancellationToken cancellationToken)
    {
        // Always check tenant settings from database
        var tenantSettings = await GetTenantSettingsAsync(cancellationToken);
        return tenantSettings.Enabled;
    }

    private async Task<Configuration.SamlTenantSettings> GetTenantSettingsAsync(CancellationToken cancellationToken)
    {
        if (_tenantContext.TenantId == null)
        {
            return Configuration.SamlTenantSettings.Default;
        }
        return await _samlSettings.GetSettingsAsync(_tenantContext.TenantId, cancellationToken);
    }

    private async Task SignOutAsync()
    {
        await HttpContext.SignOutAsync(IdentityConstants.ApplicationScheme);
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
}
