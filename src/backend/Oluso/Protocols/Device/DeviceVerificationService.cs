using System.Text.Json;
using Microsoft.Extensions.Logging;
using Oluso.Core.Domain.Entities;
using Oluso.Core.Domain.Interfaces;
using Oluso.Core.Events;
using Oluso.Core.Protocols;
using Oluso.Core.Protocols.Models;
using Oluso.Core.Services;
using Oluso.Protocols.Grants;

namespace Oluso.Protocols.Device;

/// <summary>
/// The user's side of the device authorization grant (RFC 8628 §3.3): look up a user code, show what the device
/// asks for, and record the signed-in user's approval or denial so the device's token request
/// (<see cref="DeviceCodeGrantHandler"/>) gets tokens or <c>access_denied</c>.
/// </summary>
public interface IDeviceVerificationService
{
    /// <summary>The pending request for <paramref name="userCode"/> (any case, with or without the dash).</summary>
    Task<DeviceVerificationLookup> FindAsync(string? userCode, CancellationToken cancellationToken = default);

    /// <summary>Approves the request for the signed-in user and their session.</summary>
    Task<DeviceVerificationLookup> ApproveAsync(string? userCode, DeviceApproval approval, CancellationToken cancellationToken = default);

    /// <summary>Denies the request.</summary>
    Task<DeviceVerificationLookup> DenyAsync(string? userCode, string subjectId, CancellationToken cancellationToken = default);
}

/// <summary>Who approved, from the signed-in session.</summary>
public sealed class DeviceApproval
{
    public required string SubjectId { get; init; }

    /// <summary>
    /// The approving browser session's id. Recorded for the audit trail; the device gets a session of its own
    /// (see <see cref="DeviceVerificationService.ApproveAsync"/>).
    /// </summary>
    public string? SessionId { get; init; }
    public IReadOnlyDictionary<string, string> Claims { get; init; } = new Dictionary<string, string>();
    public IReadOnlyCollection<string> Amr { get; init; } = Array.Empty<string>();
    public string? Acr { get; init; }
}

public enum DeviceVerificationStatus
{
    /// <summary>No code was given.</summary>
    Missing,
    /// <summary>No request has this code.</summary>
    NotFound,
    /// <summary>The request's time ran out.</summary>
    Expired,
    /// <summary>Waiting for the user's answer.</summary>
    Pending,
    /// <summary>The user approved.</summary>
    Approved,
    /// <summary>The user denied.</summary>
    Denied,
}

/// <summary>A device request as the verification page shows it.</summary>
public sealed class DeviceVerificationLookup
{
    public DeviceVerificationStatus Status { get; init; }

    /// <summary>The user code as shown to the user (XXXX-XXXX).</summary>
    public string? UserCode { get; init; }
    public string? ClientId { get; init; }
    public string? ClientName { get; init; }
    public string? LogoUri { get; init; }
    public string? ClientUri { get; init; }
    public string? PolicyUri { get; init; }
    public string? TosUri { get; init; }
    public IReadOnlyList<DeviceScopeDescription> Scopes { get; init; } = Array.Empty<DeviceScopeDescription>();

    /// <summary>The Matrix device id the device asks for (<c>urn:matrix:client:device:&lt;id&gt;</c>), if any.</summary>
    public string? MatrixDeviceId { get; init; }

    public DateTime? ExpiresAt { get; init; }

    internal static DeviceVerificationLookup Of(DeviceVerificationStatus status, string? userCode = null) =>
        new() { Status = status, UserCode = userCode };
}

/// <summary>One requested permission in plain words.</summary>
public sealed record DeviceScopeDescription(string Name, string DisplayName, string? Description);

public sealed class DeviceVerificationService : IDeviceVerificationService
{
    private readonly IDeviceFlowStore _deviceFlowStore;
    private readonly IClientStore _clientStore;
    private readonly IResourceStore? _resourceStore;
    private readonly ITenantSettingsProvider? _tenantSettings;
    private readonly IOlusoUserService? _userService;
    private readonly IOlusoEventService? _events;
    private readonly ITenantContext? _tenantContext;
    private readonly ILogger<DeviceVerificationService> _logger;

    public DeviceVerificationService(
        IDeviceFlowStore deviceFlowStore,
        IClientStore clientStore,
        ILogger<DeviceVerificationService> logger,
        IResourceStore? resourceStore = null,
        ITenantSettingsProvider? tenantSettings = null,
        IOlusoUserService? userService = null,
        IOlusoEventService? events = null,
        ITenantContext? tenantContext = null)
    {
        _deviceFlowStore = deviceFlowStore;
        _clientStore = clientStore;
        _logger = logger;
        _resourceStore = resourceStore;
        _tenantSettings = tenantSettings;
        _userService = userService;
        _events = events;
        _tenantContext = tenantContext;
    }

    /// <summary>
    /// The stored form of a user code: letters only, upper case. People type codes in any case, with or without
    /// the dash or spaces (RFC 8628 §6.1).
    /// </summary>
    public static string NormalizeUserCode(string? userCode) =>
        new((userCode ?? string.Empty).Where(char.IsLetterOrDigit).Select(char.ToUpperInvariant).ToArray());

    /// <summary>The user code as shown: XXXX-XXXX for the usual eight characters.</summary>
    public static string FormatUserCode(string userCode) =>
        userCode.Length == 8 ? $"{userCode[..4]}-{userCode[4..]}" : userCode;

    public async Task<DeviceVerificationLookup> FindAsync(string? userCode, CancellationToken cancellationToken = default)
    {
        var (code, data, status) = await LoadAsync(userCode, cancellationToken);
        if (code == null || data == null)
        {
            return DeviceVerificationLookup.Of(status);
        }
        return await DescribeAsync(code, data, status, cancellationToken);
    }

    public async Task<DeviceVerificationLookup> ApproveAsync(string? userCode, DeviceApproval approval, CancellationToken cancellationToken = default)
    {
        var (code, data, status) = await LoadAsync(userCode, cancellationToken);
        if (code == null || data == null || status != DeviceVerificationStatus.Pending)
        {
            return code != null && data != null
                ? await DescribeAsync(code, data, status, cancellationToken)
                : DeviceVerificationLookup.Of(status);
        }

        var claims = new Dictionary<string, string>(approval.Claims);
        if (!claims.ContainsKey("tenant_id") && _userService != null
            && await _userService.FindByIdAsync(approval.SubjectId, cancellationToken) is { TenantId: { Length: > 0 } tenantId })
        {
            claims["tenant_id"] = tenantId;
        }

        var now = DateTime.UtcNow;
        data.Status = DeviceCodeStatus.Authorized;
        data.AuthorizedAt = now;
        data.AnsweredAt = now;
        data.Claims = claims;
        data.Amr = approval.Amr.ToList();
        data.Acr = approval.Acr;
        data.ApprovedFromSessionId = approval.SessionId;

        code.SubjectId = approval.SubjectId;
        // The other device's sign-in is an authorization of its own, with its own session id (sid): revoking or
        // signing out one device then doesn't end the approving browser's session or other devices approved from it,
        // and back-channel logout names just that device.
        code.SessionId = Guid.NewGuid().ToString("N");
        code.Data = JsonSerializer.Serialize(data);
        await _deviceFlowStore.UpdateByUserCodeAsync(code.UserCode, code, cancellationToken);

        _logger.LogInformation("User {SubjectId} approved device sign-in for client {ClientId}", approval.SubjectId, code.ClientId);
        if (_events != null)
        {
            await _events.RaiseAsync(new ConsentGrantedEvent
            {
                TenantId = _tenantContext?.TenantId,
                SubjectId = approval.SubjectId,
                ClientId = code.ClientId,
                Scopes = data.Scopes.ToList(),
                RememberConsent = false
            }, cancellationToken);
        }

        return await DescribeAsync(code, data, DeviceVerificationStatus.Approved, cancellationToken);
    }

    public async Task<DeviceVerificationLookup> DenyAsync(string? userCode, string subjectId, CancellationToken cancellationToken = default)
    {
        var (code, data, status) = await LoadAsync(userCode, cancellationToken);
        if (code == null || data == null || status != DeviceVerificationStatus.Pending)
        {
            return code != null && data != null
                ? await DescribeAsync(code, data, status, cancellationToken)
                : DeviceVerificationLookup.Of(status);
        }

        data.Status = DeviceCodeStatus.Denied;
        data.AnsweredAt = DateTime.UtcNow;
        code.Data = JsonSerializer.Serialize(data);
        await _deviceFlowStore.UpdateByUserCodeAsync(code.UserCode, code, cancellationToken);

        _logger.LogInformation("User {SubjectId} denied device sign-in for client {ClientId}", subjectId, code.ClientId);
        if (_events != null)
        {
            await _events.RaiseAsync(new ConsentDeniedEvent
            {
                TenantId = _tenantContext?.TenantId,
                SubjectId = subjectId,
                ClientId = code.ClientId
            }, cancellationToken);
        }

        return await DescribeAsync(code, data, DeviceVerificationStatus.Denied, cancellationToken);
    }

    private async Task<(DeviceFlowCode? Code, DeviceCodeData? Data, DeviceVerificationStatus Status)> LoadAsync(
        string? userCode, CancellationToken cancellationToken)
    {
        var normalized = NormalizeUserCode(userCode);
        if (normalized.Length == 0)
        {
            return (null, null, DeviceVerificationStatus.Missing);
        }

        var code = await _deviceFlowStore.FindByUserCodeAsync(normalized, cancellationToken);
        var data = code != null ? DeviceCodeData.Read(code) : null;
        if (code == null || data == null)
        {
            return (null, null, DeviceVerificationStatus.NotFound);
        }

        if (code.Expiration < DateTime.UtcNow)
        {
            return (code, data, DeviceVerificationStatus.Expired);
        }

        return (code, data, data.Status switch
        {
            DeviceCodeStatus.Authorized => DeviceVerificationStatus.Approved,
            DeviceCodeStatus.Denied => DeviceVerificationStatus.Denied,
            _ => DeviceVerificationStatus.Pending,
        });
    }

    private async Task<DeviceVerificationLookup> DescribeAsync(
        DeviceFlowCode code, DeviceCodeData data, DeviceVerificationStatus status, CancellationToken cancellationToken)
    {
        var client = await _clientStore.FindClientByIdAsync(code.ClientId, cancellationToken);
        return new DeviceVerificationLookup
        {
            Status = status,
            UserCode = FormatUserCode(code.UserCode),
            ClientId = code.ClientId,
            ClientName = string.IsNullOrWhiteSpace(client?.ClientName) ? code.ClientId : client!.ClientName,
            LogoUri = HttpsOrNull(client?.LogoUri),
            ClientUri = HttpsOrNull(client?.ClientUri),
            PolicyUri = HttpsOrNull(Property(client, "policy_uri")),
            TosUri = HttpsOrNull(Property(client, "tos_uri")),
            Scopes = await DescribeScopesAsync(data.Scopes, cancellationToken),
            MatrixDeviceId = data.Scopes.Select(MatrixScopes.DeviceId).FirstOrDefault(id => id != null),
            ExpiresAt = code.Expiration,
        };
    }

    /// <summary>
    /// The requested scopes in plain words. Matrix scopes are described for what they do for the user; the device
    /// scope is shown as the device id instead. Other scopes take their display name and description from the
    /// tenant's resources (a prefix scope by its own name, never the suffix), else the scope name.
    /// </summary>
    private async Task<IReadOnlyList<DeviceScopeDescription>> DescribeScopesAsync(
        IEnumerable<string> scopes, CancellationToken cancellationToken)
    {
        var requested = scopes.Distinct(StringComparer.Ordinal).ToList();
        var identity = new Dictionary<string, IdentityResource>(StringComparer.Ordinal);
        IReadOnlyDictionary<string, ApiScope> api = new Dictionary<string, ApiScope>();
        if (_resourceStore != null)
        {
            foreach (var resource in await _resourceStore.FindIdentityResourcesByScopeNameAsync(requested, cancellationToken))
            {
                identity[resource.Name] = resource;
            }
            var defaultSuffixMaxLength = await ApiScopePrefixes.GetDefaultSuffixMaxLengthAsync(_tenantSettings, cancellationToken);
            api = await ApiScopePrefixes.ResolveAsync(_resourceStore, requested, cancellationToken, defaultSuffixMaxLength);
        }

        var described = new List<DeviceScopeDescription>();
        var matrixApiShown = false;
        foreach (var scope in requested)
        {
            if (scope == OidcConstants.Scopes.OpenId || MatrixScopes.DeviceId(scope) != null)
            {
                continue;
            }

            if (MatrixScopes.IsApiScope(scope))
            {
                if (!matrixApiShown)
                {
                    described.Add(new DeviceScopeDescription(scope,
                        "Sign in to your chat account on a new device",
                        "The device can read and send your messages and manage your chat account."));
                    matrixApiShown = true;
                }
                continue;
            }

            if (scope == OidcConstants.Scopes.OfflineAccess)
            {
                described.Add(new DeviceScopeDescription(scope, "Stay signed in on that device", null));
                continue;
            }

            if (identity.TryGetValue(scope, out var resource))
            {
                described.Add(new DeviceScopeDescription(scope, resource.DisplayName ?? resource.Name, resource.Description));
            }
            else if (api.TryGetValue(scope, out var apiScope))
            {
                described.Add(new DeviceScopeDescription(scope, apiScope.DisplayName ?? apiScope.Name, apiScope.Description));
            }
            else
            {
                described.Add(new DeviceScopeDescription(scope, scope, null));
            }
        }
        return described;
    }

    private static string? Property(Client? client, string key) =>
        client?.Properties?.FirstOrDefault(p => p.Key == key)?.Value;

    /// <summary>Only https links are shown or linked; anything else is left out.</summary>
    private static string? HttpsOrNull(string? uri) =>
        Uri.TryCreate(uri, UriKind.Absolute, out var parsed) && parsed.Scheme == Uri.UriSchemeHttps ? uri : null;
}
