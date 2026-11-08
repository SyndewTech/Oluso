using System.Diagnostics;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using Microsoft.Extensions.Logging;
using Oluso.Core.UserJourneys;
using Oluso.Enterprise.Scim.Entities;

namespace Oluso.Enterprise.Scim.Outbound;

/// <summary>
/// We could not produce a credential for the provider: no secret stored, or
/// the OAuth token endpoint refused us. Retryable, since an administrator
/// may fix the configuration.
/// </summary>
public sealed class ScimProviderAuthenticationException : Exception
{
    public ScimProviderAuthenticationException(string message) : base(message)
    {
    }
}

public sealed class ScimRemoteResponse
{
    public required string Method { get; init; }
    public required string Path { get; init; }

    /// <summary>HTTP status, or 0 when the request never got a response.</summary>
    public int StatusCode { get; init; }

    public JsonNode? Body { get; init; }

    /// <summary>Response text, truncated for logging.</summary>
    public string? RawBody { get; init; }

    /// <summary>SCIM <c>detail</c>, reason phrase, or transport error.</summary>
    public string? ErrorDetail { get; init; }

    public string? ScimType { get; init; }

    public long DurationMs { get; init; }

    public bool IsSuccess => StatusCode is >= 200 and < 300;

    public bool IsTransportFailure => StatusCode == 0;

    public string? RemoteId => (Body as JsonObject)?["id"]?.ToString();

    public string? RemoteVersion => ((Body as JsonObject)?["meta"] as JsonObject)?["version"]?.ToString();

    public string Describe() => ErrorDetail == null ? $"{Method} {Path} -> {StatusCode}" : $"{Method} {Path} -> {StatusCode}: {ErrorDetail}";
}

public interface IScimProviderHttpClient
{
    /// <summary>
    /// Sends one request to the provider. Never throws for HTTP or transport
    /// failures; those come back as a response with the error captured.
    /// Throws <see cref="ScimProviderAuthenticationException"/> when a
    /// credential cannot be produced.
    /// </summary>
    Task<ScimRemoteResponse> SendAsync(
        ScimProvider provider,
        HttpMethod method,
        string relativePath,
        JsonNode? body = null,
        bool anonymous = false,
        CancellationToken cancellationToken = default);
}

public sealed class ScimProviderHttpClient : IScimProviderHttpClient
{
    public const string ClientName = "ScimProvider";
    private const int MaxLoggedBody = 4000;

    private static readonly MediaTypeWithQualityHeaderValue ScimAccept = new(ScimConstants.ContentTypes.Scim);
    private static readonly MediaTypeWithQualityHeaderValue JsonAccept = new(ScimConstants.ContentTypes.Json, 0.9);

    private readonly IHttpClientFactory _httpClientFactory;
    private readonly IScimProviderSecretStore _secrets;
    private readonly IClientCredentialsTokenCache _tokenCache;
    private readonly ScimOptions _options;
    private readonly ILogger<ScimProviderHttpClient> _logger;

    public ScimProviderHttpClient(
        IHttpClientFactory httpClientFactory,
        IScimProviderSecretStore secrets,
        IClientCredentialsTokenCache tokenCache,
        ScimOptions options,
        ILogger<ScimProviderHttpClient> logger)
    {
        _httpClientFactory = httpClientFactory;
        _secrets = secrets;
        _tokenCache = tokenCache;
        _options = options;
        _logger = logger;
    }

    public async Task<ScimRemoteResponse> SendAsync(
        ScimProvider provider,
        HttpMethod method,
        string relativePath,
        JsonNode? body = null,
        bool anonymous = false,
        CancellationToken cancellationToken = default)
    {
        var url = BuildUrl(provider.BaseUrl, relativePath);
        using var request = new HttpRequestMessage(method, url);
        request.Headers.Accept.Add(ScimAccept);
        request.Headers.Accept.Add(JsonAccept);

        if (!anonymous)
        {
            var token = await ResolveBearerTokenAsync(provider, cancellationToken);
            request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
        }

        if (body != null)
        {
            request.Content = new StringContent(body.ToJsonString(), Encoding.UTF8, ScimConstants.ContentTypes.Scim);
        }

        var client = _httpClientFactory.CreateClient(ClientName);
        client.Timeout = _options.Outbound.RequestTimeout;

        var stopwatch = Stopwatch.StartNew();
        HttpResponseMessage response;
        try
        {
            response = await client.SendAsync(request, HttpCompletionOption.ResponseContentRead, cancellationToken);
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            stopwatch.Stop();
            return new ScimRemoteResponse
            {
                Method = method.Method,
                Path = relativePath,
                StatusCode = 0,
                ErrorDetail = $"Request timed out after {_options.Outbound.RequestTimeout.TotalSeconds:0}s",
                DurationMs = stopwatch.ElapsedMilliseconds,
            };
        }
        catch (HttpRequestException ex)
        {
            stopwatch.Stop();
            _logger.LogWarning(ex, "SCIM provider {ProviderId} unreachable at {Url}", provider.Id, url);
            return new ScimRemoteResponse
            {
                Method = method.Method,
                Path = relativePath,
                StatusCode = 0,
                ErrorDetail = ex.Message,
                DurationMs = stopwatch.ElapsedMilliseconds,
            };
        }

        using (response)
        {
            var text = response.Content == null ? null : await response.Content.ReadAsStringAsync(cancellationToken);
            stopwatch.Stop();

            JsonNode? parsed = null;
            if (!string.IsNullOrWhiteSpace(text))
            {
                try
                {
                    parsed = JsonNode.Parse(text);
                }
                catch (JsonException)
                {
                    parsed = null;
                }
            }

            string? detail = null;
            string? scimType = null;
            if (!response.IsSuccessStatusCode)
            {
                if (parsed is JsonObject error)
                {
                    detail = error["detail"]?.ToString();
                    scimType = error["scimType"]?.ToString();
                }

                detail ??= string.IsNullOrWhiteSpace(response.ReasonPhrase)
                    ? $"HTTP {(int)response.StatusCode}"
                    : response.ReasonPhrase;
            }

            return new ScimRemoteResponse
            {
                Method = method.Method,
                Path = relativePath,
                StatusCode = (int)response.StatusCode,
                Body = parsed,
                RawBody = text == null ? null : (text.Length > MaxLoggedBody ? text[..MaxLoggedBody] : text),
                ErrorDetail = detail,
                ScimType = scimType,
                DurationMs = stopwatch.ElapsedMilliseconds,
            };
        }
    }

    private async Task<string> ResolveBearerTokenAsync(ScimProvider provider, CancellationToken cancellationToken)
    {
        var tenantId = provider.TenantId
            ?? throw new ScimProviderAuthenticationException($"Provider {provider.Id} has no tenant");

        var secret = await _secrets.GetSecretAsync(tenantId, provider.Id, cancellationToken)
            ?? throw new ScimProviderAuthenticationException($"No secret is configured for provider '{provider.Name}'");

        if (string.Equals(provider.AuthType, ScimProviderAuthTypes.OAuthClientCredentials, StringComparison.OrdinalIgnoreCase))
        {
            if (string.IsNullOrWhiteSpace(provider.OAuthTokenEndpoint) || string.IsNullOrWhiteSpace(provider.OAuthClientId))
                throw new ScimProviderAuthenticationException($"Provider '{provider.Name}' uses client credentials but has no token endpoint or client id");

            var token = await _tokenCache.GetAccessTokenAsync(
                provider.OAuthTokenEndpoint, provider.OAuthClientId, secret, provider.OAuthScope,
                audience: null, resource: null, cancellationToken);

            return token
                ?? throw new ScimProviderAuthenticationException($"Token endpoint {provider.OAuthTokenEndpoint} did not issue an access token for client '{provider.OAuthClientId}'");
        }

        return secret;
    }

    private static string BuildUrl(string baseUrl, string relativePath)
    {
        var trimmedBase = baseUrl.TrimEnd('/');
        var trimmedPath = relativePath.TrimStart('/');
        return $"{trimmedBase}/{trimmedPath}";
    }
}
