namespace Oluso.Core.Notifications;

/// <summary>
/// Pluggable access check for a specific entity type's conversation
/// thread. Modules implement this to gate the generic
/// <c>/api/workspace/threads/{entityType}/{entityId}</c> endpoint —
/// without one, the controller refuses requests for an unknown type so a
/// caller can't bypass module-level auth by guessing entity types.
///
/// <para>Approval registers an authorizer for <c>approval.task</c> /
/// <c>approval.instance</c> that lets the requester and assigned
/// approvers read &amp; post; other modules will plug in their own
/// (e.g. &lt;module&gt;.&lt;entity&gt;, as needed) as the need arises.</para>
/// </summary>
public interface IConversationAuthorizer
{
    /// <summary>Entity type this authorizer matches — case-sensitive.</summary>
    string EntityType { get; }

    /// <summary>
    /// True when <paramref name="userId"/> may view messages on this thread.
    /// Returns false to surface a 403 to the caller.
    /// </summary>
    Task<bool> CanReadAsync(
        string tenantId,
        string entityId,
        string userId,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// True when <paramref name="userId"/> may post a new message. Usually
    /// stricter than <see cref="CanReadAsync"/> — e.g. observers may read
    /// but not contribute.
    /// </summary>
    Task<bool> CanWriteAsync(
        string tenantId,
        string entityId,
        string userId,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Users who may legitimately be tagged with <c>@</c> in messages on
    /// this thread. Drives the autocomplete picker AND server-side
    /// validation — mention ids that aren't in this set are silently
    /// dropped so a malicious client can't notify arbitrary users.
    /// Implementations should include anyone with read access (they're
    /// addressable in a comment) and may exclude bots / system users.
    /// </summary>
    Task<IReadOnlyList<ConversationParticipant>> GetParticipantsAsync(
        string tenantId,
        string entityId,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Whether the thread's subject exists in the tenant: true, false, or null when the
    /// authorizer can't tell. Lets the Service API answer 404 for a missing entity rather
    /// than refusing the author.
    /// </summary>
    Task<bool?> ExistsAsync(
        string tenantId,
        string entityId,
        CancellationToken cancellationToken = default)
        => Task.FromResult<bool?>(null);

    /// <summary>
    /// Scopes that let a scope-restricted client read this thread with a user's
    /// token, any one of them. Empty (the default) keeps such clients out.
    /// </summary>
    IReadOnlyList<string> RestrictedClientReadScopes => Array.Empty<string>();

    /// <summary>
    /// Scopes that let a scope-restricted client post, edit and delete on this thread with a
    /// user's token, any one of them. Empty (the default) keeps such clients out.
    /// </summary>
    IReadOnlyList<string> RestrictedClientWriteScopes => Array.Empty<string>();
}

/// <summary>
/// One user that can be mentioned (and notified) on a conversation thread.
/// </summary>
public sealed record ConversationParticipant(
    string UserId,
    string? DisplayName,
    string? Email);
