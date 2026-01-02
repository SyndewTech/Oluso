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
    private readonly ILogger<ResourceOwnerPasswordGrantHandler> _logger;

    public string GrantType => OidcConstants.GrantTypes.Password;

    public ResourceOwnerPasswordGrantHandler(
        ITenantContext tenantContext,
        IProfileService profileService,
        IClientStore clientStore,
        ILogger<ResourceOwnerPasswordGrantHandler> logger,
        IOlusoUserService? userService = null,
        IResourceOwnerPasswordValidator? passwordValidator = null)
    {
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
                Username = request.UserName,
                Password = request.Password,
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
            var tenantId = _tenantContext.HasTenant ? _tenantContext.TenantId : null;
            var validationResult = await _userService.ValidateCredentialsAsync(
                request.UserName,
                request.Password,
                tenantId,
                cancellationToken);

            if (!validationResult.Success)
            {
                if (validationResult.IsLockedOut)
                {
                    return GrantResult.Failure(OidcConstants.Errors.InvalidGrant, "User account is locked out");
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

            // Validate user is allowed to access this client (AllowedUsers/AllowedRoles)
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
            : request.Client?.AllowedScopes?.ToList() ?? new List<string>();

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

        return new GrantResult
        {
            SubjectId = subjectId,
            Scopes = scopes,
            Claims = resultClaims
        };
    }

    /// <summary>
    /// Validates that a user is allowed to access a client based on AllowedUsers and AllowedRoles restrictions.
    /// </summary>
    private async Task<(bool IsAllowed, string? Reason)> ValidateUserClientAccessAsync(
        string clientId,
        string userId,
        CancellationToken cancellationToken)
    {
        // Fetch full client entity to get AllowedUsers and AllowedRoles
        var client = await _clientStore.FindClientByIdAsync(clientId, cancellationToken);
        if (client == null)
        {
            return (false, "Client not found");
        }

        var hasUserRestrictions = client.AllowedUsers.Count > 0;
        var hasRoleRestrictions = client.AllowedRoles.Count > 0;

        // No restrictions - allow all users
        if (!hasUserRestrictions && !hasRoleRestrictions)
        {
            return (true, null);
        }

        // Check if user is in allowed users list
        if (hasUserRestrictions && client.AllowedUsers.Any(au => au.SubjectId == userId))
        {
            return (true, null);
        }

        // Check if user has any of the allowed roles
        if (hasRoleRestrictions && _userService != null)
        {
            var userRoles = await _userService.GetRolesAsync(userId, cancellationToken);
            var allowedRoleNames = client.AllowedRoles.Select(r => r.Role).ToHashSet(StringComparer.OrdinalIgnoreCase);
            if (userRoles.Any(r => allowedRoleNames.Contains(r)))
            {
                return (true, null);
            }
        }

        // User is not allowed
        var reason = hasUserRestrictions && hasRoleRestrictions
            ? "User is not authorized to access this application"
            : hasUserRestrictions
                ? "User is not in the allowed users list for this application"
                : "User does not have any of the required roles for this application";

        return (false, reason);
    }
}
