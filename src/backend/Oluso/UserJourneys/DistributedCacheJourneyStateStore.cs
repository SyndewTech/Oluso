using System.Text.Json;
using Microsoft.Extensions.Caching.Distributed;
using Microsoft.Extensions.Logging;
using Oluso.Core.UserJourneys;

namespace Oluso.UserJourneys;

/// <summary>
/// Journey state store using IDistributedCache (Redis, SQL Server, etc.).
/// Configure the underlying cache with AddStackExchangeRedisCache, AddSqlServerCache, etc.
/// </summary>
public class DistributedCacheJourneyStateStore : IJourneyStateStore
{
    private readonly IDistributedCache _cache;
    private readonly ILogger<DistributedCacheJourneyStateStore> _logger;
    private readonly DistributedCacheJourneyStateStoreOptions _options;

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        WriteIndented = false
    };

    public DistributedCacheJourneyStateStore(
        IDistributedCache cache,
        ILogger<DistributedCacheJourneyStateStore> logger,
        DistributedCacheJourneyStateStoreOptions? options = null)
    {
        _cache = cache;
        _logger = logger;
        _options = options ?? new DistributedCacheJourneyStateStoreOptions();
    }

    public async Task<JourneyState?> GetAsync(string journeyId, CancellationToken cancellationToken = default)
    {
        var key = GetKey(journeyId);
        var data = await _cache.GetStringAsync(key, cancellationToken);

        if (string.IsNullOrEmpty(data))
        {
            return null;
        }

        try
        {
            return JsonSerializer.Deserialize<JourneyState>(data, JsonOptions);
        }
        catch (JsonException ex)
        {
            _logger.LogWarning(ex, "Failed to deserialize journey state for {JourneyId}", journeyId);
            return null;
        }
    }

    public async Task SaveAsync(JourneyState state, CancellationToken cancellationToken = default)
    {
        var key = GetKey(state.JourneyId);
        var data = JsonSerializer.Serialize(state, JsonOptions);

        var options = new DistributedCacheEntryOptions();

        // Use the journey's expiration time if set, otherwise use default
        if (state.ExpiresAt.HasValue)
        {
            options.AbsoluteExpiration = state.ExpiresAt.Value;
        }
        else
        {
            options.AbsoluteExpirationRelativeToNow = _options.DefaultExpiration;
        }

        await _cache.SetStringAsync(key, data, options, cancellationToken);

        // Also maintain a user index if user is set
        if (!string.IsNullOrEmpty(state.UserId))
        {
            await AddToUserIndexAsync(state.UserId, state.JourneyId, cancellationToken);
        }
    }

    public async Task DeleteAsync(string journeyId, CancellationToken cancellationToken = default)
    {
        var key = GetKey(journeyId);

        // Get state first to remove from user index
        var state = await GetAsync(journeyId, cancellationToken);
        if (state?.UserId != null)
        {
            await RemoveFromUserIndexAsync(state.UserId, journeyId, cancellationToken);
        }

        await _cache.RemoveAsync(key, cancellationToken);
    }

    public async Task<IEnumerable<JourneyState>> GetByUserAsync(string userId, CancellationToken cancellationToken = default)
    {
        var indexKey = GetUserIndexKey(userId);
        var indexData = await _cache.GetStringAsync(indexKey, cancellationToken);

        if (string.IsNullOrEmpty(indexData))
        {
            return Enumerable.Empty<JourneyState>();
        }

        try
        {
            var journeyIds = JsonSerializer.Deserialize<HashSet<string>>(indexData, JsonOptions);
            if (journeyIds == null || journeyIds.Count == 0)
            {
                return Enumerable.Empty<JourneyState>();
            }

            var states = new List<JourneyState>();
            foreach (var journeyId in journeyIds)
            {
                var state = await GetAsync(journeyId, cancellationToken);
                if (state != null)
                {
                    states.Add(state);
                }
            }

            return states;
        }
        catch (JsonException)
        {
            return Enumerable.Empty<JourneyState>();
        }
    }

    public Task CleanupExpiredAsync(CancellationToken cancellationToken = default)
    {
        // Distributed cache handles expiration automatically via TTL
        // This is a no-op for distributed cache implementations
        return Task.CompletedTask;
    }

    private string GetKey(string journeyId) =>
        $"{_options.KeyPrefix}journey:{journeyId}";

    private string GetUserIndexKey(string userId) =>
        $"{_options.KeyPrefix}user-journeys:{userId}";

    private async Task AddToUserIndexAsync(string userId, string journeyId, CancellationToken cancellationToken)
    {
        var indexKey = GetUserIndexKey(userId);
        var indexData = await _cache.GetStringAsync(indexKey, cancellationToken);

        HashSet<string> journeyIds;
        if (string.IsNullOrEmpty(indexData))
        {
            journeyIds = new HashSet<string>();
        }
        else
        {
            journeyIds = JsonSerializer.Deserialize<HashSet<string>>(indexData, JsonOptions)
                ?? new HashSet<string>();
        }

        journeyIds.Add(journeyId);

        var options = new DistributedCacheEntryOptions
        {
            AbsoluteExpirationRelativeToNow = _options.UserIndexExpiration
        };

        await _cache.SetStringAsync(
            indexKey,
            JsonSerializer.Serialize(journeyIds, JsonOptions),
            options,
            cancellationToken);
    }

    private async Task RemoveFromUserIndexAsync(string userId, string journeyId, CancellationToken cancellationToken)
    {
        var indexKey = GetUserIndexKey(userId);
        var indexData = await _cache.GetStringAsync(indexKey, cancellationToken);

        if (string.IsNullOrEmpty(indexData))
        {
            return;
        }

        var journeyIds = JsonSerializer.Deserialize<HashSet<string>>(indexData, JsonOptions);
        if (journeyIds == null)
        {
            return;
        }

        journeyIds.Remove(journeyId);

        if (journeyIds.Count == 0)
        {
            await _cache.RemoveAsync(indexKey, cancellationToken);
        }
        else
        {
            var options = new DistributedCacheEntryOptions
            {
                AbsoluteExpirationRelativeToNow = _options.UserIndexExpiration
            };

            await _cache.SetStringAsync(
                indexKey,
                JsonSerializer.Serialize(journeyIds, JsonOptions),
                options,
                cancellationToken);
        }
    }
}

/// <summary>
/// Options for the distributed cache journey state store
/// </summary>
public class DistributedCacheJourneyStateStoreOptions
{
    /// <summary>
    /// Key prefix for all cache entries (default: "oluso:")
    /// </summary>
    public string KeyPrefix { get; set; } = "oluso:";

    /// <summary>
    /// Default expiration for journey state if not specified (default: 30 minutes)
    /// </summary>
    public TimeSpan DefaultExpiration { get; set; } = TimeSpan.FromMinutes(30);

    /// <summary>
    /// Expiration for user journey index (default: 1 hour)
    /// </summary>
    public TimeSpan UserIndexExpiration { get; set; } = TimeSpan.FromHours(1);
}
