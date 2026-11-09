using System.Text.Json;
using Oluso.Core.Domain.Entities;
using Oluso.Core.Domain.Interfaces;
using Oluso.Core.Protocols;
using Oluso.Core.Protocols.Grants;
using Oluso.Core.Protocols.Models;
using Oluso.Core.Services;

namespace Oluso.Protocols.Grants;

/// <summary>
/// Handles device_code grant type (RFC 8628 §3.4/§3.5). Until the user answers on the verification page the
/// token endpoint answers <c>authorization_pending</c> (or <c>slow_down</c> when the device polls faster than
/// the interval it was given); a denial answers <c>access_denied</c> and an expired code <c>expired_token</c>.
/// An approved code is redeemed once, for the user, session and scopes recorded at approval.
/// </summary>
public class DeviceCodeGrantHandler : IGrantHandler
{
    private readonly IDeviceFlowStore _deviceFlowStore;
    private readonly IProfileService? _profileService;
    private readonly IClientStore? _clientStore;
    private readonly IClientUserAccess _clientUserAccess;

    public string GrantType => OidcConstants.GrantTypes.DeviceCode;

    public DeviceCodeGrantHandler(
        IDeviceFlowStore deviceFlowStore,
        IClientUserAccess clientUserAccess,
        IProfileService? profileService = null,
        IClientStore? clientStore = null)
    {
        _clientUserAccess = clientUserAccess;
        _deviceFlowStore = deviceFlowStore;
        _profileService = profileService;
        _clientStore = clientStore;
    }

    public async Task<GrantResult> HandleAsync(TokenRequest request, CancellationToken cancellationToken = default)
    {
        // DeviceCode presence is validated by TokenRequestValidator

        // Look up device code
        var deviceCode = await _deviceFlowStore.FindByDeviceCodeAsync(request.DeviceCode!, cancellationToken);
        if (deviceCode == null)
        {
            return GrantResult.Failure(OidcConstants.Errors.InvalidGrant, "Invalid device code");
        }

        // Validate client
        if (deviceCode.ClientId != request.Client?.ClientId)
        {
            return GrantResult.Failure(OidcConstants.Errors.InvalidGrant, "Device code was issued to different client");
        }

        // Check expiration
        if (deviceCode.Expiration < DateTime.UtcNow)
        {
            await _deviceFlowStore.RemoveByDeviceCodeAsync(request.DeviceCode!, cancellationToken);
            return GrantResult.Failure(OidcConstants.Errors.ExpiredToken, "Device code has expired");
        }

        var codeData = DeviceCodeData.Read(deviceCode);
        if (codeData == null)
        {
            return GrantResult.Failure(OidcConstants.Errors.InvalidGrant, "Invalid device code data");
        }

        // Check authorization status
        switch (codeData.Status)
        {
            case DeviceCodeStatus.Pending:
                return await PendingAsync(deviceCode, codeData, cancellationToken);

            case DeviceCodeStatus.Denied:
                await _deviceFlowStore.RemoveByDeviceCodeAsync(request.DeviceCode!, cancellationToken);
                return GrantResult.Failure(OidcConstants.Errors.AccessDenied, "User denied authorization");

            case DeviceCodeStatus.Authorized:
                // Success! Remove the device code (it is redeemed once) and return the grant
                await _deviceFlowStore.RemoveByDeviceCodeAsync(request.DeviceCode!, cancellationToken);
                return await AuthorizedAsync(request, deviceCode, codeData, cancellationToken);

            default:
                return GrantResult.Failure(OidcConstants.Errors.InvalidGrant, "Unknown device code status");
        }
    }

    /// <summary>
    /// <c>authorization_pending</c>, or <c>slow_down</c> when this poll came sooner than the interval allows
    /// (RFC 8628 §3.5). Each slow_down adds 5 seconds to the interval the device must keep from then on.
    /// </summary>
    private async Task<GrantResult> PendingAsync(DeviceFlowCode deviceCode, DeviceCodeData codeData, CancellationToken cancellationToken)
    {
        var now = DateTime.UtcNow;
        var interval = codeData.Interval > 0 ? codeData.Interval : DeviceCodeData.DefaultInterval;
        var tooSoon = codeData.LastPolledAt.HasValue && now - codeData.LastPolledAt.Value < TimeSpan.FromSeconds(interval);

        codeData.PollCount++;
        codeData.LastPolledAt = now;
        if (tooSoon)
        {
            codeData.Interval = interval + 5;
        }

        deviceCode.Data = JsonSerializer.Serialize(codeData);
        await _deviceFlowStore.UpdateByUserCodeAsync(deviceCode.UserCode, deviceCode, cancellationToken);

        return tooSoon
            ? GrantResult.Failure(OidcConstants.Errors.SlowDown, "Polling too fast; wait longer between requests")
            : GrantResult.Failure(OidcConstants.Errors.AuthorizationPending, "User has not yet authorized this device");
    }

    private async Task<GrantResult> AuthorizedAsync(TokenRequest request, DeviceFlowCode deviceCode, DeviceCodeData codeData, CancellationToken cancellationToken)
    {
        var claims = new Dictionary<string, object>();
        var permissions = new List<string>();

        // The client's user restrictions, against the tenant the approving session was for.
        if (!string.IsNullOrEmpty(deviceCode.SubjectId) && request.Client != null)
        {
            var approvedTenant = codeData.Claims.GetValueOrDefault(OidcConstants.StandardClaims.TenantId);
            var denial = await _clientUserAccess.FindDenialAsync(
                request.Client.ClientId, ClientUserRestrictions.From(request.Client),
                deviceCode.SubjectId, approvedTenant, cancellationToken);
            if (denial != null)
            {
                return GrantResult.Failure(OidcConstants.Errors.AccessDenied, denial);
            }
        }

        if (!string.IsNullOrEmpty(deviceCode.SubjectId) && _profileService != null)
        {
            if (!await _profileService.IsActiveAsync(deviceCode.SubjectId, cancellationToken))
            {
                return GrantResult.Failure(OidcConstants.Errors.InvalidGrant, "User is not active");
            }

            var fullClient = _clientStore != null
                ? await _clientStore.FindClientByIdAsync(deviceCode.ClientId, cancellationToken)
                : null;
            var profileClaims = await _profileService.GetProfileClaimsAsync(
                deviceCode.SubjectId,
                fullClient ?? new Client { ClientId = deviceCode.ClientId },
                codeData.Scopes,
                "TokenEndpoint",
                "oidc",
                request.Resource,
                null,
                cancellationToken);
            foreach (var claim in profileClaims)
            {
                claims[claim.Key] = claim.Value;
            }
        }

        // What describes the approving session (tenant_id, idp, remember-me), as an authorization code carries it.
        foreach (var claim in codeData.Claims)
        {
            claims.TryAdd(claim.Key, claim.Value);
        }

        if (!string.IsNullOrEmpty(deviceCode.SubjectId) && _profileService != null
            && codeData.Scopes.Contains(OidcConstants.Scopes.Permissions))
        {
            var tenantId = claims.TryGetValue("tenant_id", out var tid) ? tid?.ToString() : null;
            permissions = (await _profileService.GetUserPermissionsAsync(deviceCode.SubjectId, tenantId, cancellationToken))?.ToList()
                ?? new List<string>();
        }

        return new GrantResult
        {
            SubjectId = deviceCode.SubjectId,
            SessionId = deviceCode.SessionId,
            Scopes = codeData.Scopes,
            Claims = claims,
            Amr = codeData.Amr.Count > 0 ? codeData.Amr.ToList() : null,
            Acr = codeData.Acr,
            Permissions = permissions
        };
    }
}

/// <summary>
/// Data stored in device code
/// </summary>
public class DeviceCodeData
{
    /// <summary>The polling interval (seconds) handed out with a new device code.</summary>
    public const int DefaultInterval = 5;

    public DeviceCodeStatus Status { get; set; } = DeviceCodeStatus.Pending;
    public ICollection<string> Scopes { get; set; } = new List<string>();
    public DateTime? AuthorizedAt { get; set; }
    public int PollCount { get; set; }
    public DateTime? LastPolledAt { get; set; }

    /// <summary>The minimum seconds between polls; grows by 5 with each <c>slow_down</c>.</summary>
    public int Interval { get; set; } = DefaultInterval;

    /// <summary>When the user answered on the verification page (approval or denial).</summary>
    public DateTime? AnsweredAt { get; set; }

    /// <summary>Session claims recorded at approval (tenant_id, idp, remember-me), added to the tokens.</summary>
    public Dictionary<string, string> Claims { get; set; } = new();

    /// <summary>How the approving user signed in.</summary>
    public ICollection<string> Amr { get; set; } = new List<string>();
    public string? Acr { get; set; }

    /// <summary>The session of the browser the user approved from (the device gets its own session id).</summary>
    public string? ApprovedFromSessionId { get; set; }

    /// <summary>The device code's data, or null when it can't be read.</summary>
    public static DeviceCodeData? Read(DeviceFlowCode code)
    {
        try
        {
            return JsonSerializer.Deserialize<DeviceCodeData>(code.Data);
        }
        catch (JsonException)
        {
            return null;
        }
    }
}

public enum DeviceCodeStatus
{
    Pending,
    Authorized,
    Denied
}
