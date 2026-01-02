using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Logging;
using Oluso.Core.Domain.Interfaces;
using Oluso.Core.Events;
using Oluso.Core.Protocols.Models;
using Oluso.Core.Protocols.Validation;

namespace Oluso.Protocols.Oidc;

/// <summary>
/// OAuth 2.0 Token Revocation Endpoint (RFC 7009).
/// Route is configured via OidcEndpointRouteConvention.
/// </summary>

public class OidcRevocationController : ControllerBase
{
    private readonly IClientAuthenticator _clientAuthenticator;
    private readonly IPersistedGrantStore _grantStore;
    private readonly ITenantContext _tenantContext;
    private readonly IOlusoEventService? _eventService;
    private readonly ILogger<OidcRevocationController> _logger;

    public OidcRevocationController(
        IClientAuthenticator clientAuthenticator,
        IPersistedGrantStore grantStore,
        ITenantContext tenantContext,
        ILogger<OidcRevocationController> logger,
        IOlusoEventService? eventService = null)
    {
        _clientAuthenticator = clientAuthenticator;
        _grantStore = grantStore;
        _tenantContext = tenantContext;
        _eventService = eventService;
        _logger = logger;
    }

    [HttpPost]
    [Consumes("application/x-www-form-urlencoded")]
    public async Task<IActionResult> Revoke(CancellationToken cancellationToken)
    {
        // Authenticate client
        var clientAuth = await _clientAuthenticator.AuthenticateAsync(Request, cancellationToken);
        if (!clientAuth.IsValid)
        {
            return Unauthorized(new TokenErrorResponse
            {
                Error = clientAuth.Error!,
                ErrorDescription = clientAuth.ErrorDescription
            });
        }

        var form = await Request.ReadFormAsync(cancellationToken);
        var token = form["token"].FirstOrDefault();

        if (string.IsNullOrEmpty(token))
        {
            return BadRequest(new TokenErrorResponse
            {
                Error = OidcConstants.Errors.InvalidRequest,
                ErrorDescription = "token is required"
            });
        }

        // Try to revoke the token
        // Per RFC 7009, we should return 200 OK even if token doesn't exist
        var grant = await _grantStore.GetAsync(token, cancellationToken);

        if (grant != null)
        {
            // Verify the token belongs to the requesting client
            if (grant.ClientId == clientAuth.Client?.ClientId)
            {
                await _grantStore.RemoveAsync(token, cancellationToken);
                _logger.LogInformation("Token revoked for client {ClientId}", clientAuth.Client?.ClientId);

                // Raise token revoked event
                if (_eventService != null)
                {
                    await _eventService.RaiseAsync(new TokenRevokedEvent
                    {
                        TenantId = _tenantContext.TenantId,
                        ClientId = clientAuth.Client?.ClientId ?? "",
                        SubjectId = grant.SubjectId,
                        TokenType = grant.Type
                    }, cancellationToken);
                }
            }
            else
            {
                // Token doesn't belong to this client - don't revoke, but don't error either
                _logger.LogWarning("Client {ClientId} attempted to revoke token belonging to {TokenClient}",
                    clientAuth.Client?.ClientId, grant.ClientId);
            }
        }

        // Always return 200 OK per RFC 7009
        return Ok();
    }
}
