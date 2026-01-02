using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Logging;
using Oluso.Core.Domain.Interfaces;
using Oluso.Core.Events;
using Oluso.Core.Protocols.Grants;
using Oluso.Core.Protocols.Models;
using Oluso.Core.Protocols.Validation;
using Oluso.Core.Services;

namespace Oluso.Protocols.Oidc;

/// <summary>
/// OAuth 2.0 Token Endpoint.
/// Route is configured via OidcEndpointRouteConvention.
/// </summary>

public class OidcTokenController : ControllerBase
{
    private readonly ITokenRequestValidator _requestValidator;
    private readonly IGrantHandlerRegistry _grantRegistry;
    private readonly ITokenService _tokenService;
    private readonly ITenantContext _tenantContext;
    private readonly IOlusoEventService? _eventService;
    private readonly ILogger<OidcTokenController> _logger;

    public OidcTokenController(
        ITokenRequestValidator requestValidator,
        IGrantHandlerRegistry grantRegistry,
        ITokenService tokenService,
        ITenantContext tenantContext,
        ILogger<OidcTokenController> logger,
        IOlusoEventService? eventService = null)
    {
        _requestValidator = requestValidator;
        _grantRegistry = grantRegistry;
        _tokenService = tokenService;
        _tenantContext = tenantContext;
        _eventService = eventService;
        _logger = logger;
    }

    [HttpPost]
    [Consumes("application/x-www-form-urlencoded")]
    public async Task<IActionResult> Token(CancellationToken cancellationToken)
    {
        // 1. Validate request (client authentication + grant-specific validation)
        var validationResult = await _requestValidator.ValidateAsync(Request, cancellationToken);
        if (!validationResult.IsValid)
        {
            _logger.LogWarning("Token request validation failed: {Error} - {Description}",
                validationResult.Error, validationResult.ErrorDescription);

            // Add any response headers (e.g., DPoP-Nonce)
            if (validationResult.ResponseHeaders != null)
            {
                foreach (var header in validationResult.ResponseHeaders)
                {
                    Response.Headers[header.Key] = header.Value;
                }
            }

            return BadRequest(new TokenErrorResponse
            {
                Error = validationResult.Error!,
                ErrorDescription = validationResult.ErrorDescription
            });
        }

        var tokenRequest = validationResult.Data!;

        // 2. Get grant handler
        var handler = _grantRegistry.GetHandler(tokenRequest.GrantType);
        if (handler == null)
        {
            return BadRequest(new TokenErrorResponse
            {
                Error = OidcConstants.Errors.UnsupportedGrantType,
                ErrorDescription = $"Grant type '{tokenRequest.GrantType}' is not supported"
            });
        }

        // 3. Process grant
        var grantResult = await handler.HandleAsync(tokenRequest, cancellationToken);
        if (!grantResult.IsValid)
        {
            _logger.LogWarning("Grant processing failed: {Error} - {Description}",
                grantResult.Error, grantResult.ErrorDescription);

            // Special handling for device flow polling
            if (grantResult.Error == OidcConstants.Errors.AuthorizationPending ||
                grantResult.Error == OidcConstants.Errors.SlowDown)
            {
                return BadRequest(new TokenErrorResponse
                {
                    Error = grantResult.Error!,
                    ErrorDescription = grantResult.ErrorDescription
                });
            }

            return BadRequest(new TokenErrorResponse
            {
                Error = grantResult.Error!,
                ErrorDescription = grantResult.ErrorDescription
            });
        }

        // 4. Create tokens
        TokenResponse tokenResponse;
        try
        {
            tokenResponse = await _tokenService.CreateTokenResponseAsync(grantResult, tokenRequest, cancellationToken);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to create token response for client {ClientId}", tokenRequest.Client?.ClientId);
            return StatusCode(500, new TokenErrorResponse
            {
                Error = "server_error",
                ErrorDescription = "Failed to create tokens"
            });
        }

        _logger.LogInformation("Token issued for client {ClientId}, grant type {GrantType}, subject {SubjectId}",
            tokenRequest.Client?.ClientId, tokenRequest.GrantType, grantResult.SubjectId);

        // Log the actual JSON that will be serialized
        var jsonResponse = System.Text.Json.JsonSerializer.Serialize(tokenResponse);
        _logger.LogDebug("Token response JSON (first 500 chars): {Json}",
            jsonResponse.Substring(0, Math.Min(500, jsonResponse.Length)));

        // 5. Raise token issued event
        if (_eventService != null)
        {
            await _eventService.RaiseAsync(new TokenIssuedEvent
            {
                TenantId = _tenantContext.TenantId,
                ClientId = tokenRequest.Client?.ClientId ?? "",
                SubjectId = grantResult.SubjectId,
                Scopes = grantResult.Scopes ?? Enumerable.Empty<string>(),
                TokenType = tokenRequest.GrantType
            }, cancellationToken);
        }

        return Ok(tokenResponse);
    }
}
