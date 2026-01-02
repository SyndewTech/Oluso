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
    private readonly ILogger<AuthorizationCodeGrantHandler> _logger;

    public string GrantType => OidcConstants.GrantTypes.AuthorizationCode;

    public AuthorizationCodeGrantHandler(
        IAuthorizationCodeStore codeStore,
        IPkceValidator pkceValidator,
        IProfileService profileService,
        IPersistedGrantStore grantStore,
        IClientStore clientStore,
        ILogger<AuthorizationCodeGrantHandler> logger)
    {
        _codeStore = codeStore;
        _pkceValidator = pkceValidator;
        _profileService = profileService;
        _grantStore = grantStore;
        _clientStore = clientStore;
        _logger = logger;
    }

    public async Task<GrantResult> HandleAsync(TokenRequest request, CancellationToken cancellationToken = default)
    {
        // Code presence is validated by TokenRequestValidator
        _logger.LogDebug("Processing authorization_code grant for code: {Code}", request.Code![..Math.Min(10, request.Code.Length)]);

        // Look up authorization code
        var authCode = await _codeStore.GetAsync(request.Code, cancellationToken);
        if (authCode == null)
        {
            _logger.LogWarning("Authorization code not found in store: {Code}", request.Code[..Math.Min(10, request.Code.Length)]);
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

        // Validate redirect_uri matches
        if (!string.IsNullOrEmpty(authCode.RedirectUri) &&
            !string.Equals(authCode.RedirectUri, request.RedirectUri, StringComparison.Ordinal))
        {
            return GrantResult.Failure(OidcConstants.Errors.InvalidGrant, "redirect_uri does not match");
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

            // Validate AllowedUsers restriction
            if (request.Client?.AllowedUsers != null && request.Client.AllowedUsers.Count > 0)
            {
                if (!request.Client.AllowedUsers.Contains(authCode.SubjectId))
                {
                    _logger.LogWarning(
                        "User {SubjectId} not in AllowedUsers for client {ClientId}",
                        authCode.SubjectId, request.Client.ClientId);
                    return GrantResult.Failure(OidcConstants.Errors.AccessDenied,
                        "User is not authorized to access this application");
                }
            }

            // Validate AllowedRoles restriction
            if (request.Client?.AllowedRoles != null && request.Client.AllowedRoles.Count > 0)
            {
                var userRoles = await _profileService.GetUserRolesAsync(authCode.SubjectId, cancellationToken);
                var hasAllowedRole = request.Client.AllowedRoles.Any(r =>
                    userRoles.Contains(r, StringComparer.OrdinalIgnoreCase));

                if (!hasAllowedRole)
                {
                    _logger.LogWarning(
                        "User {SubjectId} does not have any required role for client {ClientId}. User roles: [{UserRoles}], Required: [{AllowedRoles}]",
                        authCode.SubjectId, request.Client.ClientId,
                        string.Join(", ", userRoles),
                        string.Join(", ", request.Client.AllowedRoles));
                    return GrantResult.Failure(OidcConstants.Errors.AccessDenied,
                        "User does not have the required role to access this application");
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
                cancellationToken);

            foreach (var claim in profileClaims)
            {
                claims[claim.Key] = claim.Value;
            }
        }

        // Merge with any claims stored in the authorization code
        if (authCode.Claims != null)
        {
            foreach (var claim in authCode.Claims)
            {
                if (!claims.ContainsKey(claim.Key))
                {
                    claims[claim.Key] = claim.Value;
                }
            }
        }

        return new GrantResult
        {
            SubjectId = authCode.SubjectId,
            SessionId = authCode.SessionId,
            Scopes = authCode.Scopes,
            Claims = claims
        };
    }
}
