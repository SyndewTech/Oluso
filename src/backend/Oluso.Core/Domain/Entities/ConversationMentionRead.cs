namespace Oluso.Core.Domain.Entities;

/// <summary>
/// One row per (message, user) acknowledging that the mentioned user has
/// seen the mention. The Action Center "Mentions" provider only surfaces
/// messages where this row is missing for the current user — opening the
/// thread auto-creates the row so the inbox count drops back to zero.
///
/// <para>Stored separately from <see cref="ConversationMessage"/> so the
/// "unread mentions for me" query is a straight anti-join, not a scan
/// over every message body's JSON mention list. Tenant-scoped so the
/// row honours the same global tenant filter as the message it points
/// at.</para>
/// </summary>
public class ConversationMentionRead : TenantEntity
{
    public string Id { get; set; } = Guid.NewGuid().ToString();

    /// <summary>The <see cref="ConversationMessage.Id"/> the user read.</summary>
    public string MessageId { get; set; } = null!;

    /// <summary>The user who saw the mention.</summary>
    public string UserId { get; set; } = null!;

    public DateTime ReadAt { get; set; } = DateTime.UtcNow;
}
