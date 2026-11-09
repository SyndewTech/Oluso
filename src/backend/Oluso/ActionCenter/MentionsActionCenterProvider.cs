using Oluso.Core.ActionCenter;
using Oluso.Core.Domain.Entities;
using Oluso.Core.Domain.Interfaces;
using Oluso.Core.Services;

namespace Oluso.ActionCenter;

/// <summary>
/// Surfaces unread <c>@mentions</c> across every conversation thread into
/// the unified Action Center inbox. The mention rows live in the platform
/// (any module that registered an <c>IConversationAuthorizer</c> can be
/// mentioned in), so this provider also lives in the platform — not in a
/// specific module.
///
/// <para>Deep links are resolved from <see cref="ConversationMessage.EntityType"/>
/// via a small switch. New mention-aware modules add their case here; the
/// fallback drops the user on the workspace home with no entity context so
/// they at least get a click target.</para>
///
/// <para>Each unread mention auto-acks when the user opens the underlying
/// thread (<see cref="Oluso.Controllers.WorkspaceConversationsController.List"/>
/// marks every visible mention as read), so the count drops back to zero
/// naturally without a separate "mark read" UX.</para>
/// </summary>
public sealed class MentionsActionCenterProvider : IActionCenterProvider
{
    private readonly IConversationMessageStore _messageStore;
    private readonly IOlusoUserService _userService;

    public MentionsActionCenterProvider(
        IConversationMessageStore messageStore,
        IOlusoUserService userService)
    {
        _messageStore = messageStore;
        _userService = userService;
    }

    public string ProviderId => "mentions";
    public string DisplayName => "Mentions";
    public string Icon => "at-symbol";

    public async Task<IReadOnlyList<ActionItem>> GetItemsForUserAsync(
        string tenantId, string userId, CancellationToken cancellationToken = default)
    {
        var mentions = await _messageStore.ListUnreadMentionsForUserAsync(
            tenantId, userId, limit: 50, cancellationToken);
        if (mentions.Count == 0) return Array.Empty<ActionItem>();

        // Resolve author display names in one pass — bounded by the limit
        // above, so no N+1 concern.
        var authorIds = mentions.Select(m => m.AuthorUserId)
            .Distinct(StringComparer.Ordinal)
            .ToArray();
        var nameLookup = new Dictionary<string, string?>(StringComparer.Ordinal);
        foreach (var id in authorIds)
        {
            var u = await _userService.FindByIdAsync(id, cancellationToken);
            nameLookup[id] = u?.DisplayName ?? u?.Email;
        }

        var items = new List<ActionItem>(mentions.Count);
        foreach (var m in mentions)
        {
            var authorName = nameLookup.TryGetValue(m.AuthorUserId, out var n) ? n : null;
            var (workspaceUrl, scope) = ResolveDeepLink(m.EntityType, m.EntityId);

            items.Add(new ActionItem
            {
                Id = m.Id,
                ProviderId = ProviderId,
                Title = authorName != null
                    ? $"{authorName} mentioned you"
                    : "You were mentioned",
                Subtitle = TruncateBody(m.Body),
                Icon = Icon,
                Category = "Mention",
                ActionUrl = workspaceUrl,
                WorkspaceActionUrl = workspaceUrl,
                Scope = scope,
                ReferenceId = m.Id,
                ReferenceType = "ConversationMessage",
                Priority = ActionItemPriority.Normal,
                Status = ActionItemStatus.Pending,
                CreatedAt = m.CreatedAt,
            });
        }
        return items;
    }

    public async Task<int> GetCountForUserAsync(
        string tenantId, string userId, CancellationToken cancellationToken = default)
    {
        var mentions = await _messageStore.ListUnreadMentionsForUserAsync(
            tenantId, userId, limit: 50, cancellationToken);
        return mentions.Count;
    }

    /// <summary>
    /// Maps <paramref name="entityType"/> to the workspace URL that renders
    /// the thread. <c>focusThread=1</c> is a hint the destination page can
    /// honour to scroll to / expand the conversation. New entity types add
    /// a case here.
    /// </summary>
    private static (string? workspaceUrl, ActionItemScope scope) ResolveDeepLink(
        string entityType, string entityId) => entityType switch
    {
        "approval.instance" =>
            ($"/approvals/submissions/{entityId}?focusThread=1", ActionItemScope.WorkspaceOnly),
        _ =>
            (null, ActionItemScope.Both),
    };

    private static string TruncateBody(string body)
    {
        if (string.IsNullOrEmpty(body)) return string.Empty;
        const int max = 120;
        return body.Length <= max ? body : body.Substring(0, max - 1) + "…";
    }
}
