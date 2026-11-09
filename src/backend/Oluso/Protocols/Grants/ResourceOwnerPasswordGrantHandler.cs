using Microsoft.Extensions.Logging;
using Oluso.Core.Domain.Interfaces;
using Oluso.Core.Protocols;
using Oluso.Core.Protocols.Grants;
using Oluso.Core.Protocols.Models;
using Oluso.Core.Services;

namespace Oluso.Protocols.Grants;

/// <summary>
/// Handles password grant type (Resource Owner Password Credentials).
///
/// Note: This grant type is deprecated in OAuth 2.1 and should be avoided in favor
/// of authorization code flow with PKCE. It's provided for legacy compatibility and
/// specific scenarios like first-party mobile apps where redirect-based flows are impractical.
/// </summary>
public class ResourceOwnerPasswordGrantHandler : IGrantHandler
{
    private readonly IOlusoUserService? _userService;
    private readonly IResourceOwnerPasswordValidator? _passwordValidator;
    private readonly ITenantContext _tenantContext;
    private readonly IProfileService _profileService;
    private readonly IClientStore _clientStore;
    private readonly IResourceStore? _resourceStore;
    private readonly IClientUserAccess _clientUserAccess;
    private readonly ILogger<ResourceOwnerPasswordGrantHandler> _logger;

    public string GrantType => OidcConstants.GrantTypes.Password;

    public ResourceOwnerPasswordGrantHandler(
        ITenantContext tenantContext,
        IProfileService profileService,
        IClientStore clientStore,
        IClientUserAccess clientUserAccess,
        ILogger<ResourceOwnerPasswordGrantHandler> logger,
        IOlusoUserService? userService = null,
        IResourceOwnerPasswordValidator? passwordValidator = null,
        IResourceStore? resourceStore = null)
    {
        _resourceStore = resourceStore;
        _clientUserAccess = clientUserAccess;
        _tenantContext = tenantContext;
        _profileService = profileService;
        _clientStore = clientStore;
        _logger = logger;
        _userService = userService;
        _passwordValidator = passwordValidator;
    }

    public async Task<GrantResult> HandleAsync(TokenRequest request, CancellationToken cancellationToken = default)
    {
        // Username and Password presence is validated by TokenRequestValidator
        _logger.LogDebug("Processing password grant for user: {Username}", request.UserName);

        // Try custom validator first if registered
        if (_passwordValidator != null)
        {
            // Fetch full client entity for the validator context
            var client = await _clientStore.FindClientByIdAsync(request.Client!.ClientId, cancellationToken);
            if (client == null)
            {
                return GrantResult.Failure(OidcConstants.Errors.InvalidClient, "Client not found");
            }

            var context = new ResourceOwnerPasswordValidationContext
            {
                Username = request.UserName!,
                Password = request.Password!,
                Client = client,
                RequestedScopes = request.RequestedScopes
            };

            await _passwordValidator.ValidateAsync(context);

            if (context.Result.IsValid)
            {
                return await BuildSuccessResultAsync(
                    context.Result.SubjectId!,
                    request,
                    context.Result.Claims?.ToDictionary(c => c.Type, c => (object)c.Value),
                    cancellationToken);
            }

            return GrantResult.Failure(
                context.Result.Error ?? OidcConstants.Errors.InvalidGrant,
                context.Result.ErrorDescription ?? "Invalid username or password");
        }

        // Fall back to IOlusoUserService if available
        if (_userService != null)
        {
            // Use the authenticated client's registered tenant — not _tenantContext (routing context).
            // The token endpoint is an OIDC protocol path where tenant resolution is from URL/header
            // without JWT validation, so _tenantContext is not a trusted source.
            var tenantId = request.Client?.TenantId;
            var validationResult = await _userService.ValidateCredentialsAsync(
                request.UserName!,
                request.Password!,
                tenantId,
                cancellationToken);

            if (!validationResult.Success)
            {
                if (validationResult.IsLockedOut)
                {
                    return GrantResult.Failure(OidcConstants.Errors.InvalidGrant, "User account is locked out");
                }

                if (validationResult.PasswordExpired)
                {
                    return GrantResult.Failure(OidcConstants.Errors.InvalidGrant,
                        "Password expired - sign in interactively to change it");
                }

                if (validationResult.RequiresMfa)
                {
                    return GrantResult.Failure(OidcConstants.Errors.InvalidGrant,
                        "MFA required - use authorization_code flow for MFA");
                }

                return GrantResult.Failure(
                    validationResult.Error ?? OidcConstants.Errors.InvalidGrant,
                    validationResult.ErrorDescription ?? "Invalid username or password");
            }

            var user = validationResult.User!;

            // Check if user is active
            if (!user.IsActive)
            {
                return GrantResult.Failure(OidcConstants.Errors.InvalidGrant, "User account is deactivated");
            }

            // Validate user is allowed to access this client
            if (request.Client != null)
            {
                var accessResult = await ValidateUserClientAccessAsync(
                    request.Client.ClientId,
                    user.Id,
                    cancellationToken);

                if (!accessResult.IsAllowed)
                {
                    return GrantResult.Failure(OidcConstants.Errors.AccessDenied, accessResult.Reason);
                }
            }

            // Record successful login
            await _userService.RecordLoginAsync(user.Id, cancellationToken);

            return await BuildSuccessResultAsync(user.Id, request, null, cancellationToken);
        }

        // No validator available
        _logger.LogWarning("Password grant requested but no IResourceOwnerPasswordValidator or IOlusoUserService is registered");
        return GrantResult.Failure(OidcConstants.Errors.UnsupportedGrantType,
            "Password grant is not configured on this server");
    }

    private async Task<GrantResult> BuildSuccessResultAsync(
        string subjectId,
        TokenRequest request,
        IDictionary<string, object>? additionalClaims,
        CancellationToken cancellationToken)
    {
        // Determine scopes
        var scopes = request.RequestedScopes.Any()
            ? request.RequestedScopes.ToList()
            : await ApiScopePrefixes.WithoutPrefixScopesAsync(
                _resourceStore, request.Client?.AllowedScopes ?? new List<string>(), cancellationToken);

        // Fetch full client to pass to profile service
        var fullClient = request.Client != null
            ? await _clientStore.FindClientByIdAsync(request.Client.ClientId, cancellationToken)
            : null;

        // Get claims from profile service with protocol context
        var claims = await _profileService.GetProfileClaimsAsync(
            subjectId,
            fullClient ?? new Core.Domain.Entities.Client { ClientId = request.Client?.ClientId ?? "internal" },
            scopes,
            "TokenEndpoint",
            "oidc",
            request.Resource,
            additionalData: null,
            cancellationToken);

        // Merge with any additional claims from validator
        var resultClaims = new Dictionary<string, object>(claims);
        if (additionalClaims != null)
        {
            foreach (var claim in additionalClaims)
            {
                if (!resultClaims.ContainsKey(claim.Key))
                {
                    resultClaims[claim.Key] = claim.Value;
                }
            }
        }

        // Collect permissions from user's roles only when the "permissions" scope is requested
        var permissions = new List<string>();
        if (scopes.Contains(OidcConstants.Scopes.Permissions))
        {
            var tenantId = resultClaims.TryGetValue("tenant_id", out var tid) ? tid?.ToString() : null;
            permissions = (await _profileService.GetUserPermissionsAsync(
                subjectId, tenantId, cancellationToken)).ToList();
        }

        return new GrantResult
        {
            SubjectId = subjectId,
            Scopes = scopes,
            Claims = resultClaims,
            Permissions = permissions
        };
    }

    /// <summary>
    /// Whether the user may still have tokens for the client: its AllowedUsers, AllowedRoles, RequiredPermission and
    /// RequireVerifiedEmail, all of which must be met. The permission is looked up in the user's tenant.
    /// </summary>
    private async Task<(bool IsAllowed, string? Reason)> ValidateUserClientAccessAsync(
        string clientId,
        string userId,
        CancellationToken cancellationToken)
    {
        var client = await _clientStore.FindClientByIdAsync(clientId, cancellationToken);
        if (client == null)
        {
            return (false, "Client not found");
        }

        var restrictions = ClientUserRestrictions.From(client);
        if (restrictions.IsEmpty)
        {
            return (true, null);
        }

        var user = _userService != null ? await _userService.FindByIdAsync(userId, cancellationToken) : null;
        var denial = await _clientUserAccess.FindDenialAsync(clientId, restrictions, userId, user?.TenantId, cancellationToken);
        return (denial == null, denial);
    }
}
