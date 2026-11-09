using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using System.Text.Json;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using Microsoft.IdentityModel.Tokens;
using Oluso.Core;
using Oluso.Core.Authentication;
using Oluso.Core.Domain.Interfaces;
using Oluso.Core.Protocols;
using Oluso.Core.Protocols.Grants;
using Oluso.Core.Protocols.Models;
using Oluso.Core.Protocols.SenderConstraint;
using Oluso.Core.Services;

namespace Oluso.Protocols.Grants;

/// <summary>
/// Handles token exchange grant type (RFC 8693)
/// Used for impersonation, delegation, and token type conversion, and for one-time tickets
/// (<see cref="OidcConstants.TokenTypes.Ticket"/>): issued to a client for an audience client it lists
/// in <see cref="OidcConstants.TokenExchangeClientProperties.Audiences"/>, redeemed once by that
/// audience (a confidential client) for its own tokens.
/// </summary>
/// <remarks>
/// Rules that apply to every exchange: a sender-constrained (DPoP-bound) subject or actor token is
/// exchanged only with a DPoP proof signed by its key; the issued token never inherits the subject's
/// <c>cnf</c>, <c>client_id</c> or <c>azp</c>; a refresh token is exchanged only by the client it was
/// issued to; an <c>audience</c> must be one the requesting client lists and an enabled client of its tenant.
/// </remarks>
public class TokenExchangeGrantHandler : IGrantHandler
{
    /// <summary>Subject-token claims never carried into the issued token.</summary>
    private static readonly HashSet<string> NotCopied = new(StringComparer.Ordinal)
    {
        "iat", "exp", "nbf", "jti", "iss", "aud", "typ", "cnf", "client_id", "azp", "scope",
    };

    private readonly ISigningCredentialStore _signingCredentialStore;
    private readonly IPersistedGrantStore _grantStore;
    private readonly IIssuerResolver _issuerResolver;
    private readonly ILogger<TokenExchangeGrantHandler> _logger;
    private readonly IClientStore? _clients;
    private readonly IProfileService? _profiles;
    private readonly IActionTokenService? _tickets;
    private readonly IConfiguration? _configuration;
    private readonly IClientUserAccess _clientUserAccess;

    public string GrantType => OidcConstants.GrantTypes.TokenExchange;

    public TokenExchangeGrantHandler(
        ISigningCredentialStore signingCredentialStore,
        IPersistedGrantStore grantStore,
        IIssuerResolver issuerResolver,
        IClientUserAccess clientUserAccess,
        ILogger<TokenExchangeGrantHandler> logger,
        IClientStore? clients = null,
        IProfileService? profiles = null,
        IActionTokenService? tickets = null,
        IConfiguration? configuration = null)
    {
        _signingCredentialStore = signingCredentialStore;
        _clientUserAccess = clientUserAccess;
        _grantStore = grantStore;
        _issuerResolver = issuerResolver;
        _logger = logger;
        _clients = clients;
        _profiles = profiles;
        _tickets = tickets;
        _configuration = configuration;
    }

    public async Task<GrantResult> HandleAsync(TokenRequest request, CancellationToken cancellationToken = default)
    {
        var result = await ExchangeAsync(request, cancellationToken);
        if (!result.IsValid || string.IsNullOrEmpty(result.SubjectId) || request.Client == null)
        {
            return result;
        }

        // Every way out of an exchange that speaks for a user (a token, a ticket, a redeemed ticket) is held to the
        // requesting client's user restrictions, in the tenant the new token is for.
        var tenantId = result.Claims.TryGetValue(OidcConstants.StandardClaims.TenantId, out var tenant)
            ? tenant?.ToString()
            : request.Client.TenantId;
        var denial = await _clientUserAccess.FindDenialAsync(
            request.Client.ClientId, ClientUserRestrictions.From(request.Client), result.SubjectId, tenantId, cancellationToken);
        return denial == null ? result : GrantResult.Failure(OidcConstants.Errors.AccessDenied, denial);
    }

    private async Task<GrantResult> ExchangeAsync(TokenRequest request, CancellationToken cancellationToken)
    {
        if (string.IsNullOrEmpty(request.SubjectToken))
        {
            return GrantResult.Failure(OidcConstants.Errors.InvalidRequest, "subject_token is required");
        }

        if (string.IsNullOrEmpty(request.SubjectTokenType))
        {
            return GrantResult.Failure(OidcConstants.Errors.InvalidRequest, "subject_token_type is required");
        }

        if (request.SubjectTokenType == OidcConstants.TokenTypes.Ticket)
        {
            return await RedeemTicketAsync(request, cancellationToken);
        }

        // Validate subject_token_type
        var validTokenTypes = new[]
        {
            OidcConstants.TokenTypes.AccessToken,
            OidcConstants.TokenTypes.RefreshToken,
            OidcConstants.TokenTypes.IdToken,
            OidcConstants.TokenTypes.Jwt,
        };

        if (!validTokenTypes.Contains(request.SubjectTokenType))
        {
            return GrantResult.Failure(
                OidcConstants.Errors.InvalidRequest,
                $"Unsupported subject_token_type: {request.SubjectTokenType}");
        }

        // Determine the requested token type for response
        var requestedTokenType = string.IsNullOrEmpty(request.RequestedTokenType)
            ? OidcConstants.TokenTypes.AccessToken
            : request.RequestedTokenType;
        if (requestedTokenType is not (OidcConstants.TokenTypes.AccessToken or OidcConstants.TokenTypes.Jwt or OidcConstants.TokenTypes.Ticket))
        {
            return GrantResult.Failure(OidcConstants.Errors.InvalidRequest,
                $"Unsupported requested_token_type: {requestedTokenType}");
        }
        var wantsTicket = requestedTokenType == OidcConstants.TokenTypes.Ticket;
        var client = request.Client!;

        // Validate and extract claims from subject token
        TokenValidationResult? subjectValidation;
        try
        {
            subjectValidation = await ValidateTokenAsync(
                request.SubjectToken,
                request.SubjectTokenType,
                cancellationToken);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Subject token validation failed");
            return GrantResult.Failure(OidcConstants.Errors.InvalidGrant, "Invalid subject token");
        }

        if (!subjectValidation.IsValid)
        {
            return GrantResult.Failure(OidcConstants.Errors.InvalidGrant, subjectValidation.ErrorMessage ?? "Invalid subject token");
        }

        // A refresh token is a credential of the client it was issued to (RFC 6749 §6).
        if (request.SubjectTokenType == OidcConstants.TokenTypes.RefreshToken
            && !string.Equals(subjectValidation.ClientId, client.ClientId, StringComparison.Ordinal))
        {
            return GrantResult.Failure(OidcConstants.Errors.InvalidGrant, "The refresh token was issued to another client");
        }

        // A DPoP-bound token stays usable only by the holder of its key, here too (RFC 9449 §7):
        // the exchange must carry a proof signed by that key.
        var boundProblem = BoundKeyProblem(subjectValidation.Jkt, request.DPoPKeyThumbprint, "subject");
        if (boundProblem != null)
        {
            return GrantResult.Failure(OidcConstants.Errors.InvalidGrant, boundProblem);
        }

        // Audience (RFC 8693 §2.1): only one the requesting client lists, and an enabled client of its tenant.
        request.Raw.TryGetValue("audience", out var audience);
        audience = string.IsNullOrWhiteSpace(audience) ? null : audience.Trim();
        if (wantsTicket && audience == null)
        {
            return GrantResult.Failure(OidcConstants.Errors.InvalidRequest, "audience is required for a ticket");
        }
        if (audience != null)
        {
            var audienceProblem = await AudienceProblemAsync(client, audience, cancellationToken);
            if (audienceProblem != null)
            {
                return GrantResult.Failure(OidcConstants.Errors.InvalidTarget, audienceProblem);
            }
        }

        if (wantsTicket)
        {
            if (request.SubjectTokenType is not (OidcConstants.TokenTypes.AccessToken or OidcConstants.TokenTypes.Jwt))
            {
                return GrantResult.Failure(OidcConstants.Errors.InvalidRequest, "A ticket is issued for an access token");
            }
            if (!string.IsNullOrEmpty(request.ActorToken))
            {
                return GrantResult.Failure(OidcConstants.Errors.InvalidRequest, "actor_token is not supported for a ticket");
            }
            // The ticket speaks for the requesting client: it must hold the user's token itself.
            if (!string.Equals(subjectValidation.ClientId, client.ClientId, StringComparison.Ordinal))
            {
                return GrantResult.Failure(OidcConstants.Errors.InvalidGrant, "The subject token was issued to another client");
            }
            if (string.IsNullOrEmpty(subjectValidation.SubjectId))
            {
                return GrantResult.Failure(OidcConstants.Errors.InvalidGrant, "The subject token names no user");
            }
        }

        var claims = new Dictionary<string, object>();
        var subjectId = subjectValidation.SubjectId;

        // Copy relevant claims from subject token. Never its sender constraint (cnf) or the client it
        // was issued to (client_id, azp): the issued token gets its own.
        foreach (var claim in subjectValidation.Claims)
        {
            if (NotCopied.Contains(claim.Key))
                continue;

            claims[claim.Key] = claim.Value;
        }

        // Handle actor_token for delegation (RFC 8693 Section 4.1)
        if (!string.IsNullOrEmpty(request.ActorToken))
        {
            if (string.IsNullOrEmpty(request.ActorTokenType))
            {
                return GrantResult.Failure(OidcConstants.Errors.InvalidRequest,
                    "actor_token_type is required when actor_token is present");
            }

            TokenValidationResult? actorValidation;
            try
            {
                actorValidation = await ValidateTokenAsync(
                    request.ActorToken,
                    request.ActorTokenType,
                    cancellationToken);
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Actor token validation failed");
                return GrantResult.Failure(OidcConstants.Errors.InvalidGrant, "Invalid actor token");
            }

            if (!actorValidation.IsValid)
            {
                return GrantResult.Failure(OidcConstants.Errors.InvalidGrant,
                    actorValidation.ErrorMessage ?? "Invalid actor token");
            }

            if (request.ActorTokenType == OidcConstants.TokenTypes.RefreshToken
                && !string.Equals(actorValidation.ClientId, client.ClientId, StringComparison.Ordinal))
            {
                return GrantResult.Failure(OidcConstants.Errors.InvalidGrant, "The actor refresh token was issued to another client");
            }

            var actorBoundProblem = BoundKeyProblem(actorValidation.Jkt, request.DPoPKeyThumbprint, "actor");
            if (actorBoundProblem != null)
            {
                return GrantResult.Failure(OidcConstants.Errors.InvalidGrant, actorBoundProblem);
            }

            // Add "act" claim for delegation chain (RFC 8693 Section 4.1)
            var actClaim = new Dictionary<string, object>
            {
                ["sub"] = actorValidation.SubjectId ?? "unknown"
            };

            // If actor token already has an act claim, chain it
            if (actorValidation.Claims.TryGetValue("act", out var existingAct))
            {
                actClaim["act"] = existingAct;
            }

            claims["act"] = actClaim;

            _logger.LogInformation(
                "Token exchange with delegation: subject={Subject}, actor={Actor}",
                subjectId, actorValidation.SubjectId);
        }

        // Determine effective scopes
        var effectiveScopes = request.RequestedScopes.Any()
            ? request.RequestedScopes.ToList()
            : subjectValidation.Scopes.ToList();

        // Validate requested scopes are subset of subject token scopes (always for a ticket; otherwise
        // when the subject has scopes)
        if (request.ScopesAsked.Any() && (wantsTicket || subjectValidation.Scopes.Any()))
        {
            var invalidScopes = request.ScopesAsked.Except(subjectValidation.Scopes).ToList();
            if (invalidScopes.Any())
            {
                return GrantResult.Failure(
                    OidcConstants.Errors.InvalidScope,
                    $"Requested scope(s) exceed subject token scopes: {string.Join(", ", invalidScopes)}");
            }
        }

        if (wantsTicket)
        {
            return await IssueTicketAsync(client, subjectValidation, audience!, effectiveScopes, claims, cancellationToken);
        }

        _logger.LogInformation(
            "Token exchange successful: subject={Subject}, scopes={Scopes}, requestedType={RequestedType}",
            subjectId, string.Join(" ", effectiveScopes), requestedTokenType);

        var customData = new Dictionary<string, object>
        {
            ["issued_token_type"] = requestedTokenType
        };
        if (audience != null)
        {
            customData[ExchangeTickets.AudienceCustomDataKey] = audience;
        }

        return new GrantResult
        {
            SubjectId = subjectId,
            SessionId = subjectValidation.SessionId,
            Scopes = effectiveScopes,
            Claims = claims,
            CustomData = customData
        };
    }

    private static string? BoundKeyProblem(string? boundJkt, string? presentedJkt, string which)
    {
        if (string.IsNullOrEmpty(boundJkt)) return null;
        if (string.IsNullOrEmpty(presentedJkt))
            return $"The {which} token is DPoP-bound; present a DPoP proof signed by its key.";
        return string.Equals(boundJkt, presentedJkt, StringComparison.Ordinal)
            ? null
            : $"The DPoP proof is not signed by the key the {which} token is bound to.";
    }

    /// <summary>Why <paramref name="audience"/> may not be named by <paramref name="client"/>, or null when it may.</summary>
    private async Task<string?> AudienceProblemAsync(ValidatedClient client, string audience, CancellationToken cancellationToken)
    {
        var allowed = client.Properties.TryGetValue(OidcConstants.TokenExchangeClientProperties.Audiences, out var listed)
            ? (listed ?? string.Empty).Split(' ', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            : Array.Empty<string>();
        if (!allowed.Contains(audience, StringComparer.Ordinal))
            return $"This client may not exchange tokens for audience '{audience}'.";

        if (_clients == null)
            return "Audience clients cannot be resolved.";
        var target = await _clients.FindClientByIdAsync(audience, cancellationToken);
        if (target == null || !target.Enabled)
            return $"Audience '{audience}' is not an enabled client.";
        if (!string.Equals(target.TenantId ?? string.Empty, client.TenantId ?? string.Empty, StringComparison.Ordinal))
            return $"Audience '{audience}' is not a client of this tenant.";
        return null;
    }

    private int TicketLifetimeSeconds()
    {
        var configured = _configuration?.GetValue<int?>(ExchangeTickets.LifetimeConfigurationKey) ?? ExchangeTickets.MaxLifetimeSeconds;
        return Math.Clamp(configured, 1, ExchangeTickets.MaxLifetimeSeconds);
    }

    private async Task<GrantResult> IssueTicketAsync(
        ValidatedClient client,
        TokenValidationResult subject,
        string audience,
        List<string> scopes,
        Dictionary<string, object> subjectClaims,
        CancellationToken cancellationToken)
    {
        if (_tickets == null)
        {
            return GrantResult.Failure(OidcConstants.Errors.ServerError, "Tickets are not available on this server");
        }

        // A ticket grants a one-off sign-in at the audience, never offline access.
        var ticketScopes = scopes.Where(s => s != OidcConstants.Scopes.OfflineAccess).Distinct(StringComparer.Ordinal).ToList();
        if (ticketScopes.Count == 0)
        {
            return GrantResult.Failure(OidcConstants.Errors.InvalidScope, "A ticket needs at least one scope");
        }

        var lifetime = TicketLifetimeSeconds();
        var tokenTenant = subjectClaims.TryGetValue(OidcConstants.StandardClaims.TenantId, out var tid) ? tid?.ToString() : null;
        var jti = await _tickets.IssueAsync(
            client.TenantId ?? tokenTenant ?? PlatformConstants.TenantId,
            subject.SubjectId!,
            ExchangeTickets.Action,
            audience,
            TimeSpan.FromSeconds(lifetime) + ExchangeTickets.RedeemClockSkew,
            cancellationToken);

        _logger.LogInformation(
            "Token exchange ticket issued: subject={Subject}, client={Client}, audience={Audience}, scopes={Scopes}",
            subject.SubjectId, client.ClientId, audience, string.Join(" ", ticketScopes));

        var claims = new Dictionary<string, object>();
        if (!string.IsNullOrEmpty(tokenTenant))
            claims[OidcConstants.StandardClaims.TenantId] = tokenTenant;

        return new GrantResult
        {
            SubjectId = subject.SubjectId,
            Scopes = ticketScopes,
            Claims = claims,
            CustomData = new Dictionary<string, object>
            {
                ["issued_token_type"] = OidcConstants.TokenTypes.Ticket,
                [ExchangeTickets.CustomDataKey] = new ExchangeTicketDescriptor(jti, audience, lifetime),
            }
        };
    }

    /// <summary>
    /// Redeems a ticket (<see cref="OidcConstants.TokenTypes.Ticket"/>) for the authenticated audience
    /// client's own tokens. The ticket must be addressed to that client, unexpired, and unused: the
    /// first redemption consumes it atomically, any later one fails with <c>invalid_grant</c>.
    /// </summary>
    private async Task<GrantResult> RedeemTicketAsync(TokenRequest request, CancellationToken cancellationToken)
    {
        var client = request.Client!;
        if (client.AuthenticationMethod == ClientAuthenticationMethod.None)
        {
            return GrantResult.Failure(OidcConstants.Errors.UnauthorizedClient, "Only a confidential client can redeem a ticket");
        }
        if (!string.IsNullOrEmpty(request.ActorToken))
        {
            return GrantResult.Failure(OidcConstants.Errors.InvalidRequest, "actor_token is not supported when redeeming a ticket");
        }
        if (!string.IsNullOrEmpty(request.RequestedTokenType) && request.RequestedTokenType != OidcConstants.TokenTypes.AccessToken)
        {
            return GrantResult.Failure(OidcConstants.Errors.InvalidRequest, "A ticket is redeemed for an access token");
        }
        if (_tickets == null || _profiles == null || _clients == null)
        {
            return GrantResult.Failure(OidcConstants.Errors.ServerError, "Tickets are not available on this server");
        }

        JwtSecurityToken ticket;
        try
        {
            var issuer = await _issuerResolver.GetIssuerAsync(cancellationToken);
            var keys = await _signingCredentialStore.GetValidationKeysAsync(cancellationToken);
            if (keys == null || !keys.Any())
                return GrantResult.Failure(OidcConstants.Errors.InvalidGrant, "Invalid ticket");

            var handler = new JwtSecurityTokenHandler { MapInboundClaims = false };
            handler.ValidateToken(request.SubjectToken, new TokenValidationParameters
            {
                ValidIssuer = issuer,
                ValidateIssuer = true,
                ValidAudience = client.ClientId,
                ValidateAudience = true,
                ValidateLifetime = true,
                RequireExpirationTime = true,
                ValidateIssuerSigningKey = true,
                IssuerSigningKeys = keys.Select(k => k.Key),
                ClockSkew = ExchangeTickets.RedeemClockSkew,
                ValidTypes = new[] { OidcConstants.JwtTypes.Ticket },
            }, out var validated);
            ticket = (JwtSecurityToken)validated;
        }
        catch (Exception ex) when (ex is SecurityTokenException or ArgumentException or InvalidCastException)
        {
            _logger.LogInformation("Ticket redemption by {Client} refused: {Message}", client.ClientId, ex.Message);
            return GrantResult.Failure(OidcConstants.Errors.InvalidGrant, "Invalid or expired ticket");
        }

        var subjectId = ticket.Subject;
        var jti = ticket.Id;
        var ticketTenant = ticket.Claims.FirstOrDefault(c => c.Type == OidcConstants.StandardClaims.TenantId)?.Value;
        if (string.IsNullOrEmpty(subjectId) || string.IsNullOrEmpty(jti))
        {
            return GrantResult.Failure(OidcConstants.Errors.InvalidGrant, "Invalid ticket");
        }
        if (!string.IsNullOrEmpty(ticketTenant) && !string.IsNullOrEmpty(client.TenantId)
            && !string.Equals(ticketTenant, client.TenantId, StringComparison.Ordinal))
        {
            return GrantResult.Failure(OidcConstants.Errors.InvalidGrant, "The ticket belongs to another tenant");
        }

        // Single use: the first redemption consumes the ticket's record atomically.
        var consumed = await _tickets.RedeemAsync(jti, cancellationToken);
        if (consumed == null)
        {
            _logger.LogWarning("Ticket replayed or unknown (client {Client}, subject {Subject})", client.ClientId, subjectId);
            return GrantResult.Failure(OidcConstants.Errors.InvalidGrant, "The ticket has already been used or is unknown");
        }
        if (consumed.Action != ExchangeTickets.Action
            || !string.Equals(consumed.SubjectId, client.ClientId, StringComparison.Ordinal)
            || !string.Equals(consumed.UserId, subjectId, StringComparison.Ordinal))
        {
            return GrantResult.Failure(OidcConstants.Errors.InvalidGrant, "Invalid ticket");
        }

        var ticketScopes = ticket.Claims.Where(c => c.Type == "scope")
            .SelectMany(c => c.Value.Split(' ', StringSplitOptions.RemoveEmptyEntries))
            .Distinct(StringComparer.Ordinal)
            .ToList();
        List<string> scopes;
        if (request.ScopesAsked.Any())
        {
            var beyond = request.ScopesAsked.Except(ticketScopes).ToList();
            if (beyond.Count > 0)
            {
                return GrantResult.Failure(OidcConstants.Errors.InvalidScope,
                    $"Requested scope(s) exceed the ticket's scopes: {string.Join(", ", beyond)}");
            }
            scopes = request.RequestedScopes.ToList();
        }
        else
        {
            scopes = ticketScopes.Where(s => client.AllowedScopes.Contains(s)).ToList();
        }
        scopes.Remove(OidcConstants.Scopes.OfflineAccess);

        if (!await _profiles.IsActiveAsync(subjectId, cancellationToken))
        {
            return GrantResult.Failure(OidcConstants.Errors.InvalidGrant, "User is not active");
        }

        var fullClient = await _clients.FindClientByIdAsync(client.ClientId, cancellationToken)
            ?? new Core.Domain.Entities.Client { ClientId = client.ClientId };
        var profileClaims = await _profiles.GetProfileClaimsAsync(
            subjectId, fullClient, scopes, "TokenEndpoint", "oidc", request.Resource, null, cancellationToken);
        var claims = new Dictionary<string, object>(profileClaims);
        if (!string.IsNullOrEmpty(ticketTenant)
            && claims.TryGetValue(OidcConstants.StandardClaims.TenantId, out var profileTenant)
            && !string.Equals(profileTenant?.ToString(), ticketTenant, StringComparison.Ordinal))
        {
            return GrantResult.Failure(OidcConstants.Errors.InvalidGrant, "The ticket's user is no longer in its tenant");
        }
        if (!string.IsNullOrEmpty(ticketTenant))
            claims[OidcConstants.StandardClaims.TenantId] = ticketTenant;

        _logger.LogInformation("Ticket redeemed: subject={Subject}, client={Client}, scopes={Scopes}",
            subjectId, client.ClientId, string.Join(" ", scopes));

        return new GrantResult
        {
            SubjectId = subjectId,
            Scopes = scopes,
            Claims = claims,
            CustomData = new Dictionary<string, object>
            {
                ["issued_token_type"] = OidcConstants.TokenTypes.AccessToken
            }
        };
    }

    private async Task<TokenValidationResult> ValidateTokenAsync(
        string token,
        string tokenType,
        CancellationToken cancellationToken)
    {
        return tokenType switch
        {
            OidcConstants.TokenTypes.AccessToken or OidcConstants.TokenTypes.Jwt =>
                await ValidateJwtTokenAsync(token, cancellationToken),

            OidcConstants.TokenTypes.IdToken =>
                await ValidateIdTokenAsync(token, cancellationToken),

            OidcConstants.TokenTypes.RefreshToken =>
                await ValidateRefreshTokenAsync(token, cancellationToken),

            _ => TokenValidationResult.Failed($"Unsupported token type: {tokenType}")
        };
    }

    private async Task<TokenValidationResult> ValidateJwtTokenAsync(
        string token,
        CancellationToken cancellationToken)
    {
        var issuer = await _issuerResolver.GetIssuerAsync(cancellationToken);

        // Get signing keys for validation
        var signingKeyInfos = await _signingCredentialStore.GetValidationKeysAsync(cancellationToken);
        if (signingKeyInfos == null || !signingKeyInfos.Any())
        {
            return TokenValidationResult.Failed("No validation keys available");
        }

        var validationParameters = new TokenValidationParameters
        {
            ValidIssuer = issuer,
            ValidateIssuer = true,
            ValidateAudience = false, // Access tokens may have various audiences
            ValidateLifetime = true,
            ValidateIssuerSigningKey = true,
            IssuerSigningKeys = signingKeyInfos.Select(k => k.Key),
            ClockSkew = TimeSpan.FromMinutes(1),
            // Tickets and logout tokens are signed with the same keys but are not exchangeable tokens.
            TypeValidator = Oluso.Protocols.Validation.AccessTokenTypes.Validate
        };

        var handler = new JwtSecurityTokenHandler();

        try
        {
            var principal = handler.ValidateToken(token, validationParameters, out var validatedToken);

            if (validatedToken is not JwtSecurityToken jwt)
            {
                return TokenValidationResult.Failed("Invalid JWT token");
            }

            var subjectId = principal.FindFirst(ClaimTypes.NameIdentifier)?.Value
                ?? principal.FindFirst(JwtRegisteredClaimNames.Sub)?.Value;

            var sessionId = principal.FindFirst(OidcConstants.StandardClaims.SessionId)?.Value;

            var scopes = principal.FindAll("scope").Select(c => c.Value).ToList();
            if (scopes.Count == 1 && scopes[0].Contains(' '))
            {
                // Space-separated scope claim
                scopes = scopes[0].Split(' ', StringSplitOptions.RemoveEmptyEntries).ToList();
            }

            var claims = new Dictionary<string, object>();
            foreach (var claim in principal.Claims)
            {
                if (!claims.ContainsKey(claim.Type))
                {
                    claims[claim.Type] = claim.Value;
                }
            }

            // The raw payload, not the mapped principal: the sender constraint and the client the token was issued to.
            var cnf = jwt.Payload.TryGetValue("cnf", out var cnfValue) && cnfValue != null
                ? TokenConfirmation.Parse(cnfValue is string s ? s : JsonSerializer.Serialize(cnfValue))
                : TokenConfirmation.None;
            var clientId = (jwt.Payload.TryGetValue("client_id", out var cid) ? cid?.ToString() : null)
                ?? (jwt.Payload.TryGetValue("azp", out var azp) ? azp?.ToString() : null);

            return TokenValidationResult.Success(subjectId, sessionId, scopes, claims, clientId, cnf.Jkt);
        }
        catch (SecurityTokenExpiredException)
        {
            return TokenValidationResult.Failed("Token has expired");
        }
        catch (SecurityTokenInvalidSignatureException)
        {
            return TokenValidationResult.Failed("Invalid token signature");
        }
        catch (SecurityTokenException ex)
        {
            return TokenValidationResult.Failed($"Token validation failed: {ex.Message}");
        }
    }

    private Task<TokenValidationResult> ValidateIdTokenAsync(
        string token,
        CancellationToken cancellationToken)
    {
        // ID tokens follow the same validation as JWT access tokens
        return ValidateJwtTokenAsync(token, cancellationToken);
    }

    private async Task<TokenValidationResult> ValidateRefreshTokenAsync(
        string token,
        CancellationToken cancellationToken)
    {
        // Refresh tokens are opaque - look them up in the grant store
        var grant = await _grantStore.GetAsync(token, cancellationToken);

        if (grant == null)
        {
            return TokenValidationResult.Failed("Invalid refresh token");
        }

        if (grant.Type != "refresh_token")
        {
            return TokenValidationResult.Failed("Token is not a refresh token");
        }

        if (grant.Expiration.HasValue && grant.Expiration.Value < DateTime.UtcNow)
        {
            return TokenValidationResult.Failed("Refresh token has expired");
        }

        // Parse refresh token data
        RefreshTokenData? tokenData;
        try
        {
            tokenData = JsonSerializer.Deserialize<RefreshTokenData>(grant.Data);
        }
        catch
        {
            return TokenValidationResult.Failed("Invalid refresh token data");
        }

        if (tokenData == null)
        {
            return TokenValidationResult.Failed("Invalid refresh token data");
        }

        var claims = tokenData.ReadClaims();

        return TokenValidationResult.Success(
            grant.SubjectId,
            grant.SessionId,
            tokenData.Scopes.ToList(),
            claims,
            grant.ClientId,
            tokenData.DPoPJkt);
    }

    private class TokenValidationResult
    {
        public bool IsValid { get; private set; }
        public string? ErrorMessage { get; private set; }
        public string? SubjectId { get; private set; }
        public string? SessionId { get; private set; }
        public IReadOnlyList<string> Scopes { get; private set; } = Array.Empty<string>();
        public IReadOnlyDictionary<string, object> Claims { get; private set; } = new Dictionary<string, object>();

        /// <summary>The client the token was issued to (client_id / azp, or the refresh token's client).</summary>
        public string? ClientId { get; private set; }

        /// <summary>The DPoP key thumbprint the token is bound to, if any.</summary>
        public string? Jkt { get; private set; }

        public static TokenValidationResult Success(
            string? subjectId,
            string? sessionId,
            List<string> scopes,
            Dictionary<string, object> claims,
            string? clientId,
            string? jkt) => new()
        {
            IsValid = true,
            SubjectId = subjectId,
            SessionId = sessionId,
            Scopes = scopes,
            Claims = claims,
            ClientId = clientId,
            Jkt = jkt
        };

        public static TokenValidationResult Failed(string error) => new()
        {
            IsValid = false,
            ErrorMessage = error
        };
    }
}
