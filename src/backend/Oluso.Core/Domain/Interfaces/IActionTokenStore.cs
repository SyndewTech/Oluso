using Oluso.Core.Domain.Entities;

namespace Oluso.Core.Domain.Interfaces;

/// <summary>
/// Persistence contract for <see cref="ActionToken"/>. Implementations live
/// next to the project's EF DbContext — the production one is in
/// <c>Oluso.EntityFramework</c>. Lookups go through the <c>TokenHash</c>
/// column; raw tokens never reach the DB.
/// </summary>
public interface IActionTokenStore
{
    Task<ActionToken> CreateAsync(ActionToken token, CancellationToken cancellationToken = default);

    /// <summary>
    /// Find a token by its hash. Returns the row regardless of
    /// expiry/consumed state — callers (or the service layer) decide
    /// whether to honour it.
    /// </summary>
    Task<ActionToken?> GetByHashAsync(string tokenHash, CancellationToken cancellationToken = default);

    /// <summary>
    /// Marks the token consumed. Single SQL UPDATE so a race between two
    /// redeems for the same raw token can never let both succeed.
    /// </summary>
    Task<bool> TryConsumeAsync(string id, DateTime consumedAtUtc, CancellationToken cancellationToken = default);

    /// <summary>
    /// Clears a consumption made moments ago, when the action it was redeemed
    /// for failed, so the recipient can use the link again.
    /// </summary>
    Task ReleaseAsync(string id, CancellationToken cancellationToken = default);

    /// <summary>Deletes tokens whose expiry has passed. Returns the number removed.</summary>
    Task<int> RemoveExpiredAsync(DateTime nowUtc, CancellationToken cancellationToken = default);
}
