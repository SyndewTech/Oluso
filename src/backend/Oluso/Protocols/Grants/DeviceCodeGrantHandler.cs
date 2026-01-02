using System.Text.Json;
using Oluso.Core.Domain.Interfaces;
using Oluso.Core.Protocols;
using Oluso.Core.Protocols.Grants;
using Oluso.Core.Protocols.Models;

namespace Oluso.Protocols.Grants;

/// <summary>
/// Handles device_code grant type (RFC 8628)
/// </summary>
public class DeviceCodeGrantHandler : IGrantHandler
{
    private readonly IDeviceFlowStore _deviceFlowStore;

    public string GrantType => OidcConstants.GrantTypes.DeviceCode;

    public DeviceCodeGrantHandler(IDeviceFlowStore deviceFlowStore)
    {
        _deviceFlowStore = deviceFlowStore;
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
            await _deviceFlowStore.RemoveByDeviceCodeAsync(request.DeviceCode, cancellationToken);
            return GrantResult.Failure(OidcConstants.Errors.ExpiredToken, "Device code has expired");
        }

        // Deserialize device code data
        DeviceCodeData? codeData;
        try
        {
            codeData = JsonSerializer.Deserialize<DeviceCodeData>(deviceCode.Data);
        }
        catch
        {
            return GrantResult.Failure(OidcConstants.Errors.InvalidGrant, "Invalid device code data");
        }

        if (codeData == null)
        {
            return GrantResult.Failure(OidcConstants.Errors.InvalidGrant, "Invalid device code data");
        }

        // Check authorization status
        switch (codeData.Status)
        {
            case DeviceCodeStatus.Pending:
                return GrantResult.Failure(OidcConstants.Errors.AuthorizationPending, "User has not yet authorized this device");

            case DeviceCodeStatus.Denied:
                await _deviceFlowStore.RemoveByDeviceCodeAsync(request.DeviceCode, cancellationToken);
                return GrantResult.Failure(OidcConstants.Errors.AccessDenied, "User denied authorization");

            case DeviceCodeStatus.Authorized:
                // Success! Remove the device code and return grant
                await _deviceFlowStore.RemoveByDeviceCodeAsync(request.DeviceCode, cancellationToken);
                return new GrantResult
                {
                    SubjectId = deviceCode.SubjectId,
                    SessionId = deviceCode.SessionId,
                    Scopes = codeData.Scopes,
                    Claims = new Dictionary<string, object>()
                };

            default:
                return GrantResult.Failure(OidcConstants.Errors.InvalidGrant, "Unknown device code status");
        }
    }
}

/// <summary>
/// Data stored in device code
/// </summary>
public class DeviceCodeData
{
    public DeviceCodeStatus Status { get; set; } = DeviceCodeStatus.Pending;
    public ICollection<string> Scopes { get; set; } = new List<string>();
    public DateTime? AuthorizedAt { get; set; }
    public int PollCount { get; set; }
    public DateTime? LastPolledAt { get; set; }
}

public enum DeviceCodeStatus
{
    Pending,
    Authorized,
    Denied
}
