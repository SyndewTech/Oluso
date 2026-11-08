namespace Oluso.Core.Domain.Interfaces;

/// <summary>
/// Persistence for <see cref="Entities.PasswordHistoryEntry"/> rows: the hashes of a user's
/// recent passwords, newest first.
/// </summary>
public interface IPasswordHistoryStore
{
    /// <summary>The user's most recent password hashes, newest first.</summary>
    Task<IReadOnlyList<string>> GetRecentHashesAsync(string userId, int count, CancellationToken cancellationToken = default);

    /// <summary>
    /// Records <paramref name="passwordHash"/> as the user's newest password and keeps only the
    /// <paramref name="retain"/> most recent entries (0 removes them all).
    /// </summary>
    Task RecordAsync(string userId, string passwordHash, int retain, CancellationToken cancellationToken = default);
}
