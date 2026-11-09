using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Oluso.Core.Api;
using Oluso.Core.Domain.Entities;
using Oluso.Core.Domain.Interfaces;
using Oluso.Core.Events;
using Oluso.Core.Storage;
using Oluso.Dto.Feedback;

namespace Oluso.Controllers;

/// <summary>
/// Workspace API for submitting beta feedback. Authenticated users only.
/// </summary>
[Route("api/workspace/feedback")]
public class FeedbackSubmitController : WorkspaceBaseController
{
    private readonly IFeedbackStore _feedbackStore;
    private readonly IFileUploader _fileUploader;
    private readonly IOlusoEventService _eventService;

    public FeedbackSubmitController(
        IFeedbackStore feedbackStore,
        IFileUploader fileUploader,
        IOlusoEventService eventService,
        ITenantContext tenantContext) : base(tenantContext)
    {
        _feedbackStore = feedbackStore;
        _fileUploader = fileUploader;
        _eventService = eventService;
    }

    /// <summary>
    /// Submit feedback from the workspace portal
    /// </summary>
    [HttpPost]
    public async Task<ActionResult<WorkspaceFeedbackResponse>> Submit(
        [FromForm] WorkspaceFeedbackRequest request,
        [FromForm] IFormFile? screenshot,
        CancellationToken cancellationToken)
    {
        var feedback = new FeedbackSubmission
        {
            Title = request.Title,
            Description = request.Description,
            Category = request.Category ?? "bug",
            Url = request.Url,
            ElementSelector = request.ElementSelector,
            UserAgent = UserAgent,
            ViewportSize = request.ViewportSize,
            SubmittedByUserId = UserId,
            SubmittedByEmail = UserEmail,
            SubmittedByName = UserName,
            Portal = "workspace",
            TenantId = TenantId,
        };

        // Screenshot: prefer a pre-uploaded file, fall back to multipart file, then data URI.
        // The storage path is kept; the review page reads it back through the API.
        if (!string.IsNullOrEmpty(request.ScreenshotPath))
        {
            if (!FeedbackScreenshots.IsInTenantFolder(TenantId, request.ScreenshotPath))
                return BadRequest(new { error = "The screenshot must be uploaded to this tenant's feedback folder" });
            feedback.ScreenshotPath = request.ScreenshotPath;
        }
        else if (screenshot is { Length: > 0 })
        {
            var path = $"{FeedbackScreenshots.Folder(TenantId)}{feedback.Id}/screenshot.png";
            using var stream = screenshot.OpenReadStream();
            var result = await _fileUploader.UploadAsync(path, stream, "image/png", cancellationToken: cancellationToken);
            if (result.Success)
                feedback.ScreenshotPath = path;
        }
        else if (!string.IsNullOrEmpty(request.ScreenshotDataUrl))
        {
            var bytes = ParseDataUrl(request.ScreenshotDataUrl);
            if (bytes != null)
            {
                var path = $"{FeedbackScreenshots.Folder(TenantId)}{feedback.Id}/screenshot.png";
                var result = await _fileUploader.UploadAsync(path, bytes, "image/png", cancellationToken: cancellationToken);
                if (result.Success)
                    feedback.ScreenshotPath = path;
            }
        }

        await _feedbackStore.CreateAsync(feedback, cancellationToken);

        await _eventService.RaiseAsync(new FeedbackSubmittedEvent
        {
            TenantId = TenantId,
            FeedbackId = feedback.Id,
            Title = feedback.Title,
            FeedbackCategory = feedback.Category,
            Portal = "workspace",
            SubmittedByUserId = feedback.SubmittedByUserId,
            SubmittedByEmail = feedback.SubmittedByEmail,
            Url = feedback.Url,
        }, cancellationToken);

        return Ok(new WorkspaceFeedbackResponse
        {
            Id = feedback.Id,
            CreatedAt = feedback.CreatedAt,
        });
    }

    private static byte[]? ParseDataUrl(string dataUrl)
    {
        const string marker = ";base64,";
        var idx = dataUrl.IndexOf(marker, StringComparison.Ordinal);
        if (idx < 0) return null;
        var base64 = dataUrl[(idx + marker.Length)..];
        try { return Convert.FromBase64String(base64); }
        catch { return null; }
    }
}

