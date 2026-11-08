using Microsoft.Extensions.Logging;
using Oluso.Core.Domain.Interfaces;
using Oluso.Core.Protocols;
using Oluso.Core.Protocols.Grants;
using Oluso.Core.Protocols.Models;
using Oluso.Core.Protocols.Validation;
using Oluso.Core.Services;

namespace Oluso.Protocols.Grants;

/// <summary>
/// Handles authorization_code grant type
/// </summary>
public class AuthorizationCodeGrantHandler : IGrantHandler
{
    private readonly IAuthorizationCodeStore _codeStore;
    private readonly IPkceValidator _pkceValidator;
    private readonly IProfileService _profileService;
    private readonly IPersistedGrantStore _grantStore;
    private readonly IClientStore _clientStore;
    private readonly IClientUserAccess _clientUserAccess;
    private readonly ILogger<AuthorizationCodeGrantHandler> _logger;

    public string GrantType => OidcConstants.GrantTypes.AuthorizationCode;

    public AuthorizationCodeGrantHandler(
        IAuthorizationCodeStore codeStore,
        IPkceValidator pkceValidator,
        IProfileService profileService,
        IPersistedGrantStore grantStore,
        IClientStore clientStore,
        IClientUserAccess clientUserAccess,
        ILogger<AuthorizationCodeGrantHandler> logger)
    {
        _clientUserAccess = clientUserAccess;
        _codeStore = codeStore;
        _pkceValidator = pkceValidator;
        _profileService = profileService;
        _grantStore = grantStore;
        _clientStore = clientStore;
        _logger = logger;
    }

    public async Task<GrantResult> HandleAsync(TokenRequest request, CancellationToken cancellationToken = default)
    {
        // Validate code parameter is present (RFC 6749 Section 4.1.3)
        if (string.IsNullOrEmpty(request.Code))
        {
            _logger.LogWarning("Authorization code is missing from token request");
            return GrantResult.Failure(OidcConstants.Errors.InvalidGrant, "Authorization code is missing");
        }

        // Look up authorization code
        var authCode = await _codeStore.GetAsync(request.Code, cancellationToken);
        if (authCode == null)
        {
            _logger.LogWarning("Authorization code not found in store");
            return GrantResult.Failure(OidcConstants.Errors.InvalidGrant, "Invalid authorization code");
        }

        _logger.LogDebug("Found authorization code for client {ClientId}, subject {SubjectId}, scopes {Scopes}",
            authCode.ClientId, authCode.SubjectId, string.Join(" ", authCode.Scopes));

        // Check if already consumed
        if (authCode.IsConsumed)
        {
            // RFC 6749 Section 4.1.2: Code reuse attempt detected
            // "If an authorization code is used more than once, the authorization server MUST deny the request
            // and SHOULD revoke (when possible) all tokens previously issued based on that authorization code."
            _logger.LogWarning(
                "Authorization code replay attack detected for client {ClientId}, subject {SubjectId}, session {SessionId}. Revoking all session tokens.",
                authCode.ClientId, authCode.SubjectId, authCode.SessionId);

            // Revoke all tokens issued to this session (access tokens, refresh tokens, etc.)
            await _grantStore.RemoveAllAsync(new PersistedGrantFilter
            {
                SubjectId = authCode.SubjectId,
                ClientId = authCode.ClientId,
                SessionId = authCode.SessionId
            }, cancellationToken);

            // Remove the compromised authorization code
            await _codeStore.RemoveAsync(request.Code, cancellationToken);

            return GrantResult.Failure(OidcConstants.Errors.InvalidGrant, "Authorization code has already been used");
        }

        // Check expiration
        if (authCode.Expiration < DateTime.UtcNow)
        {
            await _codeStore.RemoveAsync(request.Code, cancellationToken);
            return GrantResult.Failure(OidcConstants.Errors.InvalidGrant, "Authorization code has expired");
        }

        // Validate client matches
        if (authCode.ClientId != request.Client?.ClientId)
        {
            return GrantResult.Failure(OidcConstants.Errors.InvalidGrant, "Authorization code was issued to different client");
        }

        // RFC 9449 §10: a code bound to a DPoP key (dpop_jkt at /authorize) is redeemed only with a
        // proof signed by that key.
        if (authCode.Properties != null
            && authCode.Properties.TryGetValue(Oidc.OidcProtocolService.AuthorizationCodeDPoPJktProperty, out var codeJkt)
            && !string.IsNullOrEmpty(codeJkt)
            && !string.Equals(codeJkt, request.DPoPKeyThumbprint, StringComparison.Ordinal))
        {
            return GrantResult.Failure(OidcConstants.Errors.InvalidDPoPProof,
                "This authorization code is bound to a DPoP key; present a DPoP proof signed by that key.");
        }

        // Validate redirect_uri matches exactly (RFC 6749 Section 4.1.3 — "identical" comparison)
        if (!string.IsNullOrEmpty(authCode.RedirectUri) &&
            !string.Equals(authCode.RedirectUri, request.RedirectUri, StringComparison.Ordinal))
        {
            return GrantResult.Failure(OidcConstants.Errors.InvalidGrant, "redirect_uri does not match");
        }

        // RFC 8707: resources named at sign-in apply to the whole grant; a token request may name
        // some of them, but not others.
        var resources = ResourceIndicators.Narrow(authCode.Resources, request.Resource.ToList());
        if (resources == null)
        {
            return GrantResult.Failure(OidcConstants.Errors.InvalidTarget,
                "The requested resource was not part of the authorization request");
        }

        // Validate PKCE if code_challenge was used
        if (!string.IsNullOrEmpty(authCode.CodeChallenge))
        {
            var pkceResult = _pkceValidator.ValidateCodeVerifier(
                request.CodeVerifier,
                authCode.CodeChallenge,
                authCode.CodeChallengeMethod ?? CodeChallengeMethods.Plain);

            if (!pkceResult.IsValid)
            {
                return GrantResult.Failure(pkceResult.Error!, pkceResult.ErrorDescription);
            }
        }
        else if (request.Client?.RequirePkce == true)
        {
            return GrantResult.Failure(OidcConstants.Errors.InvalidGrant, "PKCE is required but code_challenge was not used");
        }

        // Check if user is still active
        if (!string.IsNullOrEmpty(authCode.SubjectId))
        {
            var isActive = await _profileService.IsActiveAsync(authCode.SubjectId, cancellationToken);
            if (!isActive)
            {
                return GrantResult.Failure(OidcConstants.Errors.InvalidGrant, "User is not active");
            }

            // The client's user restrictions, against the tenant this code was issued for.
            if (request.Client != null)
            {
                var denial = await _clientUserAccess.FindDenialAsync(
                    request.Client.ClientId, ClientUserRestrictions.From(request.Client),
                    authCode.SubjectId, authCode.TenantId, cancellationToken);
                if (denial != null)
                {
                    return GrantResult.Failure(OidcConstants.Errors.AccessDenied, denial);
                }
            }
        }

        // Mark code as consumed by removing it (one-time use)
        await _codeStore.RemoveAsync(request.Code, cancellationToken);

        // Build claims from user profile based on requested scopes
        var claims = new Dictionary<string, object>();

        // Add nonce from authorization request
        if (!string.IsNullOrEmpty(authCode.Nonce))
        {
            claims[OidcConstants.StandardClaims.Nonce] = authCode.Nonce;
        }

        // Surface allowlisted custom authorize parameters captured at /authorize
        // (transported under a prefix in the code's claim bag) as AdditionalData,
        // keyed by parameter name, so the AuthorizeParameterClaimsProvider can map
        // and validate them into real claims at issuance.
        Dictionary<string, object>? authorizeParameters = null;
        if (authCode.Claims != null)
        {
            foreach (var claim in authCode.Claims)
            {
                if (claim.Key.StartsWith(Core.Domain.Entities.ClientAuthorizeParameter.TransportClaimPrefix, StringComparison.Ordinal))
                {
                    authorizeParameters ??= new Dictionary<string, object>(StringComparer.Ordinal);
                    var paramName = claim.Key[Core.Domain.Entities.ClientAuthorizeParameter.TransportClaimPrefix.Length..];
                    authorizeParameters[paramName] = claim.Value;
                }
            }
        }

        // Load profile claims based on scopes
        if (!string.IsNullOrEmpty(authCode.SubjectId) && request.Client != null)
        {
            // Fetch the full client entity for profile service
            var fullClient = await _clientStore.FindClientByIdAsync(request.Client.ClientId, cancellationToken);

            var profileClaims = await _profileService.GetProfileClaimsAsync(
                authCode.SubjectId,
                fullClient ?? new Core.Domain.Entities.Client { ClientId = request.Client.ClientId },
                authCode.Scopes,
                "TokenEndpoint",
                "oidc",
                request.Resource,
                authorizeParameters,
                cancellationToken);

            foreach (var claim in profileClaims)
            {
                claims[claim.Key] = claim.Value;
            }
        }

        // Merge with any claims stored in the authorization code, skipping the
        // authorize-parameter transport keys — those are inputs to the claims
        // provider above, not claims, and must never leak into the token.
        if (authCode.Claims != null)
        {
            foreach (var claim in authCode.Claims)
            {
                if (claim.Key.StartsWith(Core.Domain.Entities.ClientAuthorizeParameter.TransportClaimPrefix, StringComparison.Ordinal))
                {
                    continue;
                }

                if (!claims.ContainsKey(claim.Key))
                {
                    claims[claim.Key] = claim.Value;
                }
            }
        }

        // Collect permissions from user's roles only when the "permissions" scope is requested
        var permissions = new List<string>();
        if (!string.IsNullOrEmpty(authCode.SubjectId) && authCode.Scopes.Contains(OidcConstants.Scopes.Permissions))
        {
            var tenantId = claims.TryGetValue("tenant_id", out var tid) ? tid?.ToString() : null;
            permissions = (await _profileService.GetUserPermissionsAsync(authCode.SubjectId, tenantId, cancellationToken))?.ToList();
        }

        return new GrantResult
        {
            SubjectId = authCode.SubjectId,
            SessionId = authCode.SessionId,
            Amr = AuthorizationCodes.ReadAmr(authCode).ToList(),
            Acr = AuthorizationCodes.ReadAcr(authCode),
            Scopes = authCode.Scopes,
            Claims = claims,
            Permissions = permissions ?? new List<string>(),
            Resources = resources.ToList(),
            GrantedResources = authCode.Resources.Count > 0 ? authCode.Resources.ToList() : resources.ToList()
        };
    }
}
