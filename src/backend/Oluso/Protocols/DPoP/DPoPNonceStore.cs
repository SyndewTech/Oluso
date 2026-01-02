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
    private readonly TimeSpan _nonceLifetime = TimeSpan.FromMinutes(5);

    private const string NonceCachePrefix = "oluso:dpop:nonce:";
    private const string JtiCachePrefix = "oluso:dpop:jti:";

    public bool IsNonceRequired { get; set; } = false;

    public InMemoryDPoPNonceStore(IDistributedCache cache)
    {
        _cache = cache;
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

    public async Task<bool> ValidateJtiAsync(string jti, TimeSpan lifetime, CancellationToken cancellationToken = default)
    {
        var cacheKey = GetJtiCacheKey(jti);

        // Check if JTI was already used
        var existing = await _cache.GetAsync(cacheKey, cancellationToken);
        if (existing != null)
        {
            return false;
        }

        // Mark JTI as used
        var options = new DistributedCacheEntryOptions
        {
            AbsoluteExpirationRelativeToNow = lifetime
        };
        await _cache.SetAsync(cacheKey, new byte[] { 1 }, options, cancellationToken);
        return true;
    }

    private static string GetNonceCacheKey(string nonce) => $"{NonceCachePrefix}{nonce}";
    private static string GetJtiCacheKey(string jti) => $"{JtiCachePrefix}{jti}";
}
