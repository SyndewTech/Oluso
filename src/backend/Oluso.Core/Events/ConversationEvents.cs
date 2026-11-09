namespace Oluso.Core.Events;

/// <summary>
/// Webhook event types for conversation threads (comments on any entity: approval requests,
/// candidate profiles, …). The body is not carried — subscribers re-read the thread through the
/// Service API, which keeps comment text out of outbound payloads.
/// </summary>
public static class ConversationWebhookEvents
{
    public const string MessageCreated = "conversation.message.created";
    public const string MessageUpdated = "conversation.message.updated";
    public const string MessageDeleted = "conversation.message.deleted";
}

/// <summary>
/// Base for conversation message events. Raised by the conversation service for every entity
/// type; the origin is the writer (the user, or the service client that wrote on their behalf),
/// so an endpoint with <c>SkipOwnClientEvents</c> doesn't hear its own writes.
/// </summary>
public abstract class ConversationMessageEvent : OlusoEvent
{
    public override string Category => EventCategories.Conversation;

    public required string MessageId { get; init; }

    /// <summary>Thread subject type, e.g. <c>approval.instance</c>.</summary>
    public required string EntityType { get; init; }

    public required string EntityId { get; init; }

    public required string AuthorUserId { get; init; }

    public string? AuthorName { get; init; }

    /// <summary>OAuth client the message was written through, when known.</summary>
    public string? ClientId { get; init; }
}

[WebhookEvent("Conversation Message Created",
    Description = "Triggered when a comment is posted on a conversation thread")]
public sealed class ConversationMessageCreatedEvent : ConversationMessageEvent
{
    public override string? WebhookEventType => ConversationWebhookEvents.MessageCreated;
}

[WebhookEvent("Conversation Message Updated",
    Description = "Triggered when the author edits a comment on a conversation thread")]
public sealed class ConversationMessageUpdatedEvent : ConversationMessageEvent
{
    public override string? WebhookEventType => ConversationWebhookEvents.MessageUpdated;
}

[WebhookEvent("Conversation Message Deleted",
    Description = "Triggered when the author deletes a comment on a conversation thread")]
public sealed class ConversationMessageDeletedEvent : ConversationMessageEvent
{
    public override string? WebhookEventType => ConversationWebhookEvents.MessageDeleted;
}
