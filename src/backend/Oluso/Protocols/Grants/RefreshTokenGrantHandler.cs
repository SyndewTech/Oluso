using System.Text.Json;
using Oluso.Core;
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
    private readonly ITenantStore _tenantStore;
    private readonly ITenantContext _tenantContext;
    private readonly IClientUserAccess _clientUserAccess;

    public string GrantType => OidcConstants.GrantTypes.RefreshToken;

    public RefreshTokenGrantHandler(
        IPersistedGrantStore grantStore,
        IProfileService profileService,
        IClientStore clientStore,
        IOlusoUserService userService,
        ITenantStore tenantStore,
        ITenantContext tenantContext,
        IClientUserAccess clientUserAccess)
    {
        _clientUserAccess = clientUserAccess;
        _grantStore = grantStore;
        _profileService = profileService;
        _clientStore = clientStore;
        _userService = userService;
        _tenantStore = tenantStore;
        _tenantContext = tenantContext;
    }

    public async Task<GrantResult> HandleAsync(TokenRequest request, CancellationToken cancellationToken = default)
    {
        var client = request.Client!;

        // Validate refresh token parameter is present
        if (string.IsNullOrEmpty(request.RefreshToken))
        {
            return GrantResult.Failure(OidcConstants.Errors.InvalidGrant, "Refresh token is missing");
        }

        // Look up refresh token
        var grant = await _grantStore.GetAsync(request.RefreshToken, cancellationToken);
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

        // A refresh token bound to a DPoP key is redeemed only with a proof signed by that key (RFC 9449 §5),
        // so a copied token is useless and cannot be traded for unbound tokens. The token is left unspent.
        if (!string.IsNullOrEmpty(tokenData.DPoPJkt)
            && !string.Equals(tokenData.DPoPJkt, request.DPoPKeyThumbprint, StringComparison.Ordinal))
        {
            return GrantResult.Failure(OidcConstants.Errors.InvalidDPoPProof,
                "This refresh token is bound to a DPoP key; present a DPoP proof signed by that key.");
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

            // Validate user is still allowed to access this client
            var accessResult = await ValidateUserClientAccessAsync(client.ClientId, grant.SubjectId, cancellationToken);
            if (!accessResult.IsAllowed)
            {
                await _grantStore.RemoveAsync(request.RefreshToken, cancellationToken);
                return GrantResult.Failure(OidcConstants.Errors.AccessDenied, accessResult.Reason);
            }
        }

        // Handle scope reduction (client can request fewer scopes than original). RFC 6749 §6: the request must
        // not name a scope beyond the original grant, even one the server would have left out anyway.
        var effectiveScopes = tokenData.Scopes;
        if (request.ScopesAsked.Any())
        {
            // Validate requested scopes are subset of original
            var invalidScopes = request.ScopesAsked.Except(tokenData.Scopes).ToList();
            if (invalidScopes.Any())
            {
                return GrantResult.Failure(
                    OidcConstants.Errors.InvalidScope,
                    $"Requested scope(s) not in original grant: {string.Join(", ", invalidScopes)}");
            }
            effectiveScopes = request.RequestedScopes.ToList();
        }

        // RFC 8707 §2.2: a refresh may ask for any of the grant's resources, but no others.
        // Checked before the token is consumed, so a refused request doesn't burn it.
        var grantedResources = tokenData.Resources ?? new List<string>();
        var resources = ResourceIndicators.Narrow(grantedResources.ToList(), request.Resource.ToList());
        if (resources == null)
        {
            return GrantResult.Failure(OidcConstants.Errors.InvalidTarget,
                "The requested resource was not part of the original grant");
        }

        // The tenant the new tokens are for: the one the request names, else the one this refresh token was
        // issued for, so a switched session stays switched across refreshes. Checked and claimed before the token
        // is consumed, so a refused switch leaves it usable.
        var targetTenantId = request.Tenant
            ?? tokenData.ClaimText("tenant_id");
        if (request.Tenant != null)
        {
            var tenant = await _tenantStore.GetByIdAsync(request.Tenant, cancellationToken);
            if (tenant == null || !tenant.Enabled)
            {
                return GrantResult.Failure(OidcConstants.Errors.InvalidRequest, "The requested tenant does not exist or is disabled");
            }

            // A tenant's own host issues only that tenant's tokens (they are signed with its key); a switch to
            // another tenant goes through the platform.
            var routedTenantId = _tenantContext.TenantId;
            if (!string.IsNullOrEmpty(routedTenantId)
                && routedTenantId != PlatformConstants.TenantId
                && !string.Equals(routedTenantId, request.Tenant, StringComparison.OrdinalIgnoreCase))
            {
                return GrantResult.Failure(OidcConstants.Errors.InvalidRequest,
                    "Tokens for another tenant are requested through the platform, not a tenant's host");
            }
        }

        IDictionary<string, object> claims;
        if ((request.Tenant != null || client.UpdateAccessTokenClaimsOnRefresh || tokenData.ClaimsNeedRebuild) && !string.IsNullOrEmpty(grant.SubjectId))
        {
            var fullClient = await _clientStore.FindClientByIdAsync(client.ClientId, cancellationToken)
                ?? new Client { ClientId = client.ClientId };

            // Fresh claims from the profile service. Authorize-parameter claims are request-scoped; on a
            // claims-refresh there is no /authorize request to source them from.
            claims = string.IsNullOrEmpty(targetTenantId)
                ? await _profileService.GetProfileClaimsAsync(
                    grant.SubjectId, fullClient, effectiveScopes.ToList(), "TokenEndpoint", "oidc",
                    request.Resource, additionalData: null, cancellationToken)
                : await _profileService.GetProfileClaimsForTenantAsync(
                    grant.SubjectId, fullClient, effectiveScopes.ToList(), targetTenantId, "TokenEndpoint", "oidc",
                    request.Resource, cancellationToken);
            // Rebuilt claims describe the user; when they signed in still comes from the original sign-in.
            foreach (var signInClaim in new[] { OidcConstants.StandardClaims.AuthTime, "max_age_requested" })
            {
                if (tokenData.ClaimText(signInClaim) is { } value) claims[signInClaim] = value;
            }
        }
        else
        {
            // Use claims from the original token
            claims = tokenData.ReadClaims();
        }

        if (request.Tenant != null)
        {
            var claimedTenant = claims.TryGetValue("tenant_id", out var claimed) ? claimed?.ToString() : null;
            if (!string.Equals(claimedTenant, request.Tenant, StringComparison.OrdinalIgnoreCase))
            {
                return GrantResult.Failure(OidcConstants.Errors.AccessDenied, "The user has no access to the requested tenant");
            }
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

        // Refresh permissions on token refresh only when the "permissions" scope is present
        // This picks up any role changes since the original token was issued
        var permissions = new List<string>();
        if (!string.IsNullOrEmpty(grant.SubjectId) && effectiveScopes.Contains(OidcConstants.Scopes.Permissions))
        {
            var tenantId = claims.TryGetValue("tenant_id", out var tid) ? tid?.ToString() : null;
            permissions = (await _profileService.GetUserPermissionsAsync(
                grant.SubjectId, tenantId, cancellationToken)).ToList();
        }

        var result = new GrantResult
        {
            SubjectId = grant.SubjectId,
            SessionId = grant.SessionId,
            // The sign-in this refresh token came from, unchanged by refreshing.
            Amr = tokenData.Amr?.ToList(),
            Acr = tokenData.Acr,
            Scopes = effectiveScopes,
            Claims = claims,
            Permissions = permissions,
            Resources = resources.ToList(),
            GrantedResources = grantedResources.Count > 0 ? grantedResources.ToList() : resources.ToList(),
            // RFC 6749 §6: asking for fewer scopes narrows this access token only; the rotated refresh token keeps the
            // grant's, so one grant can mint tokens for different APIs (each asking for its own scopes).
            GrantedScopes = tokenData.Scopes.ToList()
        };

        // Signal to token service about refresh token handling
        result.CustomData["refresh_token_usage"] = refreshTokenUsage;
        result.CustomData["original_refresh_token"] = request.RefreshToken;

        // For OneTimeOnly, signal that a new refresh token should be issued. A tenant switch always gets one:
        // the new token carries the tenant, which a reused token would not.
        if (refreshTokenUsage == TokenUsage.OneTimeOnly || request.Tenant != null)
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
    /// Whether the user may still have tokens for the client: its AllowedUsers, AllowedRoles, RequiredPermission and
    /// RequireVerifiedEmail, all of which must be met. The permission is looked up in the user's tenant.
    /// </summary>
    private async Task<(bool IsAllowed, string? Reason)> ValidateUserClientAccessAsync(
        string clientId,
        string subjectId,
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

        var user = await _userService.FindByIdAsync(subjectId, cancellationToken);
        var denial = await _clientUserAccess.FindDenialAsync(clientId, restrictions, subjectId, user?.TenantId, cancellationToken);
        return (denial == null, denial);
    }
}

/// <summary>
/// Data stored in refresh token
/// </summary>
public class RefreshTokenData
{
    /// <summary>
    /// <see cref="Claims"/> hold each claim as JSON in its own shape (text, list, object). Before this format every
    /// value was stored as text, which turned a multi-valued claim such as <c>role</c> into a .NET type name.
    /// </summary>
    public const int JsonClaimsFormat = 2;

    public DateTime CreatedAt { get; set; }
    public ICollection<string> Scopes { get; set; } = new List<string>();
    public IDictionary<string, JsonElement>? Claims { get; set; }
    /// <summary>How <see cref="Claims"/> are stored: <see cref="JsonClaimsFormat"/>, or 0 for the older all-text form.</summary>
    public int ClaimsFormat { get; set; }
    public string? AccessTokenHash { get; set; }

    /// <summary>Thumbprint of the DPoP key the refresh token is bound to (RFC 9449), or null when unbound.</summary>
    public string? DPoPJkt { get; set; }

    /// <summary>Resource indicators of the grant (RFC 8707); null on tokens issued before they were kept.</summary>
    public ICollection<string>? Resources { get; set; }

    /// <summary>
    /// How the user signed in (<c>amr</c>) and the authentication context (<c>acr</c>), so ID tokens issued on
    /// refresh describe the same sign-in. <c>auth_time</c> travels in <see cref="Claims"/>.
    /// </summary>
    public ICollection<string>? Amr { get; set; }

    public string? Acr { get; set; }

    /// <summary>
    /// True when the claims were stored in the older all-text form, where a multi-valued claim was lost. They are
    /// rebuilt from the user's profile rather than carried into the next token.
    /// </summary>
    public bool ClaimsNeedRebuild => Claims is { Count: > 0 } && ClaimsFormat < JsonClaimsFormat;

    /// <summary>The token's claims as stored, for storing with a refresh token.</summary>
    public static Dictionary<string, JsonElement> StoreClaims(IDictionary<string, object> claims) =>
        claims.ToDictionary(c => c.Key, c => JsonSerializer.SerializeToElement(c.Value ?? string.Empty));

    /// <summary>
    /// The stored claims in the shapes the token service writes: text as a string, a list of text as a string list,
    /// anything else as its JSON.
    /// </summary>
    public Dictionary<string, object> ReadClaims() =>
        Claims?.ToDictionary(c => c.Key, c => ClaimValue(c.Value)) ?? new Dictionary<string, object>();

    /// <summary>A single-text claim's value, or null when absent or not text.</summary>
    public string? ClaimText(string type) =>
        Claims != null && Claims.TryGetValue(type, out var value) && value.ValueKind == JsonValueKind.String ? value.GetString() : null;

    private static object ClaimValue(JsonElement value) => value.ValueKind switch
    {
        JsonValueKind.String => value.GetString() ?? string.Empty,
        JsonValueKind.Array when value.EnumerateArray().All(e => e.ValueKind == JsonValueKind.String) =>
            value.EnumerateArray().Select(e => e.GetString() ?? string.Empty).ToList(),
        _ => value.Clone(),
    };
}
