using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Oluso.Core.Api;
using Oluso.Core.Domain.Entities;
using Oluso.Core.Domain.Interfaces;
using Oluso.Core.Storage;
using Oluso.Dto.Files;

namespace Oluso.Controllers;

/// <summary>
/// Personal file storage for workspace users.
/// Files are private to the user unless explicitly attached to an entity.
/// Available to all authenticated users regardless of installed modules.
/// </summary>
[Route("api/workspace/my-files")]
public class WorkspaceMyFilesController : WorkspaceBaseController
{
    private readonly IFileAttachmentService _fileAttachmentService;
    private readonly IFileAttachmentStore _fileAttachmentStore;
    private readonly IFileUploader _fileUploader;

    private static readonly HashSet<string> AllowedContentTypes = new(StringComparer.OrdinalIgnoreCase)
    {
        "image/png", "image/jpeg", "image/gif", "image/webp",
        "application/pdf",
        "application/msword",
        "application/vnd.openxmlformats-officedocument.wordprocessingml.document",
        "application/vnd.ms-excel",
        "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet",
        "text/plain", "text/csv",
    };

    public WorkspaceMyFilesController(
        ITenantContext tenantContext,
        IFileAttachmentService fileAttachmentService,
        IFileAttachmentStore fileAttachmentStore,
        IFileUploader fileUploader)
        : base(tenantContext)
    {
        _fileAttachmentService = fileAttachmentService;
        _fileAttachmentStore = fileAttachmentStore;
        _fileUploader = fileUploader;
    }

    /// <summary>
    /// List personal files, optionally filtered by folder.
    /// </summary>
    [HttpGet]
    public async Task<ActionResult<IEnumerable<MyFileDto>>> ListFiles(
        [FromQuery] string? folder = null,
        CancellationToken cancellationToken = default)
    {
        var files = await _fileAttachmentStore.GetPersonalFilesAsync(UserId, folder, cancellationToken);
        return Ok(files.Select(MapToDto));
    }

    /// <summary>
    /// Get the user's folder list.
    /// </summary>
    [HttpGet("folders")]
    public async Task<ActionResult<IEnumerable<string>>> ListFolders(CancellationToken cancellationToken = default)
    {
        var folders = await _fileAttachmentStore.GetUserFoldersAsync(UserId, cancellationToken);
        return Ok(folders);
    }

    /// <summary>
    /// Upload a personal file.
    /// </summary>
    [HttpPost]
    public async Task<ActionResult<MyFileDto>> Upload(
        IFormFile file,
        [FromQuery] string? folder = null,
        CancellationToken cancellationToken = default)
    {
        if (file.Length == 0)
            return BadRequest(new { error = "File is empty" });

        if (file.Length > 10 * 1024 * 1024)
            return BadRequest(new { error = "File exceeds 10 MB limit" });

        if (!AllowedContentTypes.Contains(file.ContentType))
            return BadRequest(new { error = $"Content type '{file.ContentType}' is not allowed" });

        using var stream = file.OpenReadStream();
        var attachment = await _fileAttachmentService.UploadAsync(new UploadAttachmentRequest
        {
            TenantId = TenantId,
            UserId = UserId,
            UserName = UserName,
            FileName = file.FileName,
            ContentType = file.ContentType,
            Content = stream,
            Folder = folder?.Trim(),
        }, cancellationToken);

        return CreatedAtAction(nameof(Download), new { id = attachment.Id }, MapToDto(attachment));
    }

    /// <summary>
    /// Download a personal file.
    /// </summary>
    [HttpGet("{id}/download")]
    public async Task<IActionResult> Download(string id, CancellationToken cancellationToken = default)
    {
        var attachment = await _fileAttachmentStore.GetByIdAsync(id, cancellationToken);
        if (!IsOwnPersonalFile(attachment)) return NotFound();

        var result = await _fileAttachmentService.DownloadAsync(id, cancellationToken);
        if (result == null) return NotFound();

        return File(result.Content, result.Metadata.ContentType, result.Metadata.FileName);
    }

    /// <summary>
    /// Get a resolved URL for displaying a file (SAS for blob, proxied for local).
    /// </summary>
    [HttpGet("{id}/url")]
    public async Task<ActionResult<FileUrlResponse>> GetFileUrl(string id, CancellationToken cancellationToken = default)
    {
        var attachment = await _fileAttachmentStore.GetByIdAsync(id, cancellationToken);
        if (!IsOwnPersonalFile(attachment))
            return NotFound();

        var sasUrl = await _fileAttachmentService.GetReadUrlAsync(id, cancellationToken);

        return Ok(new FileUrlResponse
        {
            Url = sasUrl ?? $"/api/workspace/my-files/{id}/download",
            IsDirect = sasUrl != null,
        });
    }

    /// <summary>
    /// Delete a personal file.
    /// </summary>
    [HttpDelete("{id}")]
    public async Task<IActionResult> Delete(string id, CancellationToken cancellationToken = default)
    {
        var attachment = await _fileAttachmentStore.GetByIdAsync(id, cancellationToken);
        if (!IsOwnPersonalFile(attachment))
            return NotFound();

        await _fileAttachmentService.DeleteAsync(id, cancellationToken);
        return NoContent();
    }

    /// <summary>
    /// Move a file to a different folder.
    /// </summary>
    [HttpPatch("{id}/move")]
    public async Task<ActionResult<MyFileDto>> MoveToFolder(
        string id,
        [FromBody] MoveFileRequest request,
        CancellationToken cancellationToken = default)
    {
        var attachment = await _fileAttachmentStore.GetByIdAsync(id, cancellationToken);
        if (!IsOwnPersonalFile(attachment))
            return NotFound();

        attachment.Folder = request.Folder?.Trim();
        attachment.UpdatedBy = UserId;
        attachment.UpdatedAt = DateTime.UtcNow;

        await _fileAttachmentStore.UpdateAsync(attachment, cancellationToken);

        return Ok(MapToDto(attachment));
    }

    /// <summary>
    /// Only the uploader's personal files. Files attached to an entity are read, moved and deleted
    /// through the attachment endpoint, where the entity's access provider decides — having uploaded
    /// one does not grant access to it here.
    /// </summary>
    private bool IsOwnPersonalFile([System.Diagnostics.CodeAnalysis.NotNullWhen(true)] FileAttachment? attachment) =>
        attachment is { IsDeleted: false, EntityType: null } && attachment.UploadedByUserId == UserId;

    private static MyFileDto MapToDto(FileAttachment a) => new()
    {
        Id = a.Id,
        FileName = a.FileName,
        ContentType = a.ContentType,
        SizeBytes = a.SizeBytes,
        Folder = a.Folder,
        CreatedAt = a.CreatedAt,
    };
}

