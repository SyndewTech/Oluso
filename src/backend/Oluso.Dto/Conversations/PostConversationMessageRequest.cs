namespace Oluso.Dto.Conversations;

public class PostConversationMessageRequest
{
    public string Body { get; set; } = null!;

    /// <summary>
    /// User ids the author tagged with <c>@</c>. The server validates
    /// each id against the entity's participant list and silently drops
    /// any that don't belong — clients can't use this to spam arbitrary
    /// users with mention notifications.
    /// </summary>
    public List<string>? MentionedUserIds { get; set; }
}
