using System.Text.Json;
using Oluso.Core.Domain.Entities;
using Oluso.Core.Domain.Interfaces;
using Oluso.Core.Protocols;
using Oluso.Core.Protocols.Grants;
using Oluso.Core.Protocols.Models;
using Oluso.Core.Services;

namespace Oluso.Protocols.Grants;

/// <summary>
/// Handles refresh_token grant type
/// </summary>
public class RefreshTokenGrantHandler : IGrantHandler
{
    private readonly IPersistedGrantStore _grantStore;
    private readonly IProfileService _profileService;
    private readonly IClientStore _clientStore;
    private readonly IOlusoUserService _userService;

    public string GrantType => OidcConstants.GrantTypes.RefreshToken;

    public RefreshTokenGrantHandler(
        IPersistedGrantStore grantStore,
        IProfileService profileService,
        IClientStore clientStore,
        IOlusoUserService userService)
    {
        _grantStore = grantStore;
        _profileService = profileService;
        _clientStore = clientStore;
        _userService = userService;
    }

    public async Task<GrantResult> HandleAsync(TokenRequest request, CancellationToken cancellationToken = default)
    {
        // RefreshToken presence is validated by TokenRequestValidator
        var client = request.Client!;

        // Look up refresh token
        var grant = await _grantStore.GetAsync(request.RefreshToken!, cancellationToken);
        if (grant == null)
        {
            return GrantResult.Failure(OidcConstants.Errors.InvalidGrant, "Invalid refresh token");
        }

        // Validate grant type
        if (grant.Type != "refresh_token")
        {
            return GrantResult.Failure(OidcConstants.Errors.InvalidGrant, "Invalid token type");
        }

        // Validate client
        if (grant.ClientId != client.ClientId)
        {
            return GrantResult.Failure(OidcConstants.Errors.InvalidGrant, "Refresh token was issued to different client");
        }

        // Deserialize refresh token data
        RefreshTokenData? tokenData;
        try
        {
            tokenData = JsonSerializer.Deserialize<RefreshTokenData>(grant.Data);
        }
        catch
        {
            return GrantResult.Failure(OidcConstants.Errors.InvalidGrant, "Invalid refresh token data");
        }

        if (tokenData == null)
        {
            return GrantResult.Failure(OidcConstants.Errors.InvalidGrant, "Invalid refresh token data");
        }

        // Check absolute lifetime expiration
        if (grant.Expiration.HasValue && grant.Expiration.Value < DateTime.UtcNow)
        {
            await _grantStore.RemoveAsync(request.RefreshToken, cancellationToken);
            return GrantResult.Failure(OidcConstants.Errors.InvalidGrant, "Refresh token has expired");
        }

        // Check sliding expiration if configured
        var refreshTokenExpiration = (TokenExpiration)client.RefreshTokenExpiration;
        if (refreshTokenExpiration == TokenExpiration.Sliding)
        {
            // For sliding expiration, check if it's been inactive too long
            var slidingLifetime = TimeSpan.FromSeconds(client.SlidingRefreshTokenLifetime);
            var lastActivity = grant.ConsumedTime ?? grant.CreationTime;

            if (DateTime.UtcNow > lastActivity.Add(slidingLifetime))
            {
                await _grantStore.RemoveAsync(request.RefreshToken, cancellationToken);
                return GrantResult.Failure(OidcConstants.Errors.InvalidGrant, "Refresh token has expired due to inactivity");
            }
        }

        // Check if token was already consumed (for OneTimeOnly usage)
        var refreshTokenUsage = (TokenUsage)client.RefreshTokenUsage;
        if (refreshTokenUsage == TokenUsage.OneTimeOnly && grant.ConsumedTime.HasValue)
        {
            // Token replay attack - revoke all tokens for this session
            await _grantStore.RemoveAllAsync(new PersistedGrantFilter
            {
                SubjectId = grant.SubjectId,
                ClientId = grant.ClientId,
                SessionId = grant.SessionId
            }, cancellationToken);
            return GrantResult.Failure(OidcConstants.Errors.InvalidGrant, "Refresh token has already been used");
        }

        // Check if user is still active
        if (!string.IsNullOrEmpty(grant.SubjectId))
        {
            var isActive = await _profileService.IsActiveAsync(grant.SubjectId, cancellationToken);
            if (!isActive)
            {
                await _grantStore.RemoveAsync(request.RefreshToken, cancellationToken);
                return GrantResult.Failure(OidcConstants.Errors.InvalidGrant, "User is not active");
            }

            // Validate user is still allowed to access this client (AllowedUsers/AllowedRoles)
            var accessResult = await ValidateUserClientAccessAsync(client.ClientId, grant.SubjectId, cancellationToken);
            if (!accessResult.IsAllowed)
            {
                await _grantStore.RemoveAsync(request.RefreshToken, cancellationToken);
                return GrantResult.Failure(OidcConstants.Errors.AccessDenied, accessResult.Reason);
            }
        }

        // Handle scope reduction (client can request fewer scopes than original)
        var effectiveScopes = tokenData.Scopes;
        if (request.RequestedScopes.Any())
        {
            // Validate requested scopes are subset of original
            var invalidScopes = request.RequestedScopes.Except(tokenData.Scopes).ToList();
            if (invalidScopes.Any())
            {
                return GrantResult.Failure(
                    OidcConstants.Errors.InvalidScope,
                    $"Requested scope(s) not in original grant: {string.Join(", ", invalidScopes)}");
            }
            effectiveScopes = request.RequestedScopes.ToList();
        }

        // Handle token rotation based on client settings
        if (refreshTokenUsage == TokenUsage.OneTimeOnly)
        {
            // Mark the token as consumed - a new one will be issued by the token service
            grant.ConsumedTime = DateTime.UtcNow;
            await _grantStore.StoreAsync(grant, cancellationToken);
        }
        else if (refreshTokenExpiration == TokenExpiration.Sliding)
        {
            // For ReUse with sliding expiration, update the expiration time
            var newExpiration = CalculateNewExpiration(grant.CreationTime, client);
            if (newExpiration != grant.Expiration)
            {
                grant.Expiration = newExpiration;
                // Update last activity time for sliding window calculation
                grant.ConsumedTime = DateTime.UtcNow;
                await _grantStore.StoreAsync(grant, cancellationToken);
            }
        }

        // Get claims - either update them or use existing
        IDictionary<string, object> claims;
        if (client.UpdateAccessTokenClaimsOnRefresh && !string.IsNullOrEmpty(grant.SubjectId))
        {
            // Fetch the full client to pass to profile service
            var fullClient = await _clientStore.FindClientByIdAsync(client.ClientId, cancellationToken);

            // Fetch fresh claims from profile service
            claims = await _profileService.GetProfileClaimsAsync(
                grant.SubjectId,
                fullClient ?? new Client { ClientId = client.ClientId },
                effectiveScopes.ToList(),
                "TokenEndpoint",
                "oidc",
                cancellationToken);
        }
        else
        {
            // Use claims from the original token
            claims = tokenData.Claims?.ToDictionary(c => c.Key, c => (object)c.Value)
                    ?? new Dictionary<string, object>();
        }

        var result = new GrantResult
        {
            SubjectId = grant.SubjectId,
            SessionId = grant.SessionId,
            Scopes = effectiveScopes,
            Claims = claims
        };

        // Signal to token service about refresh token handling
        result.CustomData["refresh_token_usage"] = refreshTokenUsage;
        result.CustomData["original_refresh_token"] = request.RefreshToken;

        // For OneTimeOnly, signal that a new refresh token should be issued
        if (refreshTokenUsage == TokenUsage.OneTimeOnly)
        {
            result.CustomData["rotate_refresh_token"] = true;
        }

        return result;
    }

    /// <summary>
    /// Calculate the new expiration time respecting both absolute and sliding lifetimes
    /// </summary>
    private static DateTime CalculateNewExpiration(DateTime creationTime, ValidatedClient client)
    {
        var now = DateTime.UtcNow;

        // Calculate absolute maximum expiration
        var absoluteExpiration = creationTime.AddSeconds(client.AbsoluteRefreshTokenLifetime);

        // Calculate new sliding expiration
        var slidingExpiration = now.AddSeconds(client.SlidingRefreshTokenLifetime);

        // Return the earlier of the two (don't exceed absolute lifetime)
        return slidingExpiration < absoluteExpiration ? slidingExpiration : absoluteExpiration;
    }

    /// <summary>
    /// Validates that a user is still allowed to access a client based on AllowedUsers and AllowedRoles restrictions.
    /// </summary>
    private async Task<(bool IsAllowed, string? Reason)> ValidateUserClientAccessAsync(
        string clientId,
        string subjectId,
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
        if (hasUserRestrictions && client.AllowedUsers.Any(au => au.SubjectId == subjectId))
        {
            return (true, null);
        }

        // Check if user has any of the allowed roles
        if (hasRoleRestrictions)
        {
            var userRoles = await _userService.GetRolesAsync(subjectId, cancellationToken);
            var allowedRoleNames = client.AllowedRoles.Select(r => r.Role).ToHashSet(StringComparer.OrdinalIgnoreCase);
            if (userRoles.Any(r => allowedRoleNames.Contains(r)))
            {
                return (true, null);
            }
        }

        // User is not allowed
        var reason = hasUserRestrictions && hasRoleRestrictions
            ? "User is no longer authorized to access this application"
            : hasUserRestrictions
                ? "User is no longer in the allowed users list for this application"
                : "User no longer has any of the required roles for this application";

        return (false, reason);
    }
}

/// <summary>
/// Data stored in refresh token
/// </summary>
public class RefreshTokenData
{
    public DateTime CreatedAt { get; set; }
    public ICollection<string> Scopes { get; set; } = new List<string>();
    public IDictionary<string, string>? Claims { get; set; }
    public string? AccessTokenHash { get; set; }
}
