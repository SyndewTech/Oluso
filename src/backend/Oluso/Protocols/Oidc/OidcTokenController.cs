using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Logging;
using Oluso.Core.Domain.Interfaces;
using Oluso.Core.Events;
using Oluso.Core.Protocols;
using Oluso.Core.Protocols.Grants;
using Oluso.Core.Protocols.Models;
using Oluso.Core.Protocols.Validation;
using Oluso.Core.Services;
using Microsoft.AspNetCore.RateLimiting;
using Oluso.Core.Security;

using Oluso.Protocols.Services;

namespace Oluso.Protocols.Oidc;

/// <summary>
/// OAuth 2.0 Token Endpoint.
/// Route is configured via OidcEndpointRouteConvention.
/// </summary>

[EnableRateLimiting(SignInRateLimitPolicies.Token)]
public class OidcTokenController : OidcControllerBase
{
    private readonly ITokenRequestValidator _requestValidator;
    private readonly IGrantHandlerRegistry _grantRegistry;
    private readonly ITokenService _tokenService;
    private readonly ITenantContext _tenantContext;
    private readonly IOlusoEventService? _eventService;
    private readonly ILogger<OidcTokenController> _logger;
    private readonly IClientUsageRecorder _usage;
    private readonly IScopeFeatureGate? _scopeFeatures;

    public OidcTokenController(
        ITokenRequestValidator requestValidator,
        IGrantHandlerRegistry grantRegistry,
        ITokenService tokenService,
        ITenantContext tenantContext,
        IClientUsageRecorder usage,
        ILogger<OidcTokenController> logger,
        IOlusoEventService? eventService = null,
        IScopeFeatureGate? scopeFeatures = null)
    {
        _scopeFeatures = scopeFeatures;
        _usage = usage;
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

            // Audit pre-grant validation failure. ClientId/GrantType may not be
            // strictly available pre-validation, so read them best-effort from
            // the form so the audit row carries something useful.
            await RaiseTokenRequestFailedFromFormAsync(
                validationResult.Error!,
                validationResult.ErrorDescription,
                cancellationToken);

            return BadRequest(new TokenErrorResponse
            {
                Error = validationResult.Error!,
                ErrorDescription = validationResult.ErrorDescription
            });
        }

        var tokenRequest = validationResult.Data!;

        // Audit successful request acceptance — paired with the eventual
        // TokenIssued or TokenRequestFailed (grant) event.
        if (_eventService != null)
        {
            await _eventService.RaiseAsync(new TokenRequestReceivedEvent
            {
                // Tag the event with the client's tenant rather than the
                // request's resolved tenant. Token-endpoint callers often
                // hit the platform host (no subdomain), in which case the
                // request resolves to "_platform" by middleware fallback —
                // but the event belongs to the tenant that owns the client.
                TenantId = tokenRequest.Client?.TenantId ?? _tenantContext.TenantId,
                ClientId = tokenRequest.Client?.ClientId ?? "",
                GrantType = tokenRequest.GrantType,
                Scopes = tokenRequest.RequestedScopes ?? Enumerable.Empty<string>()
            }, cancellationToken);
        }

        // 2. Get grant handler
        var handler = _grantRegistry.GetHandler(tokenRequest.GrantType);
        if (handler == null)
        {
            await RaiseTokenRequestFailedAsync(tokenRequest, OidcConstants.Errors.UnsupportedGrantType,
                $"Grant type '{tokenRequest.GrantType}' is not supported", cancellationToken);

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

            // Don't audit device-flow polling responses — those are expected
            // intermediate states, not failures the operator needs to see.
            if (grantResult.Error != OidcConstants.Errors.AuthorizationPending &&
                grantResult.Error != OidcConstants.Errors.SlowDown)
            {
                await RaiseTokenRequestFailedAsync(tokenRequest, grantResult.Error!,
                    grantResult.ErrorDescription, cancellationToken);
            }

            return BadRequest(new TokenErrorResponse
            {
                Error = grantResult.Error!,
                ErrorDescription = grantResult.ErrorDescription
            });
        }

        // Scopes a grant carries forward (a refresh, an exchange, a code) whose tenant feature has since been
        // switched off are left out of the new tokens; explicitly requested ones were refused by the scope validator.
        if (_scopeFeatures != null && grantResult.Scopes.Count > 0)
        {
            var unavailable = await _scopeFeatures.FindUnavailableAsync(grantResult.Scopes, cancellationToken);
            if (unavailable.Count > 0)
            {
                _logger.LogInformation("Leaving out scope(s) {Scopes} for client {ClientId}: their tenant feature is off",
                    string.Join(" ", unavailable), tokenRequest.Client?.ClientId);
                grantResult.Scopes = grantResult.Scopes.Except(unavailable, StringComparer.Ordinal).ToList();
            }
        }

        // Pre-issuance hook — subscribers can veto issuance for this specific
        // grant outcome (e.g. abuse detection on top of valid credentials).
        if (_eventService != null)
        {
            var issuing = new TokenIssuingEvent
            {
                TenantId = tokenRequest.Client?.TenantId ?? _tenantContext.TenantId,
                ClientId = tokenRequest.Client?.ClientId ?? "",
                SubjectId = grantResult.SubjectId,
                Scopes = grantResult.Scopes ?? Enumerable.Empty<string>(),
                GrantType = tokenRequest.GrantType
            };
            await _eventService.RaiseAsync(issuing, cancellationToken);
            if (issuing.Deny)
            {
                _logger.LogWarning("Token issuance denied by issuing-event subscriber: {Reason}", issuing.DenyReason);
                await RaiseTokenRequestFailedAsync(tokenRequest, OidcConstants.Errors.AccessDenied,
                    issuing.DenyReason, cancellationToken);
                return BadRequest(new TokenErrorResponse
                {
                    Error = OidcConstants.Errors.AccessDenied,
                    ErrorDescription = issuing.DenyReason ?? "Token issuance denied"
                });
            }
        }

        // 4. Create tokens
        TokenResponse tokenResponse;
        try
        {
            tokenResponse = await _tokenService.CreateTokenResponseAsync(grantResult, tokenRequest, cancellationToken);
        }
        catch (TokenTenantMismatchException ex)
        {
            // The user has no access to the tenant this request is routed to (their token would be for another).
            _logger.LogWarning("No tokens for client {ClientId}, subject {SubjectId}: {Reason}",
                tokenRequest.Client?.ClientId, grantResult.SubjectId, ex.Message);
            const string description = "Your account has no access to this organization";
            await RaiseTokenRequestFailedAsync(tokenRequest, OidcConstants.Errors.AccessDenied, description, cancellationToken);
            return BadRequest(new TokenErrorResponse
            {
                Error = OidcConstants.Errors.AccessDenied,
                ErrorDescription = description
            });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to create token response for client {ClientId}", tokenRequest.Client?.ClientId);
            await RaiseTokenRequestFailedAsync(tokenRequest, "server_error",
                "Failed to create tokens", cancellationToken);
            return StatusCode(500, new TokenErrorResponse
            {
                Error = "server_error",
                ErrorDescription = "Failed to create tokens"
            });
        }

        _logger.LogInformation("Token issued for client {ClientId}, grant type {GrantType}, subject {SubjectId}",
            tokenRequest.Client?.ClientId, tokenRequest.GrantType, grantResult.SubjectId);

        await _usage.RecordAsync(tokenRequest.Client?.ClientId ?? "", cancellationToken);

        // Log the actual JSON that will be serialized
        var jsonResponse = System.Text.Json.JsonSerializer.Serialize(tokenResponse);
        _logger.LogDebug("Token response JSON (first 500 chars): {Json}",
            jsonResponse.Substring(0, Math.Min(500, jsonResponse.Length)));

        // 5. Raise token issued event (and a refresh-specific marker if applicable)
        if (_eventService != null)
        {
            await _eventService.RaiseAsync(new TokenIssuedEvent
            {
                TenantId = tokenRequest.Client?.TenantId ?? _tenantContext.TenantId,
                ClientId = tokenRequest.Client?.ClientId ?? "",
                SubjectId = grantResult.SubjectId,
                Scopes = grantResult.Scopes ?? Enumerable.Empty<string>(),
                TokenType = tokenRequest.GrantType
            }, cancellationToken);

            if (string.Equals(tokenRequest.GrantType, OidcConstants.GrantTypes.RefreshToken, StringComparison.Ordinal)
                && !string.IsNullOrEmpty(grantResult.SubjectId))
            {
                await _eventService.RaiseAsync(new RefreshTokenUsedEvent
                {
                    TenantId = tokenRequest.Client?.TenantId ?? _tenantContext.TenantId,
                    ClientId = tokenRequest.Client?.ClientId ?? "",
                    SubjectId = grantResult.SubjectId
                }, cancellationToken);
            }
        }

        return Ok(tokenResponse);
    }

    private async Task RaiseTokenRequestFailedAsync(
        TokenRequest tokenRequest,
        string error,
        string? errorDescription,
        CancellationToken cancellationToken)
    {
        if (_eventService == null) return;
        await _eventService.RaiseAsync(new TokenRequestFailedEvent
        {
            TenantId = tokenRequest.Client?.TenantId ?? _tenantContext.TenantId,
            ClientId = tokenRequest.Client?.ClientId ?? "",
            GrantType = tokenRequest.GrantType,
            Error = error,
            ErrorDescription = errorDescription
        }, cancellationToken);
    }

    private async Task RaiseTokenRequestFailedFromFormAsync(
        string error,
        string? errorDescription,
        CancellationToken cancellationToken)
    {
        if (_eventService == null) return;
        if (!Request.HasFormContentType) return;

        // Best-effort: pull client_id / grant_type from form data. Pre-
        // validation failures may have either or neither — skip the event if
        // neither is present rather than emitting a meaningless row.
        var form = await Request.ReadFormAsync(cancellationToken);
        var clientId = form["client_id"].FirstOrDefault() ?? "";
        var grantType = form["grant_type"].FirstOrDefault() ?? "";
        if (string.IsNullOrEmpty(clientId) && string.IsNullOrEmpty(grantType)) return;

        await _eventService.RaiseAsync(new TokenRequestFailedEvent
        {
            TenantId = _tenantContext.TenantId,
            ClientId = clientId,
            GrantType = grantType,
            Error = error,
            ErrorDescription = errorDescription
        }, cancellationToken);
    }
}
