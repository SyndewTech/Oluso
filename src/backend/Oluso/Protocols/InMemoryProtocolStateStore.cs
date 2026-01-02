using System.Security.Cryptography;
using System.Text.Json;
using Microsoft.Extensions.Caching.Distributed;
using Microsoft.Extensions.Logging;
using Oluso.Core.Protocols;

namespace Oluso.Protocols;

/// <summary>
/// Distributed cache implementation of protocol state store.
/// Supports Redis, SQL Server, or any IDistributedCache implementation.
/// </summary>
public class InMemoryProtocolStateStore : IProtocolStateStore
{
    private readonly IDistributedCache _cache;
    private readonly ILogger<InMemoryProtocolStateStore> _logger;
    private readonly TimeSpan _defaultExpiration = TimeSpan.FromMinutes(10);

    private const string CacheKeyPrefix = "oluso:protocol:state:";
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        WriteIndented = false
    };

    public InMemoryProtocolStateStore(
        IDistributedCache cache,
        ILogger<InMemoryProtocolStateStore> logger)
    {
        _cache = cache;
        _logger = logger;
    }

    public async Task<string> StoreAsync(
        ProtocolState state,
        TimeSpan? expiration = null,
        CancellationToken cancellationToken = default)
    {
        var correlationId = GenerateCorrelationId();
        var cacheKey = GetCacheKey(correlationId);
        var effectiveExpiration = expiration ?? _defaultExpiration;

        var json = JsonSerializer.Serialize(state, JsonOptions);
        var options = new DistributedCacheEntryOptions
        {
            AbsoluteExpirationRelativeToNow = effectiveExpiration
        };

        await _cache.SetStringAsync(cacheKey, json, options, cancellationToken);

        _logger.LogDebug(
            "Stored protocol state {CorrelationId} for {Protocol}, expires in {Expiration}",
            correlationId, state.ProtocolName, effectiveExpiration);

        return correlationId;
    }

    public async Task<ProtocolState?> GetAsync(
        string correlationId,
        CancellationToken cancellationToken = default)
    {
        var cacheKey = GetCacheKey(correlationId);
        var json = await _cache.GetStringAsync(cacheKey, cancellationToken);

        if (string.IsNullOrEmpty(json))
        {
            return null;
        }

        try
        {
            var state = JsonSerializer.Deserialize<ProtocolState>(json, JsonOptions);
            _logger.LogDebug("Retrieved protocol state {CorrelationId}", correlationId);
            return state;
        }
        catch (JsonException ex)
        {
            _logger.LogWarning(ex, "Failed to deserialize protocol state {CorrelationId}", correlationId);
            return null;
        }
    }

    public async Task RemoveAsync(
        string correlationId,
        CancellationToken cancellationToken = default)
    {
        var cacheKey = GetCacheKey(correlationId);
        await _cache.RemoveAsync(cacheKey, cancellationToken);
        _logger.LogDebug("Removed protocol state {CorrelationId}", correlationId);
    }

    private static string GetCacheKey(string correlationId) => $"{CacheKeyPrefix}{correlationId}";

    private static string GenerateCorrelationId()
    {
        var bytes = new byte[24];
        RandomNumberGenerator.Fill(bytes);
        return Convert.ToBase64String(bytes)
            .Replace('+', '-')
            .Replace('/', '_')
            .TrimEnd('=');
    }
}
