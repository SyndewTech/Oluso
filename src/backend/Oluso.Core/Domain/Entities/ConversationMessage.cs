namespace Oluso.Core.Domain.Entities;

/// <summary>
/// One message on a conversation thread. Threads are implicit — every
/// message just carries (<see cref="EntityType"/>, <see cref="EntityId"/>),
/// and "the thread for X" is "all messages where EntityType+EntityId == X
/// in tenant order by CreatedAt." That keeps the schema flat and lets any
/// module attach comments to its own entities without registering a
/// thread row up front.
///
/// <para>Modules control who can read and post by registering an
/// <c>IConversationAuthorizer</c> for their <see cref="EntityType"/>; the
/// generic workspace controller routes through that authorizer before
/// touching the store.</para>
/// </summary>
public class ConversationMessage : TenantEntity
{
    public string Id { get; set; } = Guid.NewGuid().ToString();

    /// <summary>
    /// Stable key for the conversation subject (e.g.
    /// <c>approval.task</c>, <c>&lt;module&gt;.&lt;entity&gt;</c>). Use the same
    /// namespacing convention as the event / action-token keys so
    /// authorizers can be looked up by a single string.
    /// </summary>
    public string EntityType { get; set; } = null!;

    /// <summary>The id of the subject this thread is attached to.</summary>
    public string EntityId { get; set; } = null!;

    /// <summary>Author — the user who posted the message.</summary>
    public string AuthorUserId { get; set; } = null!;

    /// <summary>
    /// Plain-text message body. No formatting in v1; the UI escapes for
    /// display. Markdown / mentions / attachments are deliberately out of
    /// scope until there's a real product need.
    /// </summary>
    public string Body { get; set; } = null!;

    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

    /// <summary>Soft-delete marker. Edited / deleted messages keep the row for audit.</summary>
    public DateTime? DeletedAt { get; set; }
    public string? DeletedByUserId { get; set; }

    /// <summary>Updated when the author edits their message.</summary>
    public DateTime? EditedAt { get; set; }

    /// <summary>
    /// JSON-serialised <c>string[]</c> of user ids the author tagged with
    /// <c>@</c> in the body. Server-validated against the entity's
    /// participant list at write time so a client can't pull arbitrary
    /// users into a notification by spoofing this field. Empty / null
    /// when no mentions.
    /// </summary>
    public string? MentionedUserIdsJson { get; set; }

    /// <summary>
    /// OAuth client the message was written through (the token's <c>client_id</c>), when known —
    /// e.g. the work-item bridge posting a chat message on the author's behalf through the
    /// Service API. Carried on the conversation events so a subscriber can skip its own writes.
    /// </summary>
    public string? ClientId { get; set; }
}
