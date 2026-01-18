using System.IdentityModel.Tokens.Jwt;
using System.Text.Json;
using Microsoft.Extensions.Logging;
using Microsoft.IdentityModel.Tokens;
using Oluso.Core.Common;
using Oluso.Core.Domain.Entities;
using Oluso.Core.Domain.Interfaces;
using Oluso.Core.Protocols;
using Oluso.Core.Protocols.DPoP;
using Oluso.Core.Protocols.Models;
using Oluso.Core.Protocols.Validation;

namespace Oluso.Protocols.Validation;

/// <summary>
/// Default implementation of bearer token validator.
/// Supports both JWT and reference tokens, with optional DPoP validation.
/// </summary>
public class BearerTokenValidator : IBearerTokenValidator
{
    private readonly ISigningCredentialStore _signingCredentialStore;
    private readonly IPersistedGrantStore _grantStore;
    private readonly IIssuerResolver _issuerResolver;
    private readonly IDPoPProofValidator _dpopValidator;
    private readonly ILogger<BearerTokenValidator> _logger;

    public BearerTokenValidator(
        ISigningCredentialStore signingCredentialStore,
        IPersistedGrantStore grantStore,
        IIssuerResolver issuerResolver,
        IDPoPProofValidator dpopValidator,
        ILogger<BearerTokenValidator> logger)
    {
        _signingCredentialStore = signingCredentialStore;
        _grantStore = grantStore;
        _issuerResolver = issuerResolver;
        _dpopValidator = dpopValidator;
        _logger = logger;
    }

    public Task<BearerTokenValidationResult> ValidateAsync(
        string token,
        CancellationToken cancellationToken = default)
    {
        return ValidateAsync(new BearerTokenValidationContext { Token = token }, cancellationToken);
    }

    public async Task<BearerTokenValidationResult> ValidateAsync(
        BearerTokenValidationContext context,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrEmpty(context.Token))
        {
            return BearerTokenValidationResult.Failure(
                OidcConstants.Errors.InvalidToken,
                "Token is required");
        }

        // Try reference token first (stored in grant store)
        var referenceResult = await ValidateReferenceTokenAsync(context, cancellationToken);
        if (referenceResult != null)
        {
            return referenceResult;
        }

        // Try JWT token
        return await ValidateJwtTokenAsync(context, cancellationToken);
    }

    private async Task<BearerTokenValidationResult?> ValidateReferenceTokenAsync(
        BearerTokenValidationContext context,
        CancellationToken cancellationToken)
    {
        // Reference tokens are stored in the grant store
        var grant = await _grantStore.GetAsync(context.Token, cancellationToken);
        if (grant == null)
        {
            return null; // Not a reference token, try JWT
        }

        // Check if it's an access token type
        if (grant.Type != "reference_token" && grant.Type != "access_token")
        {
            return null; // Not an access token
        }

        // Check if consumed
        if (grant.ConsumedTime.HasValue)
        {
            _logger.LogDebug("Reference token has been consumed");
            return BearerTokenValidationResult.Failure(
                OidcConstants.Errors.InvalidToken,
                "Token has been revoked");
        }

        // Check expiration
        if (grant.Expiration.HasValue && grant.Expiration.Value < DateTime.UtcNow)
        {
            _logger.LogDebug("Reference token has expired");
            return BearerTokenValidationResult.Failure(
                OidcConstants.Errors.InvalidToken,
                "Token has expired");
        }

        // Parse grant data
        try
        {
            var data = JsonSerializer.Deserialize<ReferenceTokenData>(grant.Data);
            if (data == null)
            {
                return BearerTokenValidationResult.Failure(
                    OidcConstants.Errors.InvalidToken,
                    "Invalid token data");
            }

            var scopes = data.Scopes?.ToList() ?? new List<string>();

            // Validate required scopes
            var scopeValidation = ValidateScopes(scopes, context.RequiredScopes, context.RequireAllScopes);
            if (!scopeValidation.IsValid)
            {
                return BearerTokenValidationResult.Failure(
                    OidcConstants.Errors.InsufficientScope,
                    scopeValidation.ErrorDescription);
            }

            // Validate DPoP if token is bound
            if (!string.IsNullOrEmpty(data.DPoPKeyThumbprint))
            {
                var dpopResult = await ValidateDPoPBindingAsync(context, data.DPoPKeyThumbprint, cancellationToken);
                if (!dpopResult.IsValid)
                {
                    return BearerTokenValidationResult.Failure(dpopResult.Error!, dpopResult.ErrorDescription);
                }
            }

            var result = BearerTokenValidationResult.Success(
                data.SubjectId,
                data.ClientId ?? grant.ClientId,
                scopes,
                data.Claims?.ToDictionary(c => c.Key, c => (object)c.Value));

            result.TokenType = "reference";
            result.Expiration = grant.Expiration;
            result.SessionId = data.SessionId ?? grant.SessionId;
            result.DPoPKeyThumbprint = data.DPoPKeyThumbprint;

            return result;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to parse reference token data");
            return BearerTokenValidationResult.Failure(
                OidcConstants.Errors.InvalidToken,
                "Invalid token format");
        }
    }

    private async Task<BearerTokenValidationResult> ValidateJwtTokenAsync(
        BearerTokenValidationContext context,
        CancellationToken cancellationToken)
    {
        var handler = new JwtSecurityTokenHandler();

        if (!handler.CanReadToken(context.Token))
        {
            return BearerTokenValidationResult.Failure(
                OidcConstants.Errors.InvalidToken,
                "Invalid token format");
        }

        // Get validation keys
        var validationKeys = await _signingCredentialStore.GetValidationKeysAsync(cancellationToken);
        if (validationKeys == null || !validationKeys.Any())
        {
            _logger.LogError("No validation keys available for token validation");
            return BearerTokenValidationResult.Failure(
                OidcConstants.Errors.InvalidToken,
                "Token validation unavailable");
        }

        var issuer = await _issuerResolver.GetIssuerAsync(cancellationToken);

        var validationParameters = new TokenValidationParameters
        {
            ValidateIssuerSigningKey = true,
            IssuerSigningKeys = validationKeys.Select(k => k.Key),
            ValidateIssuer = true,
            ValidIssuer = issuer,
            ValidateAudience = !string.IsNullOrEmpty(context.ExpectedAudience),
            ValidAudience = context.ExpectedAudience,
            ValidateLifetime = true,
            ClockSkew = TimeSpan.FromMinutes(1)
        };

        try
        {
            var principal = handler.ValidateToken(context.Token, validationParameters, out var validatedToken);
            var jwt = validatedToken as JwtSecurityToken;

            if (jwt == null)
            {
                return BearerTokenValidationResult.Failure(
                    OidcConstants.Errors.InvalidToken,
                    "Token validation failed");
            }

            // Extract claims
            var subjectId = jwt.Subject;
            var clientId = jwt.Claims.FirstOrDefault(c => c.Type == "client_id")?.Value;
            var scopes = jwt.Claims
                .Where(c => c.Type == "scope")
                .SelectMany(c => c.Value.Split(' ', StringSplitOptions.RemoveEmptyEntries))
                .ToList();
            var sessionId = jwt.Claims.FirstOrDefault(c => c.Type == "sid")?.Value;

            // Validate required scopes
            var scopeValidation = ValidateScopes(scopes, context.RequiredScopes, context.RequireAllScopes);
            if (!scopeValidation.IsValid)
            {
                return BearerTokenValidationResult.Failure(
                    OidcConstants.Errors.InsufficientScope,
                    scopeValidation.ErrorDescription);
            }

            // Check for DPoP binding (cnf claim with jkt)
            string? dpopJkt = null;
            var cnfClaim = jwt.Claims.FirstOrDefault(c => c.Type == "cnf");
            if (cnfClaim != null)
            {
                try
                {
                    var cnf = JsonSerializer.Deserialize<Dictionary<string, string>>(cnfClaim.Value);
                    cnf?.TryGetValue("jkt", out dpopJkt);
                }
                catch
                {
                    // Ignore JSON parse errors for cnf
                }
            }

            // Validate DPoP if token is bound
            if (!string.IsNullOrEmpty(dpopJkt))
            {
                var dpopResult = await ValidateDPoPBindingAsync(context, dpopJkt, cancellationToken);
                if (!dpopResult.IsValid)
                {
                    return BearerTokenValidationResult.Failure(dpopResult.Error!, dpopResult.ErrorDescription);
                }
            }

            // Build claims dictionary
            var claims = jwt.Claims
                .GroupBy(c => c.Type)
                .ToDictionary(
                    g => g.Key,
                    g => g.Count() == 1 ? (object)g.First().Value : g.Select(c => c.Value).ToArray());

            var result = BearerTokenValidationResult.Success(subjectId, clientId, scopes, claims);
            result.TokenType = "jwt";
            result.Expiration = jwt.ValidTo;
            result.SessionId = sessionId;
            result.DPoPKeyThumbprint = dpopJkt;

            return result;
        }
        catch (SecurityTokenExpiredException)
        {
            return BearerTokenValidationResult.Failure(
                OidcConstants.Errors.InvalidToken,
                "Token has expired");
        }
        catch (SecurityTokenInvalidSignatureException)
        {
            return BearerTokenValidationResult.Failure(
                OidcConstants.Errors.InvalidToken,
                "Token signature validation failed");
        }
        catch (SecurityTokenException ex)
        {
            _logger.LogWarning(ex, "Token validation failed");
            return BearerTokenValidationResult.Failure(
                OidcConstants.Errors.InvalidToken,
                "Token validation failed");
        }
    }

    private async Task<BearerTokenValidationResult> ValidateDPoPBindingAsync(
        BearerTokenValidationContext context,
        string expectedJkt,
        CancellationToken cancellationToken)
    {
        if (string.IsNullOrEmpty(context.DPoPProof))
        {
            return BearerTokenValidationResult.Failure(
                OidcConstants.Errors.InvalidToken,
                "DPoP proof is required for this token");
        }

        if (string.IsNullOrEmpty(context.HttpMethod) || string.IsNullOrEmpty(context.HttpUri))
        {
            return BearerTokenValidationResult.Failure(
                OidcConstants.Errors.InvalidRequest,
                "HTTP method and URI are required for DPoP validation");
        }

        var dpopContext = new DPoPValidationContext
        {
            Proof = context.DPoPProof,
            HttpMethod = context.HttpMethod,
            HttpUri = context.HttpUri,
            ExpectedJwkThumbprint = expectedJkt,
            ExpectedAccessTokenHash = _dpopValidator.ComputeAccessTokenHash(context.Token)
        };

        var dpopResult = await _dpopValidator.ValidateAsync(dpopContext, cancellationToken);
        if (!dpopResult.IsValid)
        {
            return BearerTokenValidationResult.Failure(
                dpopResult.Error ?? OidcConstants.Errors.InvalidDPoPProof,
                dpopResult.ErrorDescription);
        }

        return new BearerTokenValidationResult(); // Success (IsValid = true by default via base)
    }

    private static ValidationResult ValidateScopes(
        ICollection<string> tokenScopes,
        ICollection<string>? requiredScopes,
        bool requireAll)
    {
        if (requiredScopes == null || requiredScopes.Count == 0)
        {
            return new ValidationResult(); // No scope requirements
        }

        if (requireAll)
        {
            var missing = requiredScopes.Except(tokenScopes).ToList();
            if (missing.Any())
            {
                return new ValidationResult
                {
                    Error = OidcConstants.Errors.InsufficientScope,
                    ErrorDescription = $"Missing required scopes: {string.Join(", ", missing)}"
                };
            }
        }
        else
        {
            // At least one required scope must be present
            if (!requiredScopes.Any(s => tokenScopes.Contains(s)))
            {
                return new ValidationResult
                {
                    Error = OidcConstants.Errors.InsufficientScope,
                    ErrorDescription = $"Token must have at least one of: {string.Join(", ", requiredScopes)}"
                };
            }
        }

        return new ValidationResult();
    }

    /// <summary>
    /// Data structure for reference tokens
    /// </summary>
    private class ReferenceTokenData
    {
        public string? SubjectId { get; set; }
        public string? ClientId { get; set; }
        public ICollection<string>? Scopes { get; set; }
        public Dictionary<string, string>? Claims { get; set; }
        public string? SessionId { get; set; }
        public string? DPoPKeyThumbprint { get; set; }
        public DateTime CreatedAt { get; set; }
    }
}
