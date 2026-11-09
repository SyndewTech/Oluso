using System.Security.Cryptography;
using Microsoft.Extensions.Caching.Distributed;
using Oluso.Core.Protocols.DPoP;

namespace Oluso.Protocols.DPoP;

/// <summary>
/// Distributed cache implementation of DPoP nonce store.
/// Supports Redis, SQL Server, or any IDistributedCache implementation.
/// </summary>
public class InMemoryDPoPNonceStore : IDPoPNonceStore
{
    private readonly IDistributedCache _cache;
    private readonly IDPoPReplayCache _replay;
    private readonly TimeSpan _nonceLifetime = TimeSpan.FromMinutes(5);

    private const string NonceCachePrefix = "oluso:dpop:nonce:";

    public bool IsNonceRequired { get; set; } = false;

    public InMemoryDPoPNonceStore(IDistributedCache cache, IDPoPReplayCache? replay = null)
    {
        _cache = cache;
        _replay = replay ?? new LocalDPoPReplayCache(cache);
    }

    public async Task<string> GenerateNonceAsync(string? clientId, CancellationToken cancellationToken = default)
    {
        var nonceBytes = new byte[32];
        RandomNumberGenerator.Fill(nonceBytes);
        var nonce = Convert.ToBase64String(nonceBytes);

        // Store the nonce for validation
        var cacheKey = GetNonceCacheKey(nonce);
        var options = new DistributedCacheEntryOptions
        {
            AbsoluteExpirationRelativeToNow = _nonceLifetime
        };
        await _cache.SetStringAsync(cacheKey, clientId ?? string.Empty, options, cancellationToken);

        return nonce;
    }

    public async Task<bool> ValidateNonceAsync(string nonce, string? clientId, CancellationToken cancellationToken = default)
    {
        var cacheKey = GetNonceCacheKey(nonce);
        var storedClientId = await _cache.GetStringAsync(cacheKey, cancellationToken);

        if (storedClientId != null)
        {
            // If clientId was provided during generation, validate it matches
            if (!string.IsNullOrEmpty(storedClientId) && storedClientId != clientId)
            {
                return false;
            }

            // Nonce is valid - allow reuse within the lifetime window
            // DPoP nonces are not single-use; they're time-limited identifiers
            return true;
        }

        return false;
    }

    /// <summary>
    /// Records the proof's jti; false when it was seen before. Delegates to <see cref="IDPoPReplayCache"/>,
    /// whose check-and-record is atomic (a get-then-set on the distributed cache let two concurrent
    /// requests replaying one proof both pass).
    /// </summary>
    public Task<bool> ValidateJtiAsync(string jti, TimeSpan lifetime, CancellationToken cancellationToken = default) =>
        _replay.TryAddAsync(jti, lifetime, cancellationToken);

    private static string GetNonceCacheKey(string nonce) => $"{NonceCachePrefix}{nonce}";
}
