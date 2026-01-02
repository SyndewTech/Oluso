using System.IdentityModel.Tokens.Jwt;
using System.Security.Cryptography;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using Microsoft.IdentityModel.Tokens;
using Oluso.Core.Domain.Interfaces;
using Oluso.Core.Protocols.Models;
using Oluso.Core.Services;

namespace Oluso.Protocols.Ciba;

/// <summary>
/// Implementation of CIBA (Client Initiated Backchannel Authentication) service.
/// </summary>
public class CibaService : ICibaService
{
    private readonly ICibaStore _cibaStore;
    private readonly IOlusoUserService _userService;
    private readonly ISigningCredentialStore _signingCredentialStore;
    private readonly ITenantContext _tenantContext;
    private readonly IConfiguration _configuration;
    private readonly ICibaUserNotificationService? _notificationService;
    private readonly ILogger<CibaService> _logger;

    public CibaService(
        ICibaStore cibaStore,
        IOlusoUserService userService,
        ISigningCredentialStore signingCredentialStore,
        ITenantContext tenantContext,
        IConfiguration configuration,
        ILogger<CibaService> logger,
        ICibaUserNotificationService? notificationService = null)
    {
        _cibaStore = cibaStore;
        _userService = userService;
        _signingCredentialStore = signingCredentialStore;
        _tenantContext = tenantContext;
        _configuration = configuration;
        _logger = logger;
        _notificationService = notificationService;
    }

    public async Task<CibaAuthenticationResult> AuthenticateAsync(
        CibaAuthenticationRequest request,
        ValidatedClient client,
        CancellationToken cancellationToken = default)
    {
        // Validate that we have at least one hint
        if (string.IsNullOrEmpty(request.LoginHint) &&
            string.IsNullOrEmpty(request.LoginHintToken) &&
            string.IsNullOrEmpty(request.IdTokenHint))
        {
            return CibaAuthenticationResult.Failure(
                "invalid_request",
                "One of login_hint, login_hint_token, or id_token_hint is required");
        }

        // Resolve the user from the hint
        var subjectId = await ResolveUserFromHintAsync(request, client.ClientId, cancellationToken);
        if (string.IsNullOrEmpty(subjectId))
        {
            return CibaAuthenticationResult.Failure(
                "unknown_user_id",
                "Unable to identify the user from the provided hint");
        }

        // Validate user code if required
        if (client.CibaRequireUserCode && string.IsNullOrEmpty(request.UserCode))
        {
            return CibaAuthenticationResult.Failure(
                "invalid_request",
                "User code is required for this client");
        }

        // Generate auth_req_id
        var authReqId = GenerateAuthReqId();

        // Calculate expiry
        var expiresIn = request.RequestedExpiry ?? client.CibaRequestLifetime;
        if (expiresIn > client.CibaRequestLifetime)
        {
            expiresIn = client.CibaRequestLifetime;
        }

        // Determine token delivery mode
        var deliveryMode = ParseDeliveryMode(client.CibaTokenDeliveryMode);

        // Validate notification endpoint for ping/push modes
        if (deliveryMode != CibaTokenDeliveryMode.Poll)
        {
            if (string.IsNullOrEmpty(client.CibaClientNotificationEndpoint))
            {
                return CibaAuthenticationResult.Failure(
                    "invalid_request",
                    "Client notification endpoint is required for ping/push delivery modes");
            }

            if (string.IsNullOrEmpty(request.ClientNotificationToken))
            {
                return CibaAuthenticationResult.Failure(
                    "invalid_request",
                    "client_notification_token is required for ping/push delivery modes");
            }
        }

        // Create the CIBA request
        var cibaRequest = new CibaRequest
        {
            AuthReqId = authReqId,
            ClientId = request.ClientId,
            SubjectId = subjectId,
            LoginHint = request.LoginHint,
            LoginHintToken = request.LoginHintToken,
            IdTokenHint = request.IdTokenHint,
            BindingMessage = request.BindingMessage,
            UserCode = request.UserCode,
            RequestedScopes = request.Scope ?? "openid",
            AcrValues = request.AcrValues,
            CreatedAt = DateTime.UtcNow,
            ExpiresAt = DateTime.UtcNow.AddSeconds(expiresIn),
            Interval = client.CibaPollingInterval,
            TokenDeliveryMode = deliveryMode,
            ClientNotificationToken = request.ClientNotificationToken
        };

        await _cibaStore.StoreRequestAsync(cibaRequest, cancellationToken);

        _logger.LogInformation(
            "CIBA request created: {AuthReqId} for client {ClientId}, user {SubjectId}",
            authReqId, request.ClientId, subjectId);

        // Notify the user (if notification service is available)
        if (_notificationService != null)
        {
            try
            {
                await _notificationService.NotifyUserAsync(cibaRequest, cancellationToken);
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Failed to send CIBA notification to user {SubjectId}", subjectId);
                // Don't fail the request - the user can still be notified through other means
            }
        }

        return CibaAuthenticationResult.Success(
            authReqId,
            expiresIn,
            client.CibaPollingInterval);
    }

    public async Task<CibaStatusResult> GetStatusAsync(
        string authReqId,
        string clientId,
        CancellationToken cancellationToken = default)
    {
        var request = await _cibaStore.GetByAuthReqIdAsync(authReqId, cancellationToken);

        if (request == null)
        {
            return new CibaStatusResult
            {
                Status = CibaRequestStatus.Expired,
                Error = "expired_token",
                ErrorDescription = "The auth_req_id has expired or does not exist"
            };
        }

        // Verify the client
        if (request.ClientId != clientId)
        {
            return new CibaStatusResult
            {
                Status = CibaRequestStatus.Denied,
                Error = "access_denied",
                ErrorDescription = "The auth_req_id was not issued to this client"
            };
        }

        // Check expiration
        if (request.ExpiresAt < DateTime.UtcNow)
        {
            // Update status to expired
            request.Status = CibaRequestStatus.Expired;
            await _cibaStore.UpdateRequestAsync(request, cancellationToken);

            return new CibaStatusResult
            {
                Status = CibaRequestStatus.Expired,
                Error = "expired_token",
                ErrorDescription = "The auth_req_id has expired"
            };
        }

        return new CibaStatusResult
        {
            Status = request.Status,
            SessionId = request.SessionId,
            SubjectId = request.SubjectId,
            Scopes = request.GetScopes(),
            Error = request.Error,
            ErrorDescription = request.ErrorDescription,
            Interval = request.Interval
        };
    }

    public async Task<bool> ApproveRequestAsync(
        string authReqId,
        string subjectId,
        string? sessionId = null,
        CancellationToken cancellationToken = default)
    {
        var request = await _cibaStore.GetByAuthReqIdAsync(authReqId, cancellationToken);

        if (request == null || request.Status != CibaRequestStatus.Pending)
        {
            _logger.LogWarning("Cannot approve CIBA request {AuthReqId}: not found or not pending", authReqId);
            return false;
        }

        // Verify the subject matches
        if (request.SubjectId != subjectId)
        {
            _logger.LogWarning(
                "Cannot approve CIBA request {AuthReqId}: subject mismatch ({Expected} vs {Actual})",
                authReqId, request.SubjectId, subjectId);
            return false;
        }

        request.Status = CibaRequestStatus.Approved;
        request.CompletedAt = DateTime.UtcNow;
        request.SessionId = sessionId;

        await _cibaStore.UpdateRequestAsync(request, cancellationToken);

        _logger.LogInformation("CIBA request {AuthReqId} approved by user {SubjectId}", authReqId, subjectId);

        return true;
    }

    public async Task<bool> DenyRequestAsync(
        string authReqId,
        CancellationToken cancellationToken = default)
    {
        var request = await _cibaStore.GetByAuthReqIdAsync(authReqId, cancellationToken);

        if (request == null || request.Status != CibaRequestStatus.Pending)
        {
            _logger.LogWarning("Cannot deny CIBA request {AuthReqId}: not found or not pending", authReqId);
            return false;
        }

        request.Status = CibaRequestStatus.Denied;
        request.CompletedAt = DateTime.UtcNow;
        request.Error = "access_denied";
        request.ErrorDescription = "The user denied the authentication request";

        await _cibaStore.UpdateRequestAsync(request, cancellationToken);

        _logger.LogInformation("CIBA request {AuthReqId} denied", authReqId);

        return true;
    }

    private async Task<string?> ResolveUserFromHintAsync(
        CibaAuthenticationRequest request,
        string clientId,
        CancellationToken cancellationToken)
    {
        // Try login_hint first (usually email or username)
        if (!string.IsNullOrEmpty(request.LoginHint))
        {
            var subjectId = await ResolveFromLoginHintAsync(request.LoginHint, cancellationToken);
            if (subjectId != null)
            {
                return subjectId;
            }
        }

        // Try login_hint_token (a JWT containing the subject identifier)
        if (!string.IsNullOrEmpty(request.LoginHintToken))
        {
            var subjectId = await ResolveFromLoginHintTokenAsync(request.LoginHintToken, cancellationToken);
            if (subjectId != null)
            {
                _logger.LogDebug("Resolved user {SubjectId} from login_hint_token", subjectId);
                return subjectId;
            }
        }

        // Try id_token_hint (extract subject from previously issued ID token)
        if (!string.IsNullOrEmpty(request.IdTokenHint))
        {
            var subjectId = await ResolveFromIdTokenHintAsync(request.IdTokenHint, clientId, cancellationToken);
            if (subjectId != null)
            {
                _logger.LogDebug("Resolved user {SubjectId} from id_token_hint", subjectId);
                return subjectId;
            }
        }

        return null;
    }

    private async Task<string?> ResolveFromLoginHintAsync(
        string loginHint,
        CancellationToken cancellationToken)
    {
        // Try as email
        var user = await _userService.FindByEmailAsync(loginHint, cancellationToken);
        if (user != null)
        {
            return user.Id;
        }

        // Try as username
        user = await _userService.FindByUsernameAsync(loginHint, cancellationToken);
        if (user != null)
        {
            return user.Id;
        }

        // Try as user ID
        user = await _userService.FindByIdAsync(loginHint, cancellationToken);
        if (user != null)
        {
            return user.Id;
        }

        return null;
    }

    /// <summary>
    /// Resolves user from a login_hint_token.
    /// The login_hint_token is a signed JWT containing the subject identifier.
    /// Per CIBA spec, this token is typically signed by the authorization server itself
    /// or by a trusted third party.
    /// </summary>
    private async Task<string?> ResolveFromLoginHintTokenAsync(
        string loginHintToken,
        CancellationToken cancellationToken)
    {
        try
        {
            var handler = new JwtSecurityTokenHandler();
            if (!handler.CanReadToken(loginHintToken))
            {
                _logger.LogWarning("login_hint_token is not a valid JWT");
                return null;
            }

            // Get signing keys for validation
            var signingKeyInfos = await _signingCredentialStore.GetValidationKeysAsync(cancellationToken);
            if (signingKeyInfos == null || !signingKeyInfos.Any())
            {
                _logger.LogWarning("No validation keys available for login_hint_token validation");
                return null;
            }

            var issuer = GetIssuer();

            var validationParameters = new TokenValidationParameters
            {
                ValidIssuer = issuer,
                ValidateIssuer = true,
                ValidateAudience = false, // login_hint_token may not have specific audience
                ValidateLifetime = true,
                ValidateIssuerSigningKey = true,
                IssuerSigningKeys = signingKeyInfos.Select(k => k.Key),
                ClockSkew = TimeSpan.FromMinutes(5)
            };

            var principal = handler.ValidateToken(loginHintToken, validationParameters, out var validatedToken);

            if (validatedToken is not JwtSecurityToken jwt)
            {
                _logger.LogWarning("login_hint_token validation did not produce a JWT");
                return null;
            }

            // Extract subject from the token
            var subjectId = principal.FindFirst(JwtRegisteredClaimNames.Sub)?.Value
                ?? principal.FindFirst("sub")?.Value;

            if (string.IsNullOrEmpty(subjectId))
            {
                _logger.LogWarning("login_hint_token does not contain a subject claim");
                return null;
            }

            // Verify the user exists
            var user = await _userService.FindByIdAsync(subjectId, cancellationToken);
            if (user == null)
            {
                _logger.LogWarning("User {SubjectId} from login_hint_token not found", subjectId);
                return null;
            }

            return subjectId;
        }
        catch (SecurityTokenExpiredException)
        {
            _logger.LogWarning("login_hint_token has expired");
            return null;
        }
        catch (SecurityTokenInvalidSignatureException)
        {
            _logger.LogWarning("login_hint_token has invalid signature");
            return null;
        }
        catch (SecurityTokenException ex)
        {
            _logger.LogWarning(ex, "login_hint_token validation failed");
            return null;
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Unexpected error validating login_hint_token");
            return null;
        }
    }

    /// <summary>
    /// Resolves user from an id_token_hint.
    /// The id_token_hint is a previously issued ID token that identifies the user.
    /// Per OIDC Core spec, the server SHOULD validate the token signature but
    /// MAY accept expired tokens for hint purposes.
    /// </summary>
    private async Task<string?> ResolveFromIdTokenHintAsync(
        string idTokenHint,
        string clientId,
        CancellationToken cancellationToken)
    {
        try
        {
            var handler = new JwtSecurityTokenHandler();
            if (!handler.CanReadToken(idTokenHint))
            {
                _logger.LogWarning("id_token_hint is not a valid JWT");
                return null;
            }

            // Get signing keys for validation
            var signingKeyInfos = await _signingCredentialStore.GetValidationKeysAsync(cancellationToken);
            if (signingKeyInfos == null || !signingKeyInfos.Any())
            {
                _logger.LogWarning("No validation keys available for id_token_hint validation");
                return null;
            }

            var issuer = GetIssuer();

            // For id_token_hint, we validate signature but allow expired tokens
            // (the token is just a hint about who the user is)
            var validationParameters = new TokenValidationParameters
            {
                ValidIssuer = issuer,
                ValidateIssuer = true,
                ValidAudience = clientId, // ID token audience must be the client
                ValidateAudience = true,
                ValidateLifetime = false, // Allow expired tokens for hint purposes
                ValidateIssuerSigningKey = true,
                IssuerSigningKeys = signingKeyInfos.Select(k => k.Key),
                ClockSkew = TimeSpan.FromMinutes(5)
            };

            var principal = handler.ValidateToken(idTokenHint, validationParameters, out var validatedToken);

            if (validatedToken is not JwtSecurityToken jwt)
            {
                _logger.LogWarning("id_token_hint validation did not produce a JWT");
                return null;
            }

            // Extract subject from the token
            var subjectId = principal.FindFirst(JwtRegisteredClaimNames.Sub)?.Value
                ?? principal.FindFirst("sub")?.Value;

            if (string.IsNullOrEmpty(subjectId))
            {
                _logger.LogWarning("id_token_hint does not contain a subject claim");
                return null;
            }

            // Verify the user exists
            var user = await _userService.FindByIdAsync(subjectId, cancellationToken);
            if (user == null)
            {
                _logger.LogWarning("User {SubjectId} from id_token_hint not found", subjectId);
                return null;
            }

            return subjectId;
        }
        catch (SecurityTokenInvalidAudienceException)
        {
            _logger.LogWarning("id_token_hint has invalid audience (not issued to requesting client)");
            return null;
        }
        catch (SecurityTokenInvalidSignatureException)
        {
            _logger.LogWarning("id_token_hint has invalid signature");
            return null;
        }
        catch (SecurityTokenException ex)
        {
            _logger.LogWarning(ex, "id_token_hint validation failed");
            return null;
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Unexpected error validating id_token_hint");
            return null;
        }
    }

    private string GetIssuer()
    {
        var baseUrl = _configuration["Oluso:IssuerUri"]
            ?? throw new InvalidOperationException("IssuerUri not configured");

        if (_tenantContext.HasTenant)
        {
            return $"{baseUrl.TrimEnd('/')}/{_tenantContext.Tenant!.Identifier}";
        }

        return baseUrl;
    }

    private static string GenerateAuthReqId()
    {
        var bytes = RandomNumberGenerator.GetBytes(32);
        return Convert.ToBase64String(bytes)
            .Replace("+", "-")
            .Replace("/", "_")
            .TrimEnd('=');
    }

    private static CibaTokenDeliveryMode ParseDeliveryMode(string? mode)
    {
        return mode?.ToLowerInvariant() switch
        {
            "ping" => CibaTokenDeliveryMode.Ping,
            "push" => CibaTokenDeliveryMode.Push,
            _ => CibaTokenDeliveryMode.Poll
        };
    }
}
