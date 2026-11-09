namespace Oluso.Dto.Conversations;

public class ConversationMessageDto
{
    public string Id { get; set; } = null!;
    public string EntityType { get; set; } = null!;
    public string EntityId { get; set; } = null!;
    public string AuthorUserId { get; set; } = null!;
    public string? AuthorName { get; set; }
    /// <summary>Null when the message has been deleted — the UI renders a placeholder.</summary>
    public string? Body { get; set; }
    public DateTime CreatedAt { get; set; }
    public DateTime? EditedAt { get; set; }
    public DateTime? DeletedAt { get; set; }
    /// <summary>User ids tagged with <c>@</c> in this message. Frontend highlights them in the body.</summary>
    public List<string>? MentionedUserIds { get; set; }
}
