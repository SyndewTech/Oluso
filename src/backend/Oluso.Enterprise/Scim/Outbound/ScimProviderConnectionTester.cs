using System.Text.Json.Nodes;
using Oluso.Enterprise.Scim.Entities;
using Oluso.Enterprise.Scim.Stores;

namespace Oluso.Enterprise.Scim.Outbound;

public sealed class ScimProviderTestResult
{
    public bool Success { get; init; }
    public string Message { get; init; } = string.Empty;

    public bool DiscoveryReachable { get; init; }
    public int DiscoveryStatusCode { get; init; }

    public bool AuthenticationOk { get; init; }
    public int AuthenticationStatusCode { get; init; }
    public string? AuthenticationError { get; init; }

    public bool? PatchSupported { get; init; }
    public bool? BulkSupported { get; init; }
    public bool? FilterSupported { get; init; }
    public int? FilterMaxResults { get; init; }
    public bool? EtagSupported { get; init; }
    public IReadOnlyList<string> AuthenticationSchemes { get; init; } = Array.Empty<string>();
}

/// <summary>
/// Two probes an administrator can run from the provider page: anonymous
/// discovery (is the URL a SCIM endpoint?) and an authenticated one-row
/// list (does the credential work?). Both are logged like any other call.
/// </summary>
public interface IScimProviderConnectionTester
{
    Task<ScimProviderTestResult> TestAsync(ScimProvider provider, CancellationToken cancellationToken = default);
}

public sealed class ScimProviderConnectionTester : IScimProviderConnectionTester
{
    private readonly IScimProviderHttpClient _http;
    private readonly IScimProviderLogStore _logs;

    public ScimProviderConnectionTester(IScimProviderHttpClient http, IScimProviderLogStore logs)
    {
        _http = http;
        _logs = logs;
    }

    public async Task<ScimProviderTestResult> TestAsync(ScimProvider provider, CancellationToken cancellationToken = default)
    {
        var discovery = await _http.SendAsync(provider, HttpMethod.Get, "ServiceProviderConfig", anonymous: true, cancellationToken: cancellationToken);
        await LogAsync(provider, "Discover", discovery, cancellationToken);

        var config = discovery.Body as JsonObject;
        bool? Supported(string key) => (config?[key] as JsonObject)?["supported"]?.GetValue<bool>();

        var schemes = (config?["authenticationSchemes"] as JsonArray)?
            .OfType<JsonObject>()
            .Select(s => s["type"]?.ToString() ?? s["name"]?.ToString())
            .Where(s => !string.IsNullOrEmpty(s))
            .Cast<string>()
            .ToList() ?? new List<string>();

        ScimRemoteResponse? auth = null;
        string? authError = null;
        try
        {
            auth = await _http.SendAsync(provider, HttpMethod.Get, "Users?count=1", cancellationToken: cancellationToken);
            await LogAsync(provider, "Test", auth, cancellationToken);
        }
        catch (ScimProviderAuthenticationException ex)
        {
            authError = ex.Message;
        }

        var authOk = auth?.IsSuccess == true;
        var message = BuildMessage(provider, discovery, auth, authError);

        return new ScimProviderTestResult
        {
            Success = authOk,
            Message = message,
            DiscoveryReachable = discovery.IsSuccess,
            DiscoveryStatusCode = discovery.StatusCode,
            AuthenticationOk = authOk,
            AuthenticationStatusCode = auth?.StatusCode ?? 0,
            AuthenticationError = authError ?? (auth is { IsSuccess: false } ? auth.ErrorDetail : null),
            PatchSupported = Supported("patch"),
            BulkSupported = Supported("bulk"),
            FilterSupported = Supported("filter"),
            FilterMaxResults = (config?["filter"] as JsonObject)?["maxResults"]?.GetValue<int>(),
            EtagSupported = Supported("etag"),
            AuthenticationSchemes = schemes,
        };
    }

    private static string BuildMessage(ScimProvider provider, ScimRemoteResponse discovery, ScimRemoteResponse? auth, string? authError)
    {
        if (auth?.IsSuccess == true)
        {
            return discovery.IsSuccess
                ? "Connected. Discovery and authenticated access both succeeded."
                : $"Authenticated access succeeded, but discovery returned {discovery.StatusCode}. The endpoint works; it does not publish ServiceProviderConfig.";
        }

        if (authError != null)
            return authError;

        if (discovery.IsTransportFailure)
            return $"Could not reach {provider.BaseUrl}: {discovery.ErrorDetail}";

        if (discovery.StatusCode == 403 && string.Equals(provider.Flavor, ScimProviderFlavors.Stalwart, StringComparison.OrdinalIgnoreCase))
            return "Stalwart returned 403 on /scim/v2. SCIM is an Enterprise Edition feature; the Community Edition rejects every SCIM request.";

        if (auth == null)
            return $"Discovery returned {discovery.StatusCode}; authentication was not attempted.";

        return auth.StatusCode switch
        {
            401 => "The provider rejected the credential (401). Check the secret, and for OAuth the token endpoint and client id.",
            403 => "The credential is valid but lacks permission to list users (403). On Stalwart the service principal needs scimAccess and sysAccountGet.",
            404 => $"{provider.BaseUrl} does not serve /Users (404). Check that the base URL ends with the SCIM root, usually /scim/v2.",
            0 => $"Could not reach {provider.BaseUrl}: {auth.ErrorDetail}",
            _ => $"Authenticated request failed: {auth.Describe()}",
        };
    }

    private async Task LogAsync(ScimProvider provider, string operation, ScimRemoteResponse response, CancellationToken cancellationToken)
    {
        await _logs.CreateAsync(new ScimProviderLog
        {
            TenantId = provider.TenantId,
            ScimProviderId = provider.Id,
            Operation = operation,
            Method = response.Method,
            Path = response.Path,
            StatusCode = response.StatusCode,
            Success = response.IsSuccess,
            ErrorMessage = response.IsSuccess ? null : response.ErrorDetail,
            DurationMs = response.DurationMs,
        }, cancellationToken);
    }
}
