namespace Oluso.Core.ActionCenter;

/// <summary>
/// An item that requires user attention — approval, task, assignment, notification
/// </summary>
public class ActionItem
{
    public string Id { get; set; } = null!;
    public string ProviderId { get; set; } = null!;
    public string Title { get; set; } = null!;
    public string? Subtitle { get; set; }
    public string? Description { get; set; }
    public string Icon { get; set; } = "bell";
    public string Category { get; set; } = "General";
    /// <summary>Fallback URL used when no portal-specific URL is set.</summary>
    public string? ActionUrl { get; set; }
    /// <summary>URL to navigate to when viewed from the admin portal.</summary>
    public string? AdminActionUrl { get; set; }
    /// <summary>URL to navigate to when viewed from the workspace portal.</summary>
    public string? WorkspaceActionUrl { get; set; }
    public string? ReferenceId { get; set; }
    public string? ReferenceType { get; set; }
    public ActionItemPriority Priority { get; set; } = ActionItemPriority.Normal;
    public ActionItemStatus Status { get; set; } = ActionItemStatus.Pending;
    public DateTime CreatedAt { get; set; }
    public DateTime? DueDate { get; set; }
    /// <summary>
    /// Where this item should be displayed. Defaults to Both.
    /// Use AdminOnly for items whose ActionUrl is in the admin app.
    /// </summary>
    public ActionItemScope Scope { get; set; } = ActionItemScope.Both;
}

public enum ActionItemScope { Both, WorkspaceOnly, AdminOnly }

public enum ActionItemPriority { Low, Normal, High, Urgent }

public enum ActionItemStatus { Pending, Overdue, Info }

/// <summary>
/// Modules implement this to contribute action items to the unified inbox
/// </summary>
public interface IActionCenterProvider
{
    string ProviderId { get; }
    string DisplayName { get; }
    string Icon { get; }
    /// <summary>
    /// Optional workspace route the "view all" group link navigates to. Each
    /// module supplies its own landing route here so the shared UI never
    /// hardcodes module routes. Null = the group is not navigable.
    /// </summary>
    string? WorkspaceActionUrl => null;
    Task<IReadOnlyList<ActionItem>> GetItemsForUserAsync(string tenantId, string userId, CancellationToken cancellationToken = default);
    Task<int> GetCountForUserAsync(string tenantId, string userId, CancellationToken cancellationToken = default);
}

/// <summary>
/// Aggregates action items from all registered providers
/// </summary>
public interface IActionCenterService
{
    Task<ActionCenterResult> GetItemsForUserAsync(string tenantId, string userId, CancellationToken cancellationToken = default);
    Task<int> GetTotalCountAsync(string tenantId, string userId, CancellationToken cancellationToken = default);
}

public class ActionCenterResult
{
    public int TotalCount { get; set; }
    public List<ActionCenterGroup> Groups { get; set; } = new();
}

public class ActionCenterGroup
{
    public string ProviderId { get; set; } = null!;
    public string DisplayName { get; set; } = null!;
    public string Icon { get; set; } = null!;
    public string? WorkspaceActionUrl { get; set; }
    public int Count { get; set; }
    public List<ActionItem> Items { get; set; } = new();
}
