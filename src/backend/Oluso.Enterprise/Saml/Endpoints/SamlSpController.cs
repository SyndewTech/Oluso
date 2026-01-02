using System.Collections.Generic;
using System.Security.Claims;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Logging;
using Oluso.Core.Domain.Interfaces;
using Oluso.Core.Protocols;
using Oluso.Enterprise.Saml.ServiceProvider;

namespace Oluso.Enterprise.Saml.Endpoints;

/// <summary>
/// SAML Service Provider endpoints.
/// Handles ACS (Assertion Consumer Service), SLO, and metadata.
/// </summary>
[ApiController]
[Route("saml")]
public class SamlSpController : ControllerBase
{
    private readonly ISamlServiceProvider _samlSp;
    private readonly IAuthenticationCoordinator _coordinator;
    private readonly IProtocolStateStore _stateStore;
    private readonly ITenantContext _tenantContext;
    private readonly ILogger<SamlSpController> _logger;

    public SamlSpController(
        ISamlServiceProvider samlSp,
        IAuthenticationCoordinator coordinator,
        IProtocolStateStore stateStore,
        ITenantContext tenantContext,
        ILogger<SamlSpController> logger)
    {
        _samlSp = samlSp;
        _coordinator = coordinator;
        _stateStore = stateStore;
        _tenantContext = tenantContext;
        _logger = logger;
    }

    /// <summary>
    /// SP Metadata endpoint
    /// </summary>
    [HttpGet("metadata")]
    public async Task<IActionResult> Metadata(CancellationToken cancellationToken)
    {
        var metadata = await _samlSp.GenerateMetadataAsync(cancellationToken);
        return Content(metadata, "application/xml");
    }

    /// <summary>
    /// Initiates SAML authentication with an IdP
    /// </summary>
    [HttpGet("login/{idpName}")]
    public async Task<IActionResult> Login(
        string idpName,
        [FromQuery] string? returnUrl,
        [FromQuery] string? correlationId,
        [FromQuery] bool isRegistration = false,
        CancellationToken cancellationToken = default)
    {
        var idps = await _samlSp.GetConfiguredIdpsAsync(cancellationToken);
        if (!idps.Any(i => i.Name == idpName && i.Enabled))
        {
            return NotFound(new { error = "idp_not_found", error_description = $"SAML IdP '{idpName}' not found or disabled" });
        }

        // Store state for callback
        var stateProperties = new Dictionary<string, string>
        {
            ["saml_idp"] = idpName,
            ["return_url"] = returnUrl ?? "/"
        };

        // Track if this is a registration flow
        if (isRegistration)
        {
            stateProperties["IsRegistration"] = "true";
        }

        var state = new ProtocolState
        {
            ProtocolName = "saml",
            SerializedRequest = "",
            ClientId = "saml-sp",
            EndpointType = EndpointType.Authorize,
            Properties = stateProperties
        };
        correlationId = await _stateStore.StoreAsync(state, TimeSpan.FromMinutes(10), cancellationToken);

        // Create SAML AuthnRequest
        var request = await _samlSp.CreateAuthnRequestAsync(new SamlAuthnRequestParams
        {
            IdpName = idpName,
            ReturnUrl = returnUrl,
            RelayState = correlationId
        }, cancellationToken);

        _logger.LogDebug("Initiating SAML login with IdP {IdpName}, correlation {CorrelationId}", idpName, correlationId);

        // Redirect to IdP
        return Redirect(request.Url);
    }

    /// <summary>
    /// Assertion Consumer Service (ACS) - receives SAML responses.
    /// Signs the user into the external scheme and redirects to the external callback page,
    /// following the same pattern as OAuth providers.
    /// </summary>
    [HttpPost("acs")]
    public async Task<IActionResult> AssertionConsumerService(
        [FromForm(Name = "SAMLResponse")] string samlResponse,
        [FromForm(Name = "RelayState")] string? relayState,
        CancellationToken cancellationToken)
    {
        if (string.IsNullOrEmpty(samlResponse))
        {
            return BadRequest(new { error = "invalid_response", error_description = "Missing SAMLResponse" });
        }

        _logger.LogDebug("Received SAML response, RelayState: {RelayState}", relayState);

        // Validate RelayState is present - required to prevent CSRF and unsolicited responses
        if (string.IsNullOrEmpty(relayState))
        {
            _logger.LogWarning("SAML response received without RelayState - rejecting as potential CSRF or unsolicited response");
            return BadRequest(new { error = "invalid_response", error_description = "Missing RelayState" });
        }

        // Retrieve and validate stored state
        var state = await _stateStore.GetAsync(relayState, cancellationToken);
        if (state == null)
        {
            _logger.LogWarning("SAML response received with invalid or expired RelayState: {RelayState}", relayState);
            return BadRequest(new { error = "invalid_state", error_description = "Invalid or expired state" });
        }

        // Process SAML response
        var result = await _samlSp.ProcessResponseAsync(HttpContext, samlResponse, relayState, cancellationToken);

        if (!result.Succeeded)
        {
            _logger.LogWarning("SAML authentication failed: {Error}", result.Error);
            return RedirectToPage("/Error", new { error = "saml_auth_failed", message = result.Error });
        }

        var returnUrl = state.Properties?.TryGetValue("return_url", out var returnUrlValue) == true ? returnUrlValue : "/";
        var idpName = state.Properties?.TryGetValue("saml_idp", out var idpNameValue) == true ? idpNameValue : "saml";

        _logger.LogInformation("SAML authentication successful for subject {SubjectId} from IdP {IdpName}",
            result.SubjectId, idpName);

        // Validate we have a subject ID
        if (string.IsNullOrEmpty(result.SubjectId))
        {
            _logger.LogError("SAML response had no SubjectId/NameID");
            return RedirectToPage("/Error", new { error = "saml_no_subject", message = "SAML response did not contain a subject identifier" });
        }

        // Build claims for the external login
        // This follows the same pattern as OAuth providers
        var claims = new List<Claim>
        {
            new(ClaimTypes.NameIdentifier, result.SubjectId),
            new("sub", result.SubjectId)
        };

        // Add claims from SAML assertion
        if (result.Principal != null)
        {
            foreach (var claim in result.Principal.Claims)
            {
                // Skip duplicates
                if (claim.Type != ClaimTypes.NameIdentifier && claim.Type != "sub")
                {
                    claims.Add(claim);
                }
            }
        }

        // Create identity and principal for external scheme
        // Use the provider name as the authentication type to match OAuth behavior
        var identity = new ClaimsIdentity(claims, IdentityConstants.ExternalScheme);
        identity.AddClaim(new Claim("provider", $"Saml.{idpName}"));

        // Add tenant_id claim - required by TenantCookieAuthenticationOptions for validation
        if (_tenantContext.HasTenant && !string.IsNullOrEmpty(_tenantContext.TenantId))
        {
            identity.AddClaim(new Claim("tenant_id", _tenantContext.TenantId));
        }

        var principal = new ClaimsPrincipal(identity);

        _logger.LogDebug("Created external identity with {ClaimCount} claims for SAML IdP {IdpName}",
            claims.Count, idpName);

        // Build authentication properties
        var authProperties = new AuthenticationProperties
        {
            RedirectUri = $"/account/external-callback?returnUrl={Uri.EscapeDataString(returnUrl)}",
            IsPersistent = false
        };

        // Store the login provider info (used by ExternalCallback page)
        authProperties.Items["LoginProvider"] = $"Saml.{idpName}";

        // Check if this was initiated from a registration flow
        if (state?.Properties?.TryGetValue("IsRegistration", out var isReg) == true && isReg == "true")
        {
            authProperties.Items["IsRegistration"] = "true";
        }

        // Store SAML-specific data
        if (!string.IsNullOrEmpty(result.SessionIndex))
        {
            authProperties.Items["saml_session_index"] = result.SessionIndex;
        }

        // Sign into the external scheme - this is what OAuth handlers do automatically
        // This stores the claims in a temporary cookie that ExternalCallback will read
        await HttpContext.SignInAsync(IdentityConstants.ExternalScheme, principal, authProperties);

        _logger.LogDebug("Signed into external scheme for SAML IdP {IdpName}, redirecting to external callback", idpName);

        // Clean up state
        if (!string.IsNullOrEmpty(relayState))
        {
            await _stateStore.RemoveAsync(relayState, cancellationToken);
        }

        // Redirect to our external callback page (same as OAuth flow)
        return Redirect(authProperties.RedirectUri);
    }

    /// <summary>
    /// Single Logout Service - initiates logout
    /// </summary>
    [HttpGet("logout")]
    public async Task<IActionResult> Logout(
        [FromQuery] string idpName,
        [FromQuery] string nameId,
        [FromQuery] string? sessionIndex,
        [FromQuery] string? returnUrl,
        CancellationToken cancellationToken)
    {
        if (string.IsNullOrEmpty(idpName) || string.IsNullOrEmpty(nameId))
        {
            return BadRequest(new { error = "invalid_request", error_description = "Missing idpName or nameId" });
        }

        var request = await _samlSp.CreateLogoutRequestAsync(idpName, nameId, sessionIndex, cancellationToken);

        _logger.LogDebug("Initiating SAML logout with IdP {IdpName} for {NameId}", idpName, nameId);

        return Redirect(request.Url);
    }

    /// <summary>
    /// Single Logout Service - receives logout response
    /// </summary>
    [HttpPost("slo")]
    public async Task<IActionResult> SingleLogoutService(
        [FromForm(Name = "SAMLResponse")] string? samlResponse,
        [FromForm(Name = "SAMLRequest")] string? samlRequest,
        [FromForm(Name = "RelayState")] string? relayState,
        CancellationToken cancellationToken)
    {
        if (!string.IsNullOrEmpty(samlResponse))
        {
            // This is a logout response from IdP
            var success = await _samlSp.ProcessLogoutResponseAsync(samlResponse, cancellationToken);

            if (success)
            {
                _logger.LogInformation("SAML logout successful");
                return Redirect(relayState ?? "/");
            }

            _logger.LogWarning("SAML logout failed");
            return RedirectToPage("/Error", new { error = "logout_failed" });
        }

        if (!string.IsNullOrEmpty(samlRequest))
        {
            // This is a logout request from IdP (IdP-initiated logout)
            // TODO: Process logout request and send response
            _logger.LogInformation("Received IdP-initiated logout request");

            // For now, just clear local session and respond
            return Redirect("/account/logout");
        }

        return BadRequest(new { error = "invalid_request" });
    }

    /// <summary>
    /// Lists available SAML IdPs
    /// </summary>
    [HttpGet("idps")]
    public async Task<IActionResult> ListIdps(CancellationToken cancellationToken = default)
    {
        var idps = (await _samlSp.GetConfiguredIdpsAsync(cancellationToken))
            .Where(i => i.Enabled)
            .Select(i => new
            {
                name = i.Name,
                displayName = i.DisplayName,
                entityId = i.EntityId
            });

        return Ok(idps);
    }
}
