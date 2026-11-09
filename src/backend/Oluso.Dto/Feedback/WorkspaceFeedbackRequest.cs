namespace Oluso.Dto.Feedback;

public class WorkspaceFeedbackRequest
{
    public string Title { get; set; } = null!;
    public string Description { get; set; } = null!;
    public string? Category { get; set; }
    public string? Url { get; set; }
    public string? ElementSelector { get; set; }
    public string? ViewportSize { get; set; }
    /// <summary>
    /// Base64 data URI for the screenshot (alternative to multipart file upload).
    /// </summary>
    public string? ScreenshotDataUrl { get; set; }
    /// <summary>
    /// Storage path of a screenshot already uploaded under feedback/{tenant}/ (from /api/workspace/files).
    /// </summary>
    public string? ScreenshotPath { get; set; }
}
