namespace Oluso.Core.UserJourneys;

/// <summary>
/// Store for journey runtime state (active sessions).
/// Implementations can use in-memory, Redis, database, etc.
/// </summary>
public interface IJourneyStateStore
{
    /// <summary>
    /// Gets a journey state by ID
    /// </summary>
    Task<JourneyState?> GetAsync(string journeyId, CancellationToken cancellationToken = default);

    /// <summary>
    /// Saves or updates journey state
    /// </summary>
    Task SaveAsync(JourneyState state, CancellationToken cancellationToken = default);

    /// <summary>
    /// Deletes a journey state
    /// </summary>
    Task DeleteAsync(string journeyId, CancellationToken cancellationToken = default);

    /// <summary>
    /// Gets all active journeys for a user (for session management)
    /// </summary>
    Task<IEnumerable<JourneyState>> GetByUserAsync(string userId, CancellationToken cancellationToken = default);

    /// <summary>
    /// Removes expired journey states
    /// </summary>
    Task CleanupExpiredAsync(CancellationToken cancellationToken = default);
}
