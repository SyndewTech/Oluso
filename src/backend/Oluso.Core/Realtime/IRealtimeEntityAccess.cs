namespace Oluso.Core.Realtime;

/// <summary>
/// Pluggable "may this user watch this entity" check for the realtime hub's
/// per-entity groups. A page showing one record (an approval request, say)
/// calls <c>Watch(entityType, entityId)</c> on the hub; the hub resolves the
/// access for <paramref name="entityType"/> and joins the connection to the
/// entity group only when it allows. Without a registration for the entity
/// type, the watch is refused (fail-closed), so a client can't reach an
/// entity's pushes by guessing types.
///
/// <para>Implementations must apply the same rule the entity's own read API
/// uses, so the group never reaches anyone who couldn't load the page's data.
/// Approval implements this for <c>approval.instance</c> with its conversation
/// authorizer's read check.</para>
/// </summary>
public interface IRealtimeEntityAccess
{
    /// <summary>Entity type this access check matches, case-sensitive.</summary>
    string EntityType { get; }

    /// <summary>True when <paramref name="userId"/> may receive pushes for the entity.</summary>
    Task<bool> CanWatchAsync(
        string tenantId,
        string entityId,
        string userId,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Scopes that let a scope-restricted client watch with a user's token, any one of them.
    /// Empty (the default) keeps such clients out, as on the user-facing APIs.
    /// </summary>
    IReadOnlyList<string> RestrictedClientScopes => Array.Empty<string>();
}
