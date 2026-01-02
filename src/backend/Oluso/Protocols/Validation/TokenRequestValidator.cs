using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Logging;
using Oluso.Core.Common;
using Oluso.Core.Protocols.DPoP;
using Oluso.Core.Protocols.Models;
using Oluso.Core.Protocols.Validation;

namespace Oluso.Protocols.Validation;

/// <summary>
/// Default implementation of token request validator.
///
/// This validator handles request parsing and protocol-level validation:
/// - Client authentication
/// - Grant type authorization
/// - DPoP proof validation
/// - Scope validation
/// - Parameter parsing for each grant type
///
/// Grant-specific business logic (credential validation, code lookup, etc.)
/// is delegated to IGrantHandler implementations.
/// </summary>
public class TokenRequestValidator : ITokenRequestValidator
{
    private readonly IClientAuthenticator _clientAuthenticator;
    private readonly IScopeValidator _scopeValidator;
    private readonly IDPoPProofValidator _dpopValidator;
    private readonly ILogger<TokenRequestValidator> _logger;

    public TokenRequestValidator(
        IClientAuthenticator clientAuthenticator,
        IScopeValidator scopeValidator,
        IDPoPProofValidator dpopValidator,
        ILogger<TokenRequestValidator> logger)
    {
        _clientAuthenticator = clientAuthenticator;
        _scopeValidator = scopeValidator;
        _dpopValidator = dpopValidator;
        _logger = logger;
    }

    public async Task<ValidationResult<TokenRequest>> ValidateAsync(
        HttpRequest request,
        CancellationToken cancellationToken = default)
    {
        if (!request.HasFormContentType)
        {
            return ValidationResult<TokenRequest>.Failure(
                OidcConstants.Errors.InvalidRequest,
                "Content-Type must be application/x-www-form-urlencoded");
        }

        var form = await request.ReadFormAsync(cancellationToken);
        var tokenRequest = new TokenRequest
        {
            Raw = form.ToDictionary(x => x.Key, x => x.Value.ToString())
        };

        // 1. Authenticate client
        var clientAuth = await _clientAuthenticator.AuthenticateAsync(request, cancellationToken);
        if (!clientAuth.IsValid)
        {
            return ValidationResult<TokenRequest>.Failure(
                clientAuth.Error!,
                clientAuth.ErrorDescription);
        }

        var client = clientAuth.Client!;

        tokenRequest.Client = new ValidatedClient
        {
            // Basic info
            ClientId = client.ClientId,
            ClientName = client.ClientName,
            Description = client.Description,
            ClientUri = client.ClientUri,
            LogoUri = client.LogoUri,
            AuthenticationMethod = clientAuth.Method,

            // Allowed grants and scopes
            AllowedGrantTypes = client.AllowedGrantTypes.Select(g => g.GrantType).ToList(),
            AllowedScopes = client.AllowedScopes.Select(s => s.Scope).ToList(),

            // PKCE settings
            RequirePkce = client.RequirePkce,
            AllowPlainTextPkce = client.AllowPlainTextPkce,
            RequireRequestObject = client.RequireRequestObject,
            AllowAccessTokensViaBrowser = client.AllowAccessTokensViaBrowser,
            AllowOfflineAccess = client.AllowOfflineAccess,

            // Token lifetime settings
            IdentityTokenLifetime = client.IdentityTokenLifetime,
            AccessTokenLifetime = client.AccessTokenLifetime,
            AuthorizationCodeLifetime = client.AuthorizationCodeLifetime,
            DeviceCodeLifetime = client.DeviceCodeLifetime,

            // Token settings
            AccessTokenType = client.AccessTokenType,
            AllowedIdentityTokenSigningAlgorithms = client.AllowedIdentityTokenSigningAlgorithms,
            AlwaysIncludeUserClaimsInIdToken = client.AlwaysIncludeUserClaimsInIdToken,
            UpdateAccessTokenClaimsOnRefresh = client.UpdateAccessTokenClaimsOnRefresh,
            IncludeJwtId = client.IncludeJwtId,

            // Refresh token settings
            AbsoluteRefreshTokenLifetime = client.AbsoluteRefreshTokenLifetime,
            SlidingRefreshTokenLifetime = client.SlidingRefreshTokenLifetime,
            RefreshTokenExpiration = client.RefreshTokenExpiration,
            RefreshTokenUsage = client.RefreshTokenUsage,

            // DPoP
            RequireDPoP = client.RequireDPoP,

            // Subject identifier type (pairwise or public)
            PairWiseSubjectSalt = client.PairWiseSubjectSalt,

            // PAR settings
            RequirePushedAuthorization = client.RequirePushedAuthorization,
            PushedAuthorizationLifetime = client.PushedAuthorizationLifetime,

            // Consent settings
            RequireConsent = client.RequireConsent,
            AllowRememberConsent = client.AllowRememberConsent,
            ConsentLifetime = client.ConsentLifetime,

            // Login settings
            EnableLocalLogin = client.EnableLocalLogin,
            UserSsoLifetime = client.UserSsoLifetime,

            // Client claims
            AlwaysSendClientClaims = client.AlwaysSendClientClaims,
            ClientClaimsPrefix = client.ClientClaimsPrefix,
            Claims = client.Claims.Select(c => new ValidatedClientClaim { Type = c.Type, Value = c.Value }).ToList(),

            // IdP restrictions
            IdentityProviderRestrictions = client.IdentityProviderRestrictions.Select(r => r.Provider).ToList(),

            // Access restrictions
            AllowedRoles = client.AllowedRoles.Select(r => r.Role).ToList(),
            AllowedUsers = client.AllowedUsers.Select(u => u.SubjectId).ToList(),

            // Custom properties (includes domain_hint and other settings)
            Properties = client.Properties.ToDictionary(p => p.Key, p => p.Value),

            // Logout settings
            FrontChannelLogoutUri = client.FrontChannelLogoutUri,
            FrontChannelLogoutSessionRequired = client.FrontChannelLogoutSessionRequired,
            BackChannelLogoutUri = client.BackChannelLogoutUri,
            BackChannelLogoutSessionRequired = client.BackChannelLogoutSessionRequired,

            // CIBA settings
            CibaEnabled = client.CibaEnabled,
            CibaTokenDeliveryMode = client.CibaTokenDeliveryMode,
            CibaClientNotificationEndpoint = client.CibaClientNotificationEndpoint,
            CibaRequestLifetime = client.CibaRequestLifetime,
            CibaPollingInterval = client.CibaPollingInterval,
            CibaRequireUserCode = client.CibaRequireUserCode,
        };

        // 2. Validate grant_type
        var grantType = form["grant_type"].FirstOrDefault();
        if (string.IsNullOrEmpty(grantType))
        {
            return ValidationResult<TokenRequest>.Failure(
                OidcConstants.Errors.InvalidRequest,
                "grant_type is required");
        }
        tokenRequest.GrantType = grantType;

        // Check if grant type is allowed for client
        if (!tokenRequest.Client.AllowedGrantTypes.Contains(grantType))
        {
            return ValidationResult<TokenRequest>.Failure(
                OidcConstants.Errors.UnauthorizedClient,
                $"Client is not authorized for grant_type '{grantType}'");
        }

        // 3. Parse grant-type specific parameters
        // Note: Business logic validation (code lookup, credential validation, etc.)
        // is delegated to grant handlers. This only parses and validates required parameters.
        var grantValidation = grantType switch
        {
            OidcConstants.GrantTypes.AuthorizationCode => ParseAuthorizationCodeGrant(form, tokenRequest),
            OidcConstants.GrantTypes.RefreshToken => ParseRefreshTokenGrant(form, tokenRequest),
            OidcConstants.GrantTypes.ClientCredentials => ValidationResult.Success(), // No additional params required
            OidcConstants.GrantTypes.Password => ParsePasswordGrant(form, tokenRequest),
            OidcConstants.GrantTypes.DeviceCode => ParseDeviceCodeGrant(form, tokenRequest),
            OidcConstants.GrantTypes.TokenExchange => ParseTokenExchangeGrant(form, tokenRequest),
            OidcConstants.GrantTypes.JwtBearer => ParseJwtBearerGrant(form, tokenRequest),
            OidcConstants.GrantTypes.Ciba => ParseCibaGrant(form, tokenRequest),
            _ => ValidationResult.Success() // Unknown grant types are handled by grant handlers
        };

        if (!grantValidation.IsValid)
        {
            return ValidationResult<TokenRequest>.Failure(
                grantValidation.Error!,
                grantValidation.ErrorDescription);
        }

        // 4. Validate scope (if provided)
        var scope = form["scope"].FirstOrDefault();
        if (!string.IsNullOrEmpty(scope))
        {
            tokenRequest.Scope = scope;
            tokenRequest.RequestedScopes = _scopeValidator.ParseScopes(scope).ToList();

            var scopeValidation = await _scopeValidator.ValidateAsync(
                tokenRequest.RequestedScopes,
                tokenRequest.Client.AllowedScopes,
                cancellationToken);

            if (!scopeValidation.IsValid)
            {
                return ValidationResult<TokenRequest>.Failure(
                    scopeValidation.Error!,
                    scopeValidation.ErrorDescription);
            }
        }

        // 5. Validate DPoP proof
        // Per RFC 9449 Section 4.2: There MUST be exactly one DPoP header field
        var dpopHeaders = request.Headers["DPoP"];
        if (dpopHeaders.Count > 1)
        {
            return ValidationResult<TokenRequest>.Failure(
                OidcConstants.Errors.InvalidRequest,
                "Multiple DPoP headers are not allowed");
        }
        var dpopHeader = dpopHeaders.FirstOrDefault();

        if (client.RequireDPoP && string.IsNullOrEmpty(dpopHeader))
        {
            return ValidationResult<TokenRequest>.Failure(
                OidcConstants.Errors.InvalidRequest,
                "DPoP proof is required for this client");
        }

        if (!string.IsNullOrEmpty(dpopHeader))
        {
            // Build the token endpoint URL
            var tokenEndpointUrl = $"{request.Scheme}://{request.Host}{request.Path}";

            var dpopValidation = await _dpopValidator.ValidateAsync(new DPoPValidationContext
            {
                Proof = dpopHeader,
                HttpMethod = request.Method,
                HttpUri = tokenEndpointUrl,
                RequireNonce = client.RequireDPoP,
                ClientId = client.ClientId,
                // For refresh token grant, validate key matches original binding
                ExpectedJwkThumbprint = tokenRequest.BoundDPoPJkt
            }, cancellationToken);

            if (!dpopValidation.IsValid)
            {
                // Handle nonce requirement specially
                if (dpopValidation.RequiresNonce)
                {
                    return ValidationResult<TokenRequest>.Failure(
                        dpopValidation.Error!,
                        dpopValidation.ErrorDescription,
                        new Dictionary<string, string>
                        {
                            ["DPoP-Nonce"] = dpopValidation.ServerNonce!
                        });
                }

                return ValidationResult<TokenRequest>.Failure(
                    dpopValidation.Error!,
                    dpopValidation.ErrorDescription);
            }

            tokenRequest.DPoP = dpopHeader;
            tokenRequest.DPoPKeyThumbprint = dpopValidation.JwkThumbprint;
        }

        return ValidationResult<TokenRequest>.Success(tokenRequest);
    }

    #region Parameter Parsing Methods
    // These methods only parse and validate required parameters.
    // Business logic (code lookup, credential validation, etc.) is in grant handlers.

    private static ValidationResult ParseAuthorizationCodeGrant(IFormCollection form, TokenRequest request)
    {
        var code = form["code"].FirstOrDefault();
        if (string.IsNullOrEmpty(code))
        {
            return ValidationResult.Failure(
                OidcConstants.Errors.InvalidRequest,
                "code is required");
        }

        request.Code = code;
        request.RedirectUri = form["redirect_uri"].FirstOrDefault();
        request.CodeVerifier = form["code_verifier"].FirstOrDefault();

        return ValidationResult.Success();
    }

    private static ValidationResult ParseRefreshTokenGrant(IFormCollection form, TokenRequest request)
    {
        var refreshToken = form["refresh_token"].FirstOrDefault();
        if (string.IsNullOrEmpty(refreshToken))
        {
            return ValidationResult.Failure(
                OidcConstants.Errors.InvalidRequest,
                "refresh_token is required");
        }

        request.RefreshToken = refreshToken;
        return ValidationResult.Success();
    }

    private static ValidationResult ParsePasswordGrant(IFormCollection form, TokenRequest request)
    {
        var username = form["username"].FirstOrDefault();
        var password = form["password"].FirstOrDefault();

        if (string.IsNullOrEmpty(username) || string.IsNullOrEmpty(password))
        {
            return ValidationResult.Failure(
                OidcConstants.Errors.InvalidRequest,
                "username and password are required");
        }

        request.UserName = username;
        request.Password = password;

        return ValidationResult.Success();
    }

    private static ValidationResult ParseDeviceCodeGrant(IFormCollection form, TokenRequest request)
    {
        var deviceCode = form["device_code"].FirstOrDefault();
        if (string.IsNullOrEmpty(deviceCode))
        {
            return ValidationResult.Failure(
                OidcConstants.Errors.InvalidRequest,
                "device_code is required");
        }

        request.DeviceCode = deviceCode;
        return ValidationResult.Success();
    }

    private static ValidationResult ParseTokenExchangeGrant(IFormCollection form, TokenRequest request)
    {
        var subjectToken = form["subject_token"].FirstOrDefault();
        var subjectTokenType = form["subject_token_type"].FirstOrDefault();

        if (string.IsNullOrEmpty(subjectToken) || string.IsNullOrEmpty(subjectTokenType))
        {
            return ValidationResult.Failure(
                OidcConstants.Errors.InvalidRequest,
                "subject_token and subject_token_type are required");
        }

        request.SubjectToken = subjectToken;
        request.SubjectTokenType = subjectTokenType;
        request.ActorToken = form["actor_token"].FirstOrDefault();
        request.ActorTokenType = form["actor_token_type"].FirstOrDefault();
        request.RequestedTokenType = form["requested_token_type"].FirstOrDefault();

        return ValidationResult.Success();
    }

    private static ValidationResult ParseJwtBearerGrant(IFormCollection form, TokenRequest request)
    {
        var assertion = form["assertion"].FirstOrDefault();
        if (string.IsNullOrEmpty(assertion))
        {
            return ValidationResult.Failure(
                OidcConstants.Errors.InvalidRequest,
                "assertion is required");
        }

        request.Assertion = assertion;
        return ValidationResult.Success();
    }

    private static ValidationResult ParseCibaGrant(IFormCollection form, TokenRequest request)
    {
        var authReqId = form["auth_req_id"].FirstOrDefault();
        if (string.IsNullOrEmpty(authReqId))
        {
            return ValidationResult.Failure(
                OidcConstants.Errors.InvalidRequest,
                "auth_req_id is required");
        }

        request.AuthReqId = authReqId;
        return ValidationResult.Success();
    }

    #endregion
}
