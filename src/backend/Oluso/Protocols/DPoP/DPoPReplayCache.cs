using System.Collections.Concurrent;
using Microsoft.Extensions.Caching.Distributed;
using StackExchange.Redis;

namespace Oluso.Protocols.DPoP;

/// <summary>
/// Remembers the DPoP proof identifiers (<c>jti</c>) already seen so a proof cannot be replayed
/// (RFC 9449 §11.1). <see cref="TryAddAsync"/> must be atomic: of two concurrent calls with the same
/// key exactly one returns true.
/// </summary>
public interface IDPoPReplayCache
{
    /// <summary>Records <paramref name="key"/> for <paramref name="lifetime"/>; false when it was already recorded.</summary>
    Task<bool> TryAddAsync(string key, TimeSpan lifetime, CancellationToken cancellationToken = default);
}

/// <summary>
/// The default replay cache. The check-and-record is atomic within this process (a concurrent
/// dictionary); the <see cref="IDistributedCache"/> is consulted as well so another instance's proofs
/// are refused too, but a get-then-set on a distributed cache cannot be atomic across instances —
/// configure Redis (<see cref="RedisDPoPReplayCache"/>) when running several instances.
/// </summary>
public sealed class LocalDPoPReplayCache : IDPoPReplayCache
{
    private const string Prefix = "oluso:dpop:jti:";
    private readonly ConcurrentDictionary<string, DateTime> _seen = new(StringComparer.Ordinal);
    private readonly IDistributedCache? _distributed;
    private readonly TimeProvider _clock;
    private DateTime _nextSweep;

    public LocalDPoPReplayCache(IDistributedCache? distributed = null, TimeProvider? clock = null)
    {
        _distributed = distributed;
        _clock = clock ?? TimeProvider.System;
        _nextSweep = _clock.GetUtcNow().UtcDateTime;
    }

    public async Task<bool> TryAddAsync(string key, TimeSpan lifetime, CancellationToken cancellationToken = default)
    {
        var now = _clock.GetUtcNow().UtcDateTime;
        Sweep(now);

        var expires = now + lifetime;
        // Each factory sets the flag, so it reflects the attempt that won (AddOrUpdate retries under contention).
        var added = false;
        _seen.AddOrUpdate(key,
            _ => { added = true; return expires; },
            (_, existing) =>
            {
                added = existing <= now;
                return added ? expires : existing;
            });
        if (!added) return false;

        if (_distributed != null)
        {
            var cacheKey = Prefix + key;
            if (await _distributed.GetAsync(cacheKey, cancellationToken) != null)
                return false;
            await _distributed.SetAsync(cacheKey, new byte[] { 1 },
                new DistributedCacheEntryOptions { AbsoluteExpirationRelativeToNow = lifetime }, cancellationToken);
        }
        return true;
    }

    private void Sweep(DateTime now)
    {
        if (now < _nextSweep) return;
        _nextSweep = now.AddMinutes(1);
        foreach (var entry in _seen)
        {
            if (entry.Value <= now)
                _seen.TryRemove(entry);
        }
    }
}

/// <summary>Replay cache shared by every instance through Redis (<c>SET key NX PX</c>, atomic).</summary>
public sealed class RedisDPoPReplayCache : IDPoPReplayCache
{
    private readonly Lazy<Task<ConnectionMultiplexer>> _redis;

    public RedisDPoPReplayCache(string connectionString) =>
        _redis = new Lazy<Task<ConnectionMultiplexer>>(() => ConnectionMultiplexer.ConnectAsync(connectionString));

    public async Task<bool> TryAddAsync(string key, TimeSpan lifetime, CancellationToken cancellationToken = default)
    {
        var db = (await _redis.Value).GetDatabase();
        return await db.StringSetAsync((RedisKey)("oluso:dpop:jti:" + key), (RedisValue)1, lifetime, When.NotExists);
    }
}
