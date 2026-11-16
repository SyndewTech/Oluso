using System.Net.Http.Headers;
using System.Text.Json;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Oluso.Core.Protocols.Models;
using Oluso.Core.Security;

namespace Oluso.Protocols.Validation;

/// <summary>The public keys a client publishes at its <c>jwks_uri</c>.</summary>
public interface IClientJwksUriKeys
{
    /// <summary>
    /// The public JWKs (as JSON) at <paramref name="jwksUri"/>, from the cache when it is fresh. With
    /// <paramref name="refresh"/> the set is fetched again (a JWT named a key, or carried a signature, the cached set
    /// does not have), at most once a minute per URL.
    /// </summary>
    Task<IReadOnlyList<string>> GetAsync(string jwksUri, bool refresh, CancellationToken cancellationToken = default);
}

/// <summary>
/// Fetches and caches a client's published JWK Set. The URL is the client's choice, so every fetch goes through the
/// server-side request forgery guard (<see cref="PublicHttpTargets"/>: https only, public addresses only and pinned,
/// no redirects), within <see cref="MaxBytes"/> and <see cref="FetchTimeout"/>. Only public RSA and EC keys are taken
/// (<see cref="ClientPublicKeys"/>), at most <see cref="MaxKeys"/>. The set is cached for the response's
/// <c>Cache-Control</c> max-age, within <see cref="MinLifetime"/> and <see cref="MaxLifetime"/>, else
/// <see cref="DefaultLifetime"/>. When a fetch fails, the last good set is used until it expires, and nothing after.
/// </summary>
public sealed class ClientJwksUriKeys : IClientJwksUriKeys
{
    /// <summary>The named HttpClient the fetches go through (configured with <see cref="PublicHttpTargets.CreateHandler"/>).</summary>
    public const string HttpClientName = "Oluso.ClientJwks";

    /// <summary>
    /// Admits jwks_uri targets on private, loopback and link-local addresses. Off by default; only for a local test
    /// setup, never for a deployment.
    /// </summary>
    public const string AllowPrivateNetworkSetting = "Oluso:ClientJwks:AllowPrivateNetwork";

    public const int MaxBytes = 64 * 1024;
    public const int MaxKeys = 20;
    public static readonly TimeSpan FetchTimeout = TimeSpan.FromSeconds(10);
    public static readonly TimeSpan DefaultLifetime = TimeSpan.FromHours(1);
    public static readonly TimeSpan MinLifetime = TimeSpan.FromMinutes(5);
    public static readonly TimeSpan MaxLifetime = TimeSpan.FromHours(24);

    /// <summary>The least time between two fetches of one URL, so a stream of unknown kids cannot hammer it.</summary>
    public static readonly TimeSpan RefreshInterval = TimeSpan.FromMinutes(1);

    private readonly IMemoryCache _cache;
    private readonly IHttpClientFactory _httpClientFactory;
    private readonly IConfiguration _configuration;
    private readonly TimeProvider _clock;
    private readonly ClientJwksUriOptions _options;
    private readonly ILogger<ClientJwksUriKeys> _logger;

    public ClientJwksUriKeys(
        IMemoryCache cache,
        IHttpClientFactory httpClientFactory,
        IConfiguration configuration,
        ILogger<ClientJwksUriKeys> logger,
        TimeProvider? clock = null,
        IOptions<ClientJwksUriOptions>? options = null)
    {
        _cache = cache;
        _httpClientFactory = httpClientFactory;
        _configuration = configuration;
        _logger = logger;
        _clock = clock ?? TimeProvider.System;
        _options = options?.Value ?? new ClientJwksUriOptions();
    }

    private sealed record Entry(IReadOnlyList<string> Keys, DateTimeOffset FetchedAt, DateTimeOffset ExpiresAt);

    public async Task<IReadOnlyList<string>> GetAsync(string jwksUri, bool refresh, CancellationToken cancellationToken = default)
    {
        var cacheKey = $"oluso:client-jwks:{jwksUri}";
        var now = _clock.GetUtcNow();
        _cache.TryGetValue(cacheKey, out Entry? entry);

        var fresh = entry != null && now < entry.ExpiresAt;
        var recentlyFetched = entry != null && now - entry.FetchedAt < RefreshInterval;
        if (fresh && (!refresh || recentlyFetched))
        {
            return entry!.Keys;
        }
        if (!fresh && recentlyFetched)
        {
            // The last attempt failed under a minute ago: not again yet.
            return Array.Empty<string>();
        }

        var (keys, lifetime, error) = await FetchAsync(jwksUri, cancellationToken);
        if (error != null)
        {
            _logger.LogWarning("Client jwks_uri {JwksUri} could not be used: {Reason}", jwksUri, error);
            if (fresh)
            {
                // Keep the last good set until it expires; remember the attempt so it is not retried at once.
                var kept = entry! with { FetchedAt = now };
                _cache.Set(cacheKey, kept, kept.ExpiresAt);
                return kept.Keys;
            }

            _cache.Set(cacheKey, new Entry(Array.Empty<string>(), now, now), now + RefreshInterval);
            return Array.Empty<string>();
        }

        var stored = new Entry(keys!, now, now + lifetime);
        _cache.Set(cacheKey, stored, stored.ExpiresAt);
        return stored.Keys;
    }

    private async Task<(IReadOnlyList<string>? Keys, TimeSpan Lifetime, string? Error)> FetchAsync(string jwksUri, CancellationToken cancellationToken)
    {
        if (!Uri.TryCreate(jwksUri, UriKind.Absolute, out var target))
        {
            return (null, default, "not an absolute URL");
        }
        var allowPrivateNetwork = _configuration.GetValue<bool>(AllowPrivateNetworkSetting)
            || (target.Scheme == Uri.UriSchemeHttps && _options.TrustedHosts.Contains(target.Host));
        if (PublicHttpTargets.ValidateTarget(target, allowPrivateNetwork) is { } rejection)
        {
            return (null, default, rejection);
        }

        var address = await PublicHttpTargets.ResolvePublicAddressAsync(target, cancellationToken, allowPrivateNetwork);
        if (address.Error != null)
        {
            return (null, default, address.Error);
        }

        string body;
        TimeSpan? maxAge;
        try
        {
            var http = _httpClientFactory.CreateClient(HttpClientName);
            http.Timeout = FetchTimeout;
            using var message = new HttpRequestMessage(HttpMethod.Get, target);
            PublicHttpTargets.Pin(message, address.Address!, allowPrivateNetwork);
            message.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("application/jwk-set+json"));
            message.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));

            using var response = await http.SendAsync(message, HttpCompletionOption.ResponseHeadersRead, cancellationToken);
            if (!response.IsSuccessStatusCode)
            {
                // Redirects included: they are not followed.
                return (null, default, $"HTTP {(int)response.StatusCode}");
            }
            if (response.Content.Headers.ContentLength > MaxBytes)
            {
                return (null, default, "the key set is too large");
            }

            await using var stream = await response.Content.ReadAsStreamAsync(cancellationToken);
            var buffer = new byte[MaxBytes + 1];
            var read = 0;
            int chunk;
            while (read < buffer.Length &&
                   (chunk = await stream.ReadAsync(buffer.AsMemory(read, buffer.Length - read), cancellationToken)) > 0)
            {
                read += chunk;
            }
            if (read > MaxBytes)
            {
                return (null, default, "the key set is too large");
            }

            body = System.Text.Encoding.UTF8.GetString(buffer, 0, read);
            maxAge = response.Headers.CacheControl?.NoStore == true || response.Headers.CacheControl?.NoCache == true
                ? MinLifetime
                : response.Headers.CacheControl?.MaxAge;
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException && !cancellationToken.IsCancellationRequested)
        {
            return (null, default, ex.Message);
        }

        var keys = ReadKeySet(body);
        if (keys == null)
        {
            return (null, default, "the response is not a JWK Set");
        }

        var lifetime = maxAge is { } age
            ? TimeSpan.FromTicks(Math.Clamp(age.Ticks, MinLifetime.Ticks, MaxLifetime.Ticks))
            : DefaultLifetime;
        return (keys, lifetime, null);
    }

    /// <summary>The public RSA and EC keys of a JWK Set (others are skipped), or null when it is not one.</summary>
    internal static IReadOnlyList<string>? ReadKeySet(string json)
    {
        try
        {
            using var document = JsonDocument.Parse(json);
            if (document.RootElement.ValueKind != JsonValueKind.Object ||
                !document.RootElement.TryGetProperty("keys", out var keyArray) ||
                keyArray.ValueKind != JsonValueKind.Array)
            {
                return null;
            }

            return keyArray.EnumerateArray()
                .Where(key => ClientPublicKeys.PublicJwkError(key) == null)
                .Take(MaxKeys)
                .Select(key => key.GetRawText())
                .ToList();
        }
        catch (JsonException)
        {
            return null;
        }
    }
}
