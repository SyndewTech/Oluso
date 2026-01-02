using Oluso.Enterprise.Ldap.Authentication;

namespace Oluso.Enterprise.Ldap.UserSync;

/// <summary>
/// Result of a user synchronization operation
/// </summary>
public class LdapSyncResult
{
    public int TotalUsers { get; set; }
    public int Created { get; set; }
    public int Updated { get; set; }
    public int Disabled { get; set; }
    public int Errors { get; set; }
    public List<string> ErrorMessages { get; set; } = new();
    public TimeSpan Duration { get; set; }
}

/// <summary>
/// Service for synchronizing users from LDAP to the local user store
/// </summary>
public interface ILdapUserSync
{
    /// <summary>
    /// Synchronizes all users from LDAP
    /// </summary>
    Task<LdapSyncResult> SyncAllUsersAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// Synchronizes a single user by username
    /// </summary>
    Task<LdapUser?> SyncUserAsync(string username, CancellationToken cancellationToken = default);

    /// <summary>
    /// Gets all users from LDAP without syncing
    /// </summary>
    Task<IReadOnlyList<LdapUser>> GetAllLdapUsersAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// Gets users modified since a specific time (if supported by directory)
    /// </summary>
    Task<IReadOnlyList<LdapUser>> GetModifiedUsersAsync(
        DateTime since,
        CancellationToken cancellationToken = default);
}
