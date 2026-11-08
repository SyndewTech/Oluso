namespace Oluso.Core.Domain.Entities;

/// <summary>
/// Beta feedback submission from users in admin or workspace portals
/// </summary>
public class FeedbackSubmission : AuditableTenantEntity
{
    public string Id { get; set; } = Guid.NewGuid().ToString();
    public string Title { get; set; } = null!;
    public string Description { get; set; } = null!;
    public string Category { get; set; } = "bug"; // bug | feature | note
    public string? Url { get; set; }
    public string? ScreenshotPath { get; set; }
    public string? ElementSelector { get; set; }
    public string? UserAgent { get; set; }
    public string? ViewportSize { get; set; }
    public string Status { get; set; } = "new"; // new | reviewed | resolved | dismissed
    public string SubmittedByUserId { get; set; } = null!;
    public string? SubmittedByEmail { get; set; }
    public string? SubmittedByName { get; set; }
    public string Portal { get; set; } = "workspace"; // workspace | admin
}
