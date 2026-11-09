using Oluso.Core.Domain.Entities;

namespace Oluso.Core.Domain.Interfaces;

/// <summary>
/// Persistence contract for <see cref="ConversationMessage"/>. The store
/// is module-agnostic — modules attach their own threads by re-using the
/// (<c>EntityType</c>, <c>EntityId</c>) pair.
/// </summary>
public interface IConversationMessageStore
{
    /// <summary>
    /// All messages for a thread, oldest first. Soft-deleted messages are
    /// included so the UI can render "(deleted)" placeholders in-line.
    /// </summary>
    Task<IReadOnlyList<ConversationMessage>> ListAsync(
        string tenantId,
        string entityType,
        string entityId,
        CancellationToken cancellationToken = default);

    Task<ConversationMessage> CreateAsync(
        ConversationMessage message,
        CancellationToken cancellationToken = default);

    Task<ConversationMessage?> GetByIdAsync(
        string tenantId,
        string id,
        CancellationToken cancellationToken = default);

    Task<ConversationMessage> UpdateAsync(
        ConversationMessage message,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Messages in this tenant that <paramref name="userId"/> was mentioned
    /// in (via <see cref="ConversationMessage.MentionedUserIdsJson"/>) and
    /// has not yet acknowledged via <see cref="IConversationMentionReadStore.MarkReadAsync"/>.
    /// Newest first. Powers the Mentions tile in the Action Center.
    /// </summary>
    Task<IReadOnlyList<ConversationMessage>> ListUnreadMentionsForUserAsync(
        string tenantId,
        string userId,
        int limit = 50,
        CancellationToken cancellationToken = default);
}

/// <summary>
/// Per-(message, user) read receipts for @mentions. Separated from
/// <see cref="IConversationMessageStore"/> so the message store stays
/// focused on the thread itself.
/// </summary>
public interface IConversationMentionReadStore
{
    /// <summary>
    /// Ack the given <paramref name="messageIds"/> for the current user.
    /// Idempotent — duplicate calls are no-ops (the unique index on
    /// (TenantId, UserId, MessageId) guarantees one row per pair).
    /// </summary>
    Task MarkReadAsync(
        string tenantId,
        string userId,
        IReadOnlyCollection<string> messageIds,
        CancellationToken cancellationToken = default);
}
