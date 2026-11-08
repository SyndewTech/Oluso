namespace Oluso.Admin.Dto.Feedback;

public class FeedbackSubmitRequest
{
    public string Title { get; set; } = null!;
    public string Description { get; set; } = null!;
    public string? Category { get; set; }
    public string? Url { get; set; }
    public string? ElementSelector { get; set; }
    public string? ViewportSize { get; set; }
    public string? ScreenshotDataUrl { get; set; }
    /// <summary>Storage path of a screenshot already uploaded under feedback/{tenant}/.</summary>
    public string? ScreenshotPath { get; set; }
}

public class FeedbackStatusUpdate
{
    public string Status { get; set; } = null!;
}

public class FeedbackResponse
{
    public string Id { get; set; } = null!;
    public string Title { get; set; } = null!;
    public string Description { get; set; } = null!;
    public string Category { get; set; } = null!;
    public string? Url { get; set; }
    public bool HasScreenshot { get; set; }
    public string? ElementSelector { get; set; }
    public string? UserAgent { get; set; }
    public string? ViewportSize { get; set; }
    public string Status { get; set; } = null!;
    public string SubmittedByUserId { get; set; } = null!;
    public string? SubmittedByEmail { get; set; }
    public string? SubmittedByName { get; set; }
    public string Portal { get; set; } = null!;
    public string? TenantId { get; set; }
    public string? TenantName { get; set; }
    public DateTime CreatedAt { get; set; }
}
