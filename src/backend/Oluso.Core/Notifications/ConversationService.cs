using System.Text.Json;
using Microsoft.Extensions.Logging;
using Oluso.Core.Domain.Entities;
using Oluso.Core.Domain.Interfaces;
using Oluso.Core.Events;
using Oluso.Core.Realtime;
using Oluso.Core.Services;

namespace Oluso.Core.Notifications;

/// <summary>What a conversation write came to.</summary>
public enum ConversationOutcome
{
    Ok,

    /// <summary>No <see cref="IConversationAuthorizer"/> is registered for the entity type.</summary>
    UnknownEntityType,

    /// <summary>The thread's subject doesn't exist in the tenant.</summary>
    EntityNotFound,

    /// <summary>The message doesn't exist on this thread.</summary>
    MessageNotFound,

    /// <summary>The user may not do this: not on the thread, or not the message's author.</summary>
    Forbidden,

    /// <summary>The request is invalid; see <see cref="ConversationResult{T}.Error"/>.</summary>
    Invalid,

    /// <summary>The message was deleted and can't be edited.</summary>
    Deleted,
}

public sealed record ConversationResult<T>(ConversationOutcome Outcome, T? Value = default, string? Error = null)
{
    public bool Succeeded => Outcome == ConversationOutcome.Ok;

    public static ConversationResult<T> Ok(T value) => new(ConversationOutcome.Ok, value);
    public static ConversationResult<T> Fail(ConversationOutcome outcome, string? error = null) => new(outcome, default, error);
}

/// <summary>A message with its author's display name resolved.</summary>
public sealed record ConversationMessageView(ConversationMessage Message, string? AuthorName);

/// <summary>
/// The conversation thread operations shared by the workspace API (the signed-in user writes)
/// and the Service API (a trusted client writes on a named author's behalf). Access is always
/// checked for the author with the entity type's <see cref="IConversationAuthorizer"/>; every write
/// sends the realtime pushes (to the pages watching the entity, see <see cref="IRealtimeBroadcaster.BroadcastToEntityAsync"/>)
/// and raises the <c>conversation.message.*</c> events.
/// </summary>
public interface IConversationService
{
    /// <summary>Maximum message length, in characters.</summary>
    const int MaxBodyLength = 4000;

    IConversationAuthorizer? FindAuthorizer(string entityType);

    /// <summary>
    /// The thread, oldest first, including soft-deleted messages. Doesn't check access — callers
    /// authorise first.
    /// </summary>
    Task<IReadOnlyList<ConversationMessageView>> ListAsync(
        string tenantId, string entityType, string entityId, CancellationToken cancellationToken = default);

    /// <summary>Posts a message as <paramref name="authorUserId"/>, who must be allowed to write.</summary>
    /// <param name="clientId">The OAuth client the write came through, stored on the message.</param>
    Task<ConversationResult<ConversationMessageView>> PostAsync(
        string tenantId, string entityType, string entityId, string authorUserId, string? body,
        IReadOnlyList<string>? mentionedUserIds, string? clientId, CancellationToken cancellationToken = default);

    /// <summary>Edits a message; only its author, while still on the thread, may.</summary>
    Task<ConversationResult<ConversationMessageView>> EditAsync(
        string tenantId, string entityType, string entityId, string messageId, string authorUserId, string? body,
        IReadOnlyList<string>? mentionedUserIds, string? clientId, CancellationToken cancellationToken = default);

    /// <summary>Soft-deletes a message; only its author, while still on the thread, may. Deleting twice is a no-op.</summary>
    Task<ConversationResult<ConversationMessageView>> DeleteAsync(
        string tenantId, string entityType, string entityId, string messageId, string authorUserId,
        string? clientId, CancellationToken cancellationToken = default);
}

/// <inheritdoc />
public sealed class ConversationService : IConversationService
{
    /// <summary>Channel name the conversation thread broadcasts on.</summary>
    public const string ConversationChannel = "conversation";

    /// <summary>
    /// Shared Action Center channel — every module that contributes to the unified inbox pings
    /// here so the badge re-fetches its count without polling.
    /// </summary>
    public const string ActionCenterChannel = "action-center";

    private readonly IConversationMessageStore _store;
    private readonly IEnumerable<IConversationAuthorizer> _authorizers;
    private readonly IOlusoUserService _userService;
    private readonly IRealtimeBroadcaster _realtime;
    private readonly IOlusoEventService? _events;
    private readonly ILogger<ConversationService> _logger;

    public ConversationService(
        IConversationMessageStore store,
        IEnumerable<IConversationAuthorizer> authorizers,
        IOlusoUserService userService,
        IRealtimeBroadcaster realtime,
        ILogger<ConversationService> logger,
        IOlusoEventService? events = null)
    {
        _store = store;
        _authorizers = authorizers;
        _userService = userService;
        _realtime = realtime;
        _logger = logger;
        _events = events;
    }

    public IConversationAuthorizer? FindAuthorizer(string entityType)
        => _authorizers.FirstOrDefault(a => string.Equals(a.EntityType, entityType, StringComparison.Ordinal));

    public async Task<IReadOnlyList<ConversationMessageView>> ListAsync(
        string tenantId, string entityType, string entityId, CancellationToken cancellationToken = default)
    {
        var messages = await _store.ListAsync(tenantId, entityType, entityId, cancellationToken);
        var names = await ResolveNamesAsync(messages.Select(m => m.AuthorUserId), cancellationToken);
        return messages.Select(m => new ConversationMessageView(m, names.GetValueOrDefault(m.AuthorUserId))).ToList();
    }

    public async Task<ConversationResult<ConversationMessageView>> PostAsync(
        string tenantId, string entityType, string entityId, string authorUserId, string? body,
        IReadOnlyList<string>? mentionedUserIds, string? clientId, CancellationToken cancellationToken = default)
    {
        if (ValidateBody(body) is { } invalid) return invalid;

        var auth = FindAuthorizer(entityType);
        if (auth == null) return ConversationResult<ConversationMessageView>.Fail(ConversationOutcome.UnknownEntityType);
        if (string.IsNullOrEmpty(authorUserId)
            || !await auth.CanWriteAsync(tenantId, entityId, authorUserId, cancellationToken))
        {
            return ConversationResult<ConversationMessageView>.Fail(ConversationOutcome.Forbidden);
        }

        // Validate mention ids against the authorizer's participant list so a client can't
        // spoof @mentions to pull arbitrary users into a notification. Self-mentions are dropped.
        var participants = await auth.GetParticipantsAsync(tenantId, entityId, cancellationToken);
        var validatedMentions = ValidMentions(participants, authorUserId, mentionedUserIds);

        var msg = new ConversationMessage
        {
            TenantId = tenantId,
            EntityType = entityType,
            EntityId = entityId,
            AuthorUserId = authorUserId,
            Body = body!.Trim(),
            MentionedUserIdsJson = validatedMentions.Count > 0 ? JsonSerializer.Serialize(validatedMentions) : null,
            ClientId = string.IsNullOrEmpty(clientId) ? null : clientId,
        };
        await _store.CreateAsync(msg, cancellationToken);

        var authorName = (await ResolveNamesAsync([authorUserId], cancellationToken)).GetValueOrDefault(authorUserId);
        _logger.LogInformation(
            "Conversation message posted: entity={EntityType}/{EntityId} by {UserId} client={ClientId} mentions={MentionCount}",
            entityType, entityId, authorUserId, msg.ClientId, validatedMentions.Count);

        // Realtime push on the "conversation" channel:
        //   * "posted" → the entity group once: every page watching this thread refreshes,
        //     whoever is looking, the author included (the write may have come through another
        //     client, e.g. a chat bridge, while the author's page is open). Pages match by
        //     messageId, so a write they made themselves isn't shown twice.
        //   * "posted" → each participant but the author, to their user group: the "new
        //     comment" notice for participants not on the page (the mobile shell toasts on it).
        //   * "mentioned" → only the tagged users (toast / inbox count)
        var postedPayload = new
        {
            entityType,
            entityId,
            messageId = msg.Id,
            authorId = authorUserId,
            authorName,
            hasMention = validatedMentions.Count > 0,
        };
        await _realtime.BroadcastToEntityAsync(
            tenantId, entityType, entityId, ConversationChannel, "posted", postedPayload, cancellationToken);
        var participantIds = participants
            .Select(p => p.UserId)
            .Where(id => !string.IsNullOrEmpty(id) && id != authorUserId)
            .Distinct(StringComparer.Ordinal)
            .ToList();
        foreach (var participantId in participantIds)
        {
            await _realtime.BroadcastToUserAsync(
                tenantId, participantId, ConversationChannel, "posted", postedPayload, cancellationToken);
        }
        await NotifyMentionedAsync(tenantId, entityType, entityId, msg.Id, authorUserId, authorName, validatedMentions, cancellationToken);

        await RaiseAsync(new ConversationMessageCreatedEvent
        {
            MessageId = msg.Id,
            EntityType = entityType,
            EntityId = entityId,
            AuthorUserId = authorUserId,
            AuthorName = authorName,
            ClientId = msg.ClientId,
        }, tenantId, cancellationToken);

        return ConversationResult<ConversationMessageView>.Ok(new ConversationMessageView(msg, authorName));
    }

    public async Task<ConversationResult<ConversationMessageView>> EditAsync(
        string tenantId, string entityType, string entityId, string messageId, string authorUserId, string? body,
        IReadOnlyList<string>? mentionedUserIds, string? clientId, CancellationToken cancellationToken = default)
    {
        if (ValidateBody(body) is { } invalid) return invalid;

        var (auth, existing, failure) = await LoadOwnMessageAsync(tenantId, entityType, entityId, messageId, authorUserId, cancellationToken);
        if (failure != null) return failure;
        if (existing!.DeletedAt != null)
            return ConversationResult<ConversationMessageView>.Fail(ConversationOutcome.Deleted, "Message has been deleted");

        // Re-validate mentions on edit so adding @user during an edit also pings them.
        // Already-mentioned users are not re-notified (no spam from wording tweaks).
        var participants = await auth!.GetParticipantsAsync(tenantId, entityId, cancellationToken);
        var validatedMentions = ValidMentions(participants, authorUserId, mentionedUserIds);
        var previousMentions = ParseMentions(existing.MentionedUserIdsJson).ToHashSet(StringComparer.Ordinal);

        existing.Body = body!.Trim();
        existing.EditedAt = DateTime.UtcNow;
        existing.MentionedUserIdsJson = validatedMentions.Count > 0 ? JsonSerializer.Serialize(validatedMentions) : null;
        await _store.UpdateAsync(existing, cancellationToken);

        var authorName = (await ResolveNamesAsync([existing.AuthorUserId], cancellationToken)).GetValueOrDefault(existing.AuthorUserId);
        var newlyMentioned = validatedMentions.Where(id => !previousMentions.Contains(id)).ToList();
        await _realtime.BroadcastToEntityAsync(
            tenantId, entityType, entityId, ConversationChannel, "edited",
            new { entityType, entityId, messageId = existing.Id, authorId = existing.AuthorUserId, editedAt = existing.EditedAt },
            cancellationToken);
        await NotifyMentionedAsync(tenantId, entityType, entityId, existing.Id, authorUserId, authorName, newlyMentioned, cancellationToken);

        await RaiseAsync(new ConversationMessageUpdatedEvent
        {
            MessageId = existing.Id,
            EntityType = entityType,
            EntityId = entityId,
            AuthorUserId = existing.AuthorUserId,
            AuthorName = authorName,
            ClientId = string.IsNullOrEmpty(clientId) ? null : clientId,
        }, tenantId, cancellationToken);

        return ConversationResult<ConversationMessageView>.Ok(new ConversationMessageView(existing, authorName));
    }

    public async Task<ConversationResult<ConversationMessageView>> DeleteAsync(
        string tenantId, string entityType, string entityId, string messageId, string authorUserId,
        string? clientId, CancellationToken cancellationToken = default)
    {
        var (_, existing, failure) = await LoadOwnMessageAsync(tenantId, entityType, entityId, messageId, authorUserId, cancellationToken);
        if (failure != null) return failure;

        var authorName = (await ResolveNamesAsync([existing!.AuthorUserId], cancellationToken)).GetValueOrDefault(existing.AuthorUserId);
        if (existing.DeletedAt != null)
            return ConversationResult<ConversationMessageView>.Ok(new ConversationMessageView(existing, authorName));

        existing.DeletedAt = DateTime.UtcNow;
        existing.DeletedByUserId = authorUserId;
        await _store.UpdateAsync(existing, cancellationToken);

        await _realtime.BroadcastToEntityAsync(
            tenantId, entityType, entityId, ConversationChannel, "deleted",
            new { entityType, entityId, messageId = existing.Id, authorId = existing.AuthorUserId, deletedAt = existing.DeletedAt },
            cancellationToken);

        await RaiseAsync(new ConversationMessageDeletedEvent
        {
            MessageId = existing.Id,
            EntityType = entityType,
            EntityId = entityId,
            AuthorUserId = existing.AuthorUserId,
            AuthorName = authorName,
            ClientId = string.IsNullOrEmpty(clientId) ? null : clientId,
        }, tenantId, cancellationToken);

        return ConversationResult<ConversationMessageView>.Ok(new ConversationMessageView(existing, authorName));
    }

    /// <summary>
    /// Read access is enough to reach a message at all; the author-only check follows on the row.
    /// </summary>
    private async Task<(IConversationAuthorizer? Auth, ConversationMessage? Message, ConversationResult<ConversationMessageView>? Failure)>
        LoadOwnMessageAsync(string tenantId, string entityType, string entityId, string messageId, string authorUserId,
            CancellationToken cancellationToken)
    {
        var auth = FindAuthorizer(entityType);
        if (auth == null) return (null, null, ConversationResult<ConversationMessageView>.Fail(ConversationOutcome.UnknownEntityType));
        if (string.IsNullOrEmpty(authorUserId) || !await auth.CanReadAsync(tenantId, entityId, authorUserId, cancellationToken))
            return (auth, null, ConversationResult<ConversationMessageView>.Fail(ConversationOutcome.Forbidden));

        var existing = await _store.GetByIdAsync(tenantId, messageId, cancellationToken);
        if (existing == null || existing.EntityType != entityType || existing.EntityId != entityId)
            return (auth, null, ConversationResult<ConversationMessageView>.Fail(ConversationOutcome.MessageNotFound));
        if (!string.Equals(existing.AuthorUserId, authorUserId, StringComparison.Ordinal))
            return (auth, existing, ConversationResult<ConversationMessageView>.Fail(ConversationOutcome.Forbidden));

        return (auth, existing, null);
    }

    private static ConversationResult<ConversationMessageView>? ValidateBody(string? body)
    {
        if (string.IsNullOrWhiteSpace(body))
            return ConversationResult<ConversationMessageView>.Fail(ConversationOutcome.Invalid, "body is required");
        if (body.Length > IConversationService.MaxBodyLength)
            return ConversationResult<ConversationMessageView>.Fail(ConversationOutcome.Invalid,
                $"body exceeds {IConversationService.MaxBodyLength} characters");
        return null;
    }

    private static List<string> ValidMentions(
        IReadOnlyList<ConversationParticipant> participants, string authorUserId, IReadOnlyList<string>? requested)
    {
        if (requested is null || requested.Count == 0) return new();
        var participantIds = participants.Select(p => p.UserId).ToHashSet(StringComparer.Ordinal);
        return requested
            .Where(id => !string.IsNullOrEmpty(id))
            .Where(id => id != authorUserId)     // drop self-mentions
            .Where(participantIds.Contains)      // drop unauthorised mentions
            .Distinct(StringComparer.Ordinal)
            .ToList();
    }

    /// <summary>The message's mentioned user ids; empty when none or the stored JSON is unreadable.</summary>
    public static IReadOnlyList<string> ParseMentions(string? json)
    {
        if (string.IsNullOrEmpty(json)) return Array.Empty<string>();
        try { return JsonSerializer.Deserialize<List<string>>(json) ?? new List<string>(); }
        catch { return Array.Empty<string>(); }
    }

    private async Task NotifyMentionedAsync(
        string tenantId, string entityType, string entityId, string messageId, string mentionedBy,
        string? mentionedByName, IReadOnlyList<string> mentioned, CancellationToken cancellationToken)
    {
        if (mentioned.Count == 0) return;
        var payload = new { entityType, entityId, messageId, mentionedBy, mentionedByName };
        foreach (var mentionedId in mentioned)
        {
            await _realtime.BroadcastToUserAsync(
                tenantId, mentionedId, ConversationChannel, "mentioned", payload, cancellationToken);
            // Inbox badge — the mentioned user has a new row in the Action Center.
            await _realtime.BroadcastToUserAsync(
                tenantId, mentionedId, ActionCenterChannel, "changed", null, cancellationToken);
        }
    }

    private async Task<Dictionary<string, string?>> ResolveNamesAsync(
        IEnumerable<string> userIds, CancellationToken cancellationToken)
    {
        var ids = userIds.Where(id => !string.IsNullOrEmpty(id)).Distinct(StringComparer.Ordinal).ToArray();
        if (ids.Length == 0) return new Dictionary<string, string?>(StringComparer.Ordinal);
        // One batched query — never Task.WhenAll over FindByIdAsync (concurrent reads on the
        // shared scoped DbContext trip EF Core's concurrency detector).
        var users = await _userService.FindByIdsAsync(ids, cancellationToken);
        return ids.ToDictionary(
            id => id,
            id => users.TryGetValue(id, out var u) ? u.ResolveDisplayName() : null,
            StringComparer.Ordinal);
    }

    private async Task RaiseAsync(OlusoEvent evt, string tenantId, CancellationToken cancellationToken)
    {
        if (_events == null) return;
        try
        {
            evt.TenantId = tenantId;
            await _events.RaiseAsync(evt, cancellationToken);
        }
        catch (Exception ex)
        {
            // The message is saved; a failed event must not turn the write into an error.
            _logger.LogWarning(ex, "Failed to raise conversation event {EventType}", evt.GetType().Name);
        }
    }
}
