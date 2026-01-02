using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using System.Text.Json;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using Microsoft.IdentityModel.Tokens;
using Oluso.Core.Domain.Interfaces;
using Oluso.Core.Protocols;
using Oluso.Core.Protocols.Grants;
using Oluso.Core.Protocols.Models;

namespace Oluso.Protocols.Grants;

/// <summary>
/// Handles token exchange grant type (RFC 8693)
/// Used for impersonation, delegation, and token type conversion
/// </summary>
public class TokenExchangeGrantHandler : IGrantHandler
{
    private readonly ISigningCredentialStore _signingCredentialStore;
    private readonly IPersistedGrantStore _grantStore;
    private readonly ITenantContext _tenantContext;
    private readonly IConfiguration _configuration;
    private readonly ILogger<TokenExchangeGrantHandler> _logger;

    public string GrantType => OidcConstants.GrantTypes.TokenExchange;

    public TokenExchangeGrantHandler(
        ISigningCredentialStore signingCredentialStore,
        IPersistedGrantStore grantStore,
        ITenantContext tenantContext,
        IConfiguration configuration,
        ILogger<TokenExchangeGrantHandler> logger)
    {
        _signingCredentialStore = signingCredentialStore;
        _grantStore = grantStore;
        _tenantContext = tenantContext;
        _configuration = configuration;
        _logger = logger;
    }

    public async Task<GrantResult> HandleAsync(TokenRequest request, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrEmpty(request.SubjectToken))
        {
            return GrantResult.Failure(OidcConstants.Errors.InvalidRequest, "subject_token is required");
        }

        if (string.IsNullOrEmpty(request.SubjectTokenType))
        {
            return GrantResult.Failure(OidcConstants.Errors.InvalidRequest, "subject_token_type is required");
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

        var claims = new Dictionary<string, object>();
        var subjectId = subjectValidation.SubjectId;

        // Copy relevant claims from subject token
        foreach (var claim in subjectValidation.Claims)
        {
            // Don't copy certain claims that will be regenerated
            if (claim.Key is "iat" or "exp" or "nbf" or "jti" or "iss" or "aud")
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

        // Validate requested scopes are subset of subject token scopes (if subject has scopes)
        if (request.RequestedScopes.Any() && subjectValidation.Scopes.Any())
        {
            var invalidScopes = request.RequestedScopes.Except(subjectValidation.Scopes).ToList();
            if (invalidScopes.Any())
            {
                return GrantResult.Failure(
                    OidcConstants.Errors.InvalidScope,
                    $"Requested scope(s) exceed subject token scopes: {string.Join(", ", invalidScopes)}");
            }
        }

        // Determine the requested token type for response
        var requestedTokenType = request.RequestedTokenType ?? OidcConstants.TokenTypes.AccessToken;

        _logger.LogInformation(
            "Token exchange successful: subject={Subject}, scopes={Scopes}, requestedType={RequestedType}",
            subjectId, string.Join(" ", effectiveScopes), requestedTokenType);

        return new GrantResult
        {
            SubjectId = subjectId,
            SessionId = subjectValidation.SessionId,
            Scopes = effectiveScopes,
            Claims = claims,
            CustomData = new Dictionary<string, object>
            {
                ["issued_token_type"] = requestedTokenType
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
        var issuer = GetIssuer();

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
            ClockSkew = TimeSpan.FromMinutes(1)
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
            if (!scopes.Any())
            {
                // Try space-separated scope claim
                var scopeClaim = principal.FindFirst("scope")?.Value;
                if (!string.IsNullOrEmpty(scopeClaim))
                {
                    scopes = scopeClaim.Split(' ', StringSplitOptions.RemoveEmptyEntries).ToList();
                }
            }

            var claims = new Dictionary<string, object>();
            foreach (var claim in principal.Claims)
            {
                if (!claims.ContainsKey(claim.Type))
                {
                    claims[claim.Type] = claim.Value;
                }
            }

            return TokenValidationResult.Success(subjectId, sessionId, scopes, claims);
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

        var claims = tokenData.Claims?.ToDictionary(c => c.Key, c => (object)c.Value)
            ?? new Dictionary<string, object>();

        return TokenValidationResult.Success(
            grant.SubjectId,
            grant.SessionId,
            tokenData.Scopes.ToList(),
            claims);
    }

    private string GetIssuer()
    {
        var baseUrl = _configuration["Oluso:IssuerUri"]
            ?? _configuration["IdentityServer:IssuerUri"]
            ?? throw new InvalidOperationException("IssuerUri not configured");

        if (_tenantContext.HasTenant)
        {
            return $"{baseUrl.TrimEnd('/')}/{_tenantContext.Tenant!.Identifier}";
        }

        return baseUrl;
    }

    private class TokenValidationResult
    {
        public bool IsValid { get; private set; }
        public string? ErrorMessage { get; private set; }
        public string? SubjectId { get; private set; }
        public string? SessionId { get; private set; }
        public IReadOnlyList<string> Scopes { get; private set; } = Array.Empty<string>();
        public IReadOnlyDictionary<string, object> Claims { get; private set; } = new Dictionary<string, object>();

        public static TokenValidationResult Success(
            string? subjectId,
            string? sessionId,
            List<string> scopes,
            Dictionary<string, object> claims) => new()
        {
            IsValid = true,
            SubjectId = subjectId,
            SessionId = sessionId,
            Scopes = scopes,
            Claims = claims
        };

        public static TokenValidationResult Failed(string error) => new()
        {
            IsValid = false,
            ErrorMessage = error
        };
    }
}
