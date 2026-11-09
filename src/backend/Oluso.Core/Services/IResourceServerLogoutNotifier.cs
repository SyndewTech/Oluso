using Oluso.Core.Domain.Entities;

namespace Oluso.Core.Services;

/// <summary>
/// Back-channel logout for resource servers that track sessions themselves (OpenID Connect Back-Channel Logout
/// 1.0 logout tokens). A Matrix homeserver using the Matrix OAuth 2.0 API, for example, keeps a device per sign-in
/// and must hear when the session behind one ends, though the tokens belong to the Matrix client, not to it. A
/// watcher is a client with a <c>BackChannelLogoutUri</c> that is sent a logout token (its client id as audience,
/// the session's <c>sid</c>) whenever a session ends whose grants hold one of its scopes.
/// </summary>
public interface IResourceServerLogoutNotifier
{
    /// <summary>
    /// Sends the watchers a logout token for each user session among <paramref name="endedGrants"/> holding one of
    /// their scopes. Failures are logged, never thrown.
    /// </summary>
    Task NotifyAsync(IEnumerable<PersistedGrant> endedGrants, CancellationToken cancellationToken = default);
}

/// <summary>The resource servers that hear about ended sessions (<see cref="IResourceServerLogoutNotifier"/>).</summary>
public sealed class ResourceServerLogoutOptions
{
    public List<ResourceServerLogoutWatcher> Watchers { get; } = new();
}

/// <summary>A client notified when a session holding one of <see cref="Scopes"/> ends.</summary>
public sealed class ResourceServerLogoutWatcher
{
    public required string ClientId { get; init; }
    public required IReadOnlyCollection<string> Scopes { get; init; }
}
