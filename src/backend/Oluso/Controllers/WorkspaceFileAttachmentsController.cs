using Microsoft.AspNetCore.Mvc;
using Oluso.Core.Api;
using Oluso.Core.Domain.Entities;
using Oluso.Core.Domain.Interfaces;
using Oluso.Core.Storage;
using Oluso.Dto.Files;

namespace Oluso.Controllers;

/// <summary>
/// Generic file-attachment proxy keyed by <see cref="FileAttachment.Id"/>.
/// Resolves the row, asks <see cref="IFileAttachmentAccessService"/> (and through
/// it the <see cref="IFileAccessProvider"/> for the file's <c>EntityType</c>)
/// whether the caller may read or delete it,
/// then either redirects to a freshly-minted SAS URL (blob backends) or
/// streams the bytes (local).
///
/// <para>This is the canonical URL surface for entity-scoped files. Modules
/// don't add per-domain proxy endpoints — they implement
/// <see cref="IFileAccessProvider"/> and register it. Default-deny: an
/// attachment whose <c>EntityType</c> has no registered provider returns 404
/// on read and 403 on delete, so the framework can't accidentally leak files.</para>
///
/// <para>Personal files (<c>EntityType == null</c>) are intentionally not
/// reachable here — those are served by <c>WorkspaceMyFilesController</c>
/// where the owner-only rule is hard-coded. This endpoint refuses them.</para>
///
/// <para>Note: separate from the legacy <c>WorkspaceFilesController</c> at
/// <c>/api/workspace/files/*</c>, which uses a storage-path-as-URL model
/// (used by module document uploads). The two coexist — new code
/// uses this endpoint, legacy callers keep working. The legacy endpoint
/// refuses paths the attachment pipeline writes, so these files have no
/// route around the provider check.</para>
/// </summary>
[Route("api/workspace/file-attachments")]
public class WorkspaceFileAttachmentsController : WorkspaceBaseController
{
    private readonly IFileAttachmentService _attachmentService;
    private readonly IFileAttachmentAccessService _access;
    private readonly IFileAttachmentStore _store;
    private readonly IFileUploader _fileUploader;

    public WorkspaceFileAttachmentsController(
        ITenantContext tenantContext,
        IFileAttachmentService attachmentService,
        IFileAttachmentAccessService access,
        IFileAttachmentStore store,
        IFileUploader fileUploader)
        : base(tenantContext)
    {
        _attachmentService = attachmentService;
        _access = access;
        _store = store;
        _fileUploader = fileUploader;
    }

    [HttpGet("{attachmentId}")]
    public async Task<IActionResult> Get(string attachmentId, CancellationToken cancellationToken)
    {
        var file = await _store.GetByIdAsync(attachmentId, cancellationToken);
        if (file == null) return NotFound();

        var view = await _access.AuthorizeReadAsync(TenantId, UserId, file, cancellationToken);
        if (view == null) return NotFound();

        return await DeliverAsync(file, view, cancellationToken);
    }

    [HttpGet("{attachmentId}/access-url")]
    public async Task<ActionResult<AccessUrlDto>> GetAccessUrl(
        string attachmentId, CancellationToken cancellationToken)
    {
        var file = await _store.GetByIdAsync(attachmentId, cancellationToken);
        if (file == null) return NotFound();

        var view = await _access.AuthorizeReadAsync(TenantId, UserId, file, cancellationToken);
        if (view == null) return NotFound();

        var sas = await _attachmentService.GetReadUrlAsync(file.Id, cancellationToken);
        return Ok(new AccessUrlDto
        {
            Url = string.IsNullOrEmpty(sas) ? null : sas,
            FileName = view.FileName,
            ContentType = view.ContentType,
        });
    }

    [HttpDelete("{attachmentId}")]
    public async Task<IActionResult> Delete(string attachmentId, CancellationToken cancellationToken)
    {
        var file = await _store.GetByIdAsync(attachmentId, cancellationToken);
        if (file == null || file.TenantId != TenantId || file.IsDeleted) return NotFound();

        if (!await _access.CanDeleteAsync(TenantId, UserId, file, cancellationToken)) return Forbid();

        await _attachmentService.DeleteAsync(attachmentId, cancellationToken);
        return NoContent();
    }

    /// <summary>
    /// For SAS-capable backends we 302 to the signed URL so the browser
    /// fetches directly from blob storage (no traffic through us); for local
    /// storage we stream the bytes. The fresh SAS expiry is set in
    /// <c>FileAttachmentService.GetReadUrlAsync</c> (1 hour as of writing).
    /// </summary>
    private async Task<IActionResult> DeliverAsync(FileAttachment file, FileAttachmentView view, CancellationToken cancellationToken)
    {
        var sas = await _attachmentService.GetReadUrlAsync(file.Id, cancellationToken);
        if (!string.IsNullOrEmpty(sas)) return Redirect(sas);

        var bytes = await _fileUploader.DownloadAsync(file.StoragePath, cancellationToken);
        if (bytes == null) return NotFound();

        return File(bytes, view.ContentType ?? "application/octet-stream", view.FileName);
    }
}
