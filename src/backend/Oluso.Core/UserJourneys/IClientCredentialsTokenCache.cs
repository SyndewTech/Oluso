using System.Collections.Concurrent;
using System.Net.Http.Json;
using System.Text.Json.Serialization;
using Microsoft.Extensions.Logging;

namespace Oluso.Core.UserJourneys;

/// <summary>
/// Caches OAuth 2.0 client_credentials access tokens for outbound
/// integrations. Use case: a journey's <c>api_call</c> step that hits an
/// external API protected by client_credentials needs a fresh access
/// token, but minting one per step execution is expensive and
/// rate-limited by the issuer.
/// </summary>
/// <remarks>
/// Tokens are keyed by (tokenEndpoint, clientId, scope, audience, resource) so the
/// same cache safely services multiple integrations. Expiry is tracked
/// against the issuer's <c>expires_in</c> with a 60-second safety margin
/// — a request hitting the cache within that margin triggers a refresh.
/// Per-key locking serialises concurrent refreshes for the same key
/// without blocking unrelated keys.
/// </remarks>
public interface IClientCredentialsTokenCache
{
    /// <summary>
    /// Returns a fresh-or-cached access token, fetching one via
    /// client_credentials when the cache is empty or stale.
    /// </summary>
    /// <returns>The access token, or null when the issuer call failed.</returns>
    Task<string?> GetAccessTokenAsync(
        string tokenEndpoint,
        string clientId,
        string clientSecret,
        string? scope,
        string? audience,
        string? resource,
        CancellationToken cancellationToken = default);
}

/// <summary>
/// Default in-process cache. Registered as a singleton so cached tokens
/// are shared across requests within the same host. Multi-instance
/// deployments will mint a token per instance — acceptable for the
/// expected volume; revisit with a distributed cache if step rate ever
/// dwarfs token-endpoint capacity.
/// </summary>
public sealed class ClientCredentialsTokenCache : IClientCredentialsTokenCache
{
    private static readonly TimeSpan ExpiryMargin = TimeSpan.FromSeconds(60);

    private readonly IHttpClientFactory _httpClientFactory;
    private readonly ILogger<ClientCredentialsTokenCache> _logger;

    private readonly ConcurrentDictionary<string, CachedToken> _cache = new();
    private readonly ConcurrentDictionary<string, SemaphoreSlim> _locks = new();

    public ClientCredentialsTokenCache(
        IHttpClientFactory httpClientFactory,
        ILogger<ClientCredentialsTokenCache> logger)
    {
        _httpClientFactory = httpClientFactory;
        _logger = logger;
    }

    public async Task<string?> GetAccessTokenAsync(
        string tokenEndpoint,
        string clientId,
        string clientSecret,
        string? scope,
        string? audience,
        string? resource,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(tokenEndpoint) || string.IsNullOrWhiteSpace(clientId))
        {
            _logger.LogWarning("client_credentials auth requested but tokenEndpoint or clientId is empty");
            return null;
        }

        var key = BuildKey(tokenEndpoint, clientId, scope, audience, resource);

        if (_cache.TryGetValue(key, out var cached) && cached.IsFresh())
        {
            return cached.AccessToken;
        }

        // Per-key lock so two callers asking for the same token only
        // mint it once. Other keys progress in parallel.
        var gate = _locks.GetOrAdd(key, _ => new SemaphoreSlim(1, 1));
        await gate.WaitAsync(cancellationToken);
        try
        {
            // Re-check after the lock — another caller may have refreshed
            // while we waited.
            if (_cache.TryGetValue(key, out cached) && cached.IsFresh())
            {
                return cached.AccessToken;
            }

            var fetched = await FetchAsync(tokenEndpoint, clientId, clientSecret, scope, audience, resource, cancellationToken);
            if (fetched == null) return null;

            _cache[key] = fetched;
            return fetched.AccessToken;
        }
        finally
        {
            gate.Release();
        }
    }

    private async Task<CachedToken?> FetchAsync(
        string tokenEndpoint,
        string clientId,
        string clientSecret,
        string? scope,
        string? audience,
        string? resource,
        CancellationToken cancellationToken)
    {
        // Reuse the JourneyApiCall named client so token fetches share
        // the User-Agent (and any future handlers — TLS pinning, retry,
        // tracing) registered for outbound journey-driven HTTP. Keeps
        // one outbound client surface to maintain.
        var client = _httpClientFactory.CreateClient("JourneyApiCall");

        var form = new List<KeyValuePair<string, string>>
        {
            new("grant_type", "client_credentials"),
            new("client_id", clientId),
            new("client_secret", clientSecret),
        };
        if (!string.IsNullOrWhiteSpace(scope)) form.Add(new("scope", scope));
        // `audience` is what some identity providers read; RFC 8707 `resource` is the standard
        // (and what Oluso reads), one parameter per resource.
        if (!string.IsNullOrWhiteSpace(audience)) form.Add(new("audience", audience));
        foreach (var r in (resource ?? string.Empty).Split(' ', StringSplitOptions.RemoveEmptyEntries))
        {
            form.Add(new("resource", r));
        }

        try
        {
            using var content = new FormUrlEncodedContent(form);
            using var response = await client.PostAsync(tokenEndpoint, content, cancellationToken);

            if (!response.IsSuccessStatusCode)
            {
                var body = await SafeReadBodyAsync(response, cancellationToken);
                _logger.LogWarning(
                    "client_credentials token fetch returned {Status} for endpoint {Endpoint}: {Body}",
                    (int)response.StatusCode, tokenEndpoint, body);
                return null;
            }

            var payload = await response.Content.ReadFromJsonAsync<TokenResponse>(cancellationToken);
            if (payload == null || string.IsNullOrEmpty(payload.AccessToken))
            {
                _logger.LogWarning("client_credentials token endpoint {Endpoint} returned an empty access_token", tokenEndpoint);
                return null;
            }

            // expires_in is seconds. Default to 1h when the issuer omits
            // it — any nonzero positive value is honoured.
            var ttl = payload.ExpiresIn > 0 ? TimeSpan.FromSeconds(payload.ExpiresIn) : TimeSpan.FromHours(1);
            return new CachedToken(payload.AccessToken, DateTime.UtcNow + ttl);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "client_credentials token fetch threw for endpoint {Endpoint}", tokenEndpoint);
            return null;
        }
    }

    private static string BuildKey(string tokenEndpoint, string clientId, string? scope, string? audience, string? resource)
        => $"{tokenEndpoint}|{clientId}|{scope ?? string.Empty}|{audience ?? string.Empty}|{resource ?? string.Empty}";

    private static async Task<string> SafeReadBodyAsync(HttpResponseMessage response, CancellationToken ct)
    {
        try { return await response.Content.ReadAsStringAsync(ct); }
        catch { return string.Empty; }
    }

    private sealed record CachedToken(string AccessToken, DateTime ExpiresAt)
    {
        public bool IsFresh() => ExpiresAt - ExpiryMargin > DateTime.UtcNow;
    }

    private sealed record TokenResponse
    {
        [JsonPropertyName("access_token")]
        public string AccessToken { get; init; } = string.Empty;

        [JsonPropertyName("expires_in")]
        public int ExpiresIn { get; init; }

        [JsonPropertyName("token_type")]
        public string? TokenType { get; init; }
    }
}
