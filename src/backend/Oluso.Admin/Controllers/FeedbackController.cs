using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Oluso.Core.Api;
using Oluso.Core.Domain.Entities;
using Oluso.Core.Domain.Interfaces;
using Oluso.Core.Events;
using Oluso.Core.Storage;
using Oluso.Admin.Dto.Feedback;

namespace Oluso.Admin.Controllers;

/// <summary>
/// Admin API for beta feedback. Anyone in the admin portal can submit it; the platform's super admins review it
/// across every tenant, since that's where end users file it from.
/// </summary>
[Route("api/admin/feedback")]
public class FeedbackController : AdminBaseController
{
    private readonly IFeedbackStore _feedbackStore;
    private readonly IFileUploader _fileUploader;
    private readonly IOlusoEventService _eventService;
    private readonly ITenantStore _tenantStore;

    public FeedbackController(
        IFeedbackStore feedbackStore,
        IFileUploader fileUploader,
        IOlusoEventService eventService,
        ITenantStore tenantStore,
        ITenantContext tenantContext) : base(tenantContext)
    {
        _feedbackStore = feedbackStore;
        _fileUploader = fileUploader;
        _eventService = eventService;
        _tenantStore = tenantStore;
    }

    /// <summary>
    /// Submit feedback from the admin portal
    /// </summary>
    [HttpPost]
    public async Task<ActionResult<FeedbackResponse>> Submit(
        [FromForm] FeedbackSubmitRequest request,
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
            SubmittedByUserId = RequireAdminUserId,
            SubmittedByEmail = AdminEmail,
            SubmittedByName = AdminUserName,
            Portal = "admin",
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
            Portal = "admin",
            SubmittedByUserId = feedback.SubmittedByUserId,
            SubmittedByEmail = feedback.SubmittedByEmail,
            Url = feedback.Url,
        }, cancellationToken);

        return Ok(ToResponse(feedback, null));
    }

    /// <summary>
    /// List feedback from every tenant (paginated, filterable by tenant, category and status)
    /// </summary>
    [HttpGet]
    [Authorize(Policy = "SuperAdmin")]
    public async Task<ActionResult<PagedResult<FeedbackResponse>>> List(
        [FromQuery] int page = 1,
        [FromQuery] int pageSize = 25,
        [FromQuery] string? category = null,
        [FromQuery] string? status = null,
        [FromQuery] string? tenantId = null,
        CancellationToken cancellationToken = default)
    {
        var (items, total) = await _feedbackStore.ListAsync(
            string.IsNullOrEmpty(tenantId) ? null : tenantId, page, pageSize, category, status, cancellationToken);
        var tenantNames = await TenantNamesAsync(items.Select(f => f.TenantId), cancellationToken);

        return Ok(new PagedResult<FeedbackResponse>
        {
            Items = items.Select(f => ToResponse(f, tenantNames.GetValueOrDefault(f.TenantId ?? ""))).ToList(),
            TotalCount = total,
            Page = page,
            PageSize = pageSize,
            TotalPages = (int)Math.Ceiling((double)total / pageSize),
        });
    }

    /// <summary>
    /// Get a single feedback submission
    /// </summary>
    [HttpGet("{id}")]
    [Authorize(Policy = "SuperAdmin")]
    public async Task<ActionResult<FeedbackResponse>> Get(string id, CancellationToken cancellationToken)
    {
        var feedback = await _feedbackStore.GetByIdAsync(null, id, cancellationToken);
        if (feedback == null) return NotFound();
        var tenantNames = await TenantNamesAsync([feedback.TenantId], cancellationToken);
        return Ok(ToResponse(feedback, tenantNames.GetValueOrDefault(feedback.TenantId ?? "")));
    }

    /// <summary>
    /// Update the status of a feedback submission
    /// </summary>
    [HttpPatch("{id}/status")]
    [Authorize(Policy = "SuperAdmin")]
    public async Task<IActionResult> UpdateStatus(string id, [FromBody] FeedbackStatusUpdate request, CancellationToken cancellationToken)
    {
        await _feedbackStore.UpdateStatusAsync(null, id, request.Status, cancellationToken);
        return NoContent();
    }

    /// <summary>
    /// Get a feedback screenshot
    /// </summary>
    [HttpGet("{id}/screenshot")]
    [Authorize(Policy = "SuperAdmin")]
    public async Task<IActionResult> GetScreenshot(string id, CancellationToken cancellationToken)
    {
        var feedback = await _feedbackStore.GetByIdAsync(null, id, cancellationToken);
        if (feedback?.ScreenshotPath == null || feedback.TenantId == null) return NotFound();

        // Served from storage rather than redirected: a redirect to a signed blob link expires, and the browser
        // refuses to follow it to the storage origin from an authenticated fetch.
        var path = FeedbackScreenshots.StoragePath(feedback.TenantId, feedback.ScreenshotPath);
        if (path == null) return NotFound();

        var bytes = await _fileUploader.DownloadAsync(path, cancellationToken);
        if (bytes == null) return NotFound();

        return File(bytes, "image/png");
    }

    private static byte[]? ParseDataUrl(string dataUrl)
    {
        // Expected format: data:image/png;base64,<base64data>
        const string marker = ";base64,";
        var idx = dataUrl.IndexOf(marker, StringComparison.Ordinal);
        if (idx < 0) return null;
        var base64 = dataUrl[(idx + marker.Length)..];
        try { return Convert.FromBase64String(base64); }
        catch { return null; }
    }

    private async Task<Dictionary<string, string>> TenantNamesAsync(IEnumerable<string?> tenantIds, CancellationToken cancellationToken)
    {
        var names = new Dictionary<string, string>();
        foreach (var id in tenantIds.OfType<string>().Distinct())
        {
            if (await _tenantStore.GetByIdAsync(id, cancellationToken) is { } tenant)
                names[id] = tenant.Name;
        }
        return names;
    }

    private static FeedbackResponse ToResponse(FeedbackSubmission f, string? tenantName) => new()
    {
        Id = f.Id,
        Title = f.Title,
        Description = f.Description,
        Category = f.Category,
        Url = f.Url,
        HasScreenshot = f.ScreenshotPath != null,
        ElementSelector = f.ElementSelector,
        UserAgent = f.UserAgent,
        ViewportSize = f.ViewportSize,
        Status = f.Status,
        SubmittedByUserId = f.SubmittedByUserId,
        SubmittedByEmail = f.SubmittedByEmail,
        SubmittedByName = f.SubmittedByName,
        Portal = f.Portal,
        TenantId = f.TenantId,
        TenantName = tenantName,
        CreatedAt = f.CreatedAt,
    };
}
