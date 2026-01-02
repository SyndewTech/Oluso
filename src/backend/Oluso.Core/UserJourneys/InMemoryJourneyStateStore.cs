using System.Collections.Concurrent;

namespace Oluso.Core.UserJourneys;

/// <summary>
/// In-memory implementation of journey state store (for development/testing).
/// For production, use a distributed cache like Redis.
/// </summary>
public class InMemoryJourneyStateStore : IJourneyStateStore
{
    private readonly ConcurrentDictionary<string, JourneyState> _states = new();

    public Task<JourneyState?> GetAsync(string journeyId, CancellationToken cancellationToken = default)
    {
        _states.TryGetValue(journeyId, out var state);
        return Task.FromResult(state);
    }

    public Task SaveAsync(JourneyState state, CancellationToken cancellationToken = default)
    {
        _states[state.JourneyId] = state;
        return Task.CompletedTask;
    }

    public Task DeleteAsync(string journeyId, CancellationToken cancellationToken = default)
    {
        _states.TryRemove(journeyId, out _);
        return Task.CompletedTask;
    }

    public Task<IEnumerable<JourneyState>> GetByUserAsync(string userId, CancellationToken cancellationToken = default)
    {
        var states = _states.Values
            .Where(s => s.UserId == userId)
            .ToList();
        return Task.FromResult<IEnumerable<JourneyState>>(states);
    }

    public Task CleanupExpiredAsync(CancellationToken cancellationToken = default)
    {
        var now = DateTime.UtcNow;
        var expiredIds = _states
            .Where(kvp => kvp.Value.ExpiresAt.HasValue && kvp.Value.ExpiresAt < now)
            .Select(kvp => kvp.Key)
            .ToList();

        foreach (var id in expiredIds)
        {
            _states.TryRemove(id, out _);
        }

        return Task.CompletedTask;
    }
}
