using System.IdentityModel.Tokens.Jwt;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Logging;
using Microsoft.IdentityModel.Tokens;
using Oluso.Core.Domain.Entities;
using Oluso.Core.Domain.Interfaces;
using Oluso.Core.Events;
using Oluso.Core.Protocols.Models;
using Oluso.Core.Protocols.Validation;

namespace Oluso.Protocols.Oidc;

/// <summary>
/// OAuth 2.0 Token Revocation Endpoint (RFC 7009).
/// Route is configured via OidcEndpointRouteConvention.
/// <para>
/// Revoking a token revokes the authorization it belongs to (RFC 7009 §2.1; the Matrix OAuth 2.0 API requires
/// revoking one to revoke both the access and the refresh token): a refresh token or reference token takes every
/// grant of the same user, client and session with it; a JWT access token, which the server doesn't store, is
/// verified (signature, and issued to the revoking client) and the grants of its user, client and session
/// (<c>sid</c>) are removed. A token without a session revokes only itself.
/// </para>
/// </summary>
public class OidcRevocationController : OidcControllerBase
{
    private readonly IClientAuthenticator _clientAuthenticator;
    private readonly IPersistedGrantStore _grantStore;
    private readonly ITenantContext _tenantContext;
    private readonly ISigningCredentialStore? _signingCredentials;
    private readonly IOlusoEventService? _eventService;
    private readonly ILogger<OidcRevocationController> _logger;

    public OidcRevocationController(
        IClientAuthenticator clientAuthenticator,
        IPersistedGrantStore grantStore,
        ITenantContext tenantContext,
        ILogger<OidcRevocationController> logger,
        IOlusoEventService? eventService = null,
        ISigningCredentialStore? signingCredentials = null)
    {
        _clientAuthenticator = clientAuthenticator;
        _grantStore = grantStore;
        _tenantContext = tenantContext;
        _eventService = eventService;
        _signingCredentials = signingCredentials;
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

        var clientId = clientAuth.Client?.ClientId ?? "";

        // A stored token: a refresh token or a reference access token.
        var grant = await _grantStore.GetAsync(token, cancellationToken);
        if (grant != null)
        {
            if (grant.ClientId == clientId)
            {
                await _grantStore.RemoveAsync(token, cancellationToken);
                var siblings = await RevokeAuthorizationAsync(grant.SubjectId, clientId, grant.SessionId, cancellationToken);
                _logger.LogInformation("Token revoked for client {ClientId} with {Count} other grant(s) of its authorization",
                    clientId, siblings);
                await RaiseAsync(grant.TenantId, clientId, grant.SubjectId, grant.Type, cancellationToken);
            }
            else
            {
                // RFC 7009 §2.1: the token must have been issued to the revoking client. Still 200, so the
                // endpoint can't be used to probe for tokens.
                _logger.LogWarning("Client {ClientId} attempted to revoke token belonging to {TokenClient}",
                    clientId, grant.ClientId);
            }
            return Ok();
        }

        // A JWT access token: revoke the authorization it came from.
        var jwt = await VerifiedAccessTokenAsync(token, cancellationToken);
        if (jwt != null)
        {
            var tokenClient = jwt.Claims.FirstOrDefault(c => c.Type == "client_id")?.Value;
            var sessionId = jwt.Claims.FirstOrDefault(c => c.Type == "sid")?.Value;
            if (tokenClient != clientId)
            {
                _logger.LogWarning("Client {ClientId} attempted to revoke an access token issued to {TokenClient}", clientId, tokenClient);
            }
            else if (!string.IsNullOrEmpty(jwt.Subject) && !string.IsNullOrEmpty(sessionId))
            {
                var revoked = await RevokeAuthorizationAsync(jwt.Subject, clientId, sessionId, cancellationToken);
                _logger.LogInformation("Access token revoked for client {ClientId}: {Count} grant(s) of its authorization removed",
                    clientId, revoked);
                await RaiseAsync(jwt.Claims.FirstOrDefault(c => c.Type == "tenant_id")?.Value ?? _tenantContext.TenantId,
                    clientId, jwt.Subject, "access_token", cancellationToken);
            }
        }

        // RFC 7009 §2.2: an unknown or invalid token still answers 200.
        return Ok();
    }

    /// <summary>Removes every grant of the authorization (user, client, session); returns how many there were.</summary>
    private async Task<int> RevokeAuthorizationAsync(string? subjectId, string clientId, string? sessionId, CancellationToken cancellationToken)
    {
        if (string.IsNullOrEmpty(subjectId) || string.IsNullOrEmpty(sessionId))
            return 0;

        var filter = new PersistedGrantFilter { SubjectId = subjectId, ClientId = clientId, SessionId = sessionId };
        var grants = (await _grantStore.GetAllAsync(filter, cancellationToken)).Count();
        if (grants > 0)
            await _grantStore.RemoveAllAsync(filter, cancellationToken);
        return grants;
    }

    /// <summary>
    /// The token as a JWT access token this server signed (any of the token's tenant's validation keys, which also
    /// covers keys rotated out of signing), or null. Lifetime isn't checked: revoking an expired token is harmless.
    /// </summary>
    private async Task<JwtSecurityToken?> VerifiedAccessTokenAsync(string token, CancellationToken cancellationToken)
    {
        if (_signingCredentials == null)
            return null;
        var handler = new JwtSecurityTokenHandler();
        if (!handler.CanReadToken(token))
            return null;

        try
        {
            var unverified = handler.ReadJwtToken(token);
            var tenantId = unverified.Claims.FirstOrDefault(c => c.Type == "tenant_id")?.Value ?? _tenantContext.TenantId;
            var keys = (await _signingCredentials.GetValidationKeysAsync(tenantId, cancellationToken))?.Select(k => k.Key).ToList();
            if (keys == null || keys.Count == 0)
                return null;

            handler.ValidateToken(token, new TokenValidationParameters
            {
                ValidateIssuerSigningKey = true,
                IssuerSigningKeys = keys,
                ValidateIssuer = false,
                ValidateAudience = false,
                ValidateLifetime = false,
                TypeValidator = Oluso.Protocols.Validation.AccessTokenTypes.Validate,
            }, out var validated);
            return validated as JwtSecurityToken;
        }
        catch (Exception ex) when (ex is SecurityTokenException or ArgumentException)
        {
            _logger.LogDebug(ex, "Revocation: the token isn't a JWT access token this server signed");
            return null;
        }
    }

    private async Task RaiseAsync(string? tenantId, string clientId, string? subjectId, string tokenType, CancellationToken cancellationToken)
    {
        if (_eventService == null)
            return;
        await _eventService.RaiseAsync(new TokenRevokedEvent
        {
            TenantId = tenantId,
            ClientId = clientId,
            SubjectId = subjectId,
            TokenType = tokenType
        }, cancellationToken);
    }
}
