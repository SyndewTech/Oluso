using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Logging;
using Oluso.Core.Domain.Interfaces;
using Oluso.Core.Protocols;
using Oluso.Core.Protocols.Models;
using Oluso.Core.Protocols.Validation;
using Oluso.Core.Services;

namespace Oluso.Protocols.Oidc;

/// <summary>
/// CIBA (Client Initiated Backchannel Authentication) endpoints.
/// Implements OpenID Connect Client Initiated Backchannel Authentication Flow.
/// Route is configured via OidcEndpointRouteConvention.
/// </summary>
public class OidcCibaController : ControllerBase
{
    private readonly ICibaService _cibaService;
    private readonly IClientAuthenticator _clientAuthenticator;
    private readonly ILogger<OidcCibaController> _logger;

    public OidcCibaController(
        ICibaService cibaService,
        IClientAuthenticator clientAuthenticator,
        ILogger<OidcCibaController> logger)
    {
        _cibaService = cibaService;
        _clientAuthenticator = clientAuthenticator;
        _logger = logger;
    }

    /// <summary>
    /// Backchannel authentication endpoint.
    /// Clients use this to initiate authentication for a user.
    /// </summary>
    [HttpPost]
    [AllowAnonymous]
    public async Task<IActionResult> Authenticate(CancellationToken cancellationToken)
    {
        _logger.LogDebug("CIBA authentication request received");

        // Read form data
        var form = await Request.ReadFormAsync(cancellationToken);

        // Validate client credentials
        var clientValidation = await _clientAuthenticator.AuthenticateAsync(Request, cancellationToken);

        if (!clientValidation.IsValid || clientValidation.Client == null)
        {
            return BadRequest(new
            {
                error = "invalid_client",
                error_description = clientValidation.ErrorDescription ?? "Client authentication failed"
            });
        }

        var clientEntity = clientValidation.Client;

        // Check if CIBA is enabled for this client
        if (!clientEntity.CibaEnabled)
        {
            return BadRequest(new
            {
                error = "unauthorized_client",
                error_description = "CIBA is not enabled for this client"
            });
        }

        // Check grant type is allowed
        if (!clientEntity.AllowedGrantTypes.Any(g => g.GrantType == OidcConstants.GrantTypes.Ciba))
        {
            return BadRequest(new
            {
                error = "unauthorized_client",
                error_description = "Client is not authorized for CIBA grant type"
            });
        }

        // Create ValidatedClient for the service
        var validatedClient = new ValidatedClient
        {
            ClientId = clientEntity.ClientId,
            ClientName = clientEntity.ClientName,
            CibaEnabled = clientEntity.CibaEnabled,
            CibaTokenDeliveryMode = clientEntity.CibaTokenDeliveryMode,
            CibaClientNotificationEndpoint = clientEntity.CibaClientNotificationEndpoint,
            CibaRequestLifetime = clientEntity.CibaRequestLifetime,
            CibaPollingInterval = clientEntity.CibaPollingInterval,
            CibaRequireUserCode = clientEntity.CibaRequireUserCode
        };

        // Parse request
        var request = new CibaAuthenticationRequest
        {
            ClientId = clientEntity.ClientId,
            Scope = form["scope"].FirstOrDefault(),
            LoginHint = form["login_hint"].FirstOrDefault(),
            LoginHintToken = form["login_hint_token"].FirstOrDefault(),
            IdTokenHint = form["id_token_hint"].FirstOrDefault(),
            BindingMessage = form["binding_message"].FirstOrDefault(),
            UserCode = form["user_code"].FirstOrDefault(),
            AcrValues = form["acr_values"].FirstOrDefault(),
            RequestedExpiry = ParseInt(form["requested_expiry"].FirstOrDefault()),
            ClientNotificationToken = form["client_notification_token"].FirstOrDefault()
        };

        // Validate the request
        var result = await _cibaService.AuthenticateAsync(request, validatedClient, cancellationToken);

        if (!result.IsSuccess)
        {
            return BadRequest(new
            {
                error = result.Error,
                error_description = result.ErrorDescription
            });
        }

        // Return the auth_req_id
        var response = new
        {
            auth_req_id = result.AuthReqId,
            expires_in = result.ExpiresIn,
            interval = result.Interval
        };

        return Ok(response);
    }

    private static int? ParseInt(string? value)
    {
        if (string.IsNullOrEmpty(value)) return null;
        return int.TryParse(value, out var result) ? result : null;
    }
}
