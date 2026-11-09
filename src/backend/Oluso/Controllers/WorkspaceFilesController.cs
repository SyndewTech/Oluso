using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Oluso.Core.Api;
using Oluso.Core.Domain.Entities;
using Oluso.Core.Domain.Interfaces;
using Oluso.Core.Storage;
using Oluso.Dto.Files;

namespace Oluso.Controllers;

/// <summary>
/// File upload API for workspace (self-service) users.
/// Mirrors the admin FilesController but scoped to authenticated workspace users.
/// </summary>
[Route("api/workspace/files")]
public class WorkspaceFilesController : WorkspaceBaseController
{
    private readonly IFileUploader _fileUploader;
    private readonly IFileAttachmentAccessService _attachmentAccess;

    private static readonly HashSet<string> AllowedContentTypes = new(StringComparer.OrdinalIgnoreCase)
    {
        "image/png", "image/jpeg", "image/gif", "image/webp",
        "application/pdf",
    };

    public WorkspaceFilesController(
        IFileUploader fileUploader,
        IFileAttachmentAccessService attachmentAccess,
        ITenantContext tenantContext) : base(tenantContext)
    {
        _fileUploader = fileUploader;
        _attachmentAccess = attachmentAccess;
    }

    /// <summary>
    /// Upload a file. Returns an imageUrl that can be used to reference the file.
    /// </summary>
    [HttpPost("upload")]
    public async Task<ActionResult<WorkspaceFileUploadResponse>> Upload(
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

        var ext = Path.GetExtension(file.FileName);
        var prefix = string.IsNullOrEmpty(folder) ? "uploads" : folder.Trim('/');
        if (_attachmentAccess.IsAttachmentStoragePath(prefix))
            return BadRequest(new { error = $"Folder '{prefix}' is reserved" });
        var storagePath = $"{prefix}/{TenantId}/{UserId}/{Guid.NewGuid()}{ext}";

        using var stream = file.OpenReadStream();
        var result = await _fileUploader.UploadAsync(storagePath, stream, file.ContentType, cancellationToken: cancellationToken);

        if (!result.Success)
            return StatusCode(500, new { error = result.Error ?? "Upload failed" });

        var imageUrl = await ResolveUrl(storagePath, result, cancellationToken);

        return Ok(new WorkspaceFileUploadResponse
        {
            ImageUrl = imageUrl,
            FileName = file.FileName,
            SizeBytes = result.SizeBytes,
            StoragePath = storagePath,
        });
    }

    /// <summary>
    /// Get a pre-signed upload URL for client-side direct-to-blob upload.
    /// The client PUTs the file directly to <c>uploadUrl</c>, then uses <c>imageUrl</c> to reference it.
    /// Returns 400 if the storage provider does not support signed URLs (e.g. local storage).
    /// </summary>
    [HttpPost("upload-url")]
    public async Task<ActionResult<WorkspaceSasUploadResponse>> GetUploadUrl(
        [FromBody] WorkspaceSasUploadRequest request,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(request.FileName))
            return BadRequest(new { error = "fileName is required" });

        var contentType = request.ContentType ?? "application/octet-stream";
        if (!AllowedContentTypes.Contains(contentType))
            return BadRequest(new { error = $"Content type '{contentType}' is not allowed" });

        var ext = Path.GetExtension(request.FileName);
        var prefix = string.IsNullOrEmpty(request.Folder) ? "uploads" : request.Folder.Trim('/');
        if (_attachmentAccess.IsAttachmentStoragePath(prefix))
            return BadRequest(new { error = $"Folder '{prefix}' is reserved" });
        var storagePath = $"{prefix}/{TenantId}/{UserId}/{Guid.NewGuid()}{ext}";

        var uploadUrl = await _fileUploader.GetSignedUrlAsync(
            storagePath,
            TimeSpan.FromMinutes(15),
            SignedUrlPermissions.Write,
            cancellationToken);

        if (string.IsNullOrEmpty(uploadUrl))
            return BadRequest(new { error = "Storage provider does not support signed upload URLs. Use POST /api/workspace/files/upload instead." });

        var imageUrl = await _fileUploader.GetSignedUrlAsync(
            storagePath,
            TimeSpan.FromHours(24),
            SignedUrlPermissions.Read,
            cancellationToken);

        return Ok(new WorkspaceSasUploadResponse
        {
            UploadUrl = uploadUrl,
            ImageUrl = imageUrl ?? $"/api/workspace/files/{storagePath}",
            StoragePath = storagePath,
            ExpiresInSeconds = 900,
        });
    }

    /// <summary>
    /// Serve a stored file.
    /// Workspace users may only access files within their own tenant.
    /// Files under a user-specific subfolder (e.g. work-order-items/{tenant}/{userId}/...)
    /// are restricted to that user unless accessed via the admin endpoint.
    /// Files written by the attachment pipeline are never served here — they are
    /// read through <c>api/workspace/file-attachments/{id}</c>, which asks the
    /// entity's access provider.
    /// </summary>
    [HttpGet("{**path}")]
    public async Task<IActionResult> GetFile(string path, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(path))
            return NotFound();

        if (!IsPathAllowed(path) || _attachmentAccess.IsAttachmentStoragePath(path))
            return NotFound(); // Return 404, not 403, to avoid leaking path existence

        var bytes = await _fileUploader.DownloadAsync(path, cancellationToken);
        if (bytes == null)
            return NotFound();

        var contentType = GetContentType(path);
        return File(bytes, contentType);
    }

    /// <summary>
    /// Checks that the requested file path belongs to the current user's tenant
    /// and, if it contains a user-specific segment, that it matches the current user.
    /// Path format: {folder}/{tenantId}/{userId}/{file} or {folder}/{tenantId}/{file}
    /// </summary>
    private bool IsPathAllowed(string path)
    {
        var segments = path.Replace('\\', '/').TrimStart('/').Split('/', StringSplitOptions.RemoveEmptyEntries);

        // Minimum: folder/tenantId/file (3 segments)
        if (segments.Length < 3)
            return false;

        var pathTenantId = segments[1];

        // Must match current tenant
        if (!string.Equals(pathTenantId, TenantId, StringComparison.OrdinalIgnoreCase))
            return false;

        // If path has a user segment (folder/tenant/userId/file = 4+ segments),
        // workspace users can only access their own files
        if (segments.Length >= 4)
        {
            var pathUserId = segments[2];
            // Check if it looks like a user ID (GUID format) — if so, enforce ownership
            if (Guid.TryParse(pathUserId, out _) &&
                !string.Equals(pathUserId, UserId, StringComparison.OrdinalIgnoreCase))
            {
                return false;
            }
        }

        return true;
    }

    private async Task<string> ResolveUrl(string storagePath, FileUploadResult result, CancellationToken cancellationToken)
    {
        if (_fileUploader.Provider != FileStorageProvider.Local)
        {
            if (!string.IsNullOrEmpty(result.PublicUrl))
                return result.PublicUrl;

            var sasUrl = await _fileUploader.GetSignedUrlAsync(
                storagePath, TimeSpan.FromHours(24), SignedUrlPermissions.Read, cancellationToken);
            if (!string.IsNullOrEmpty(sasUrl))
                return sasUrl;
        }

        return $"/api/workspace/files/{storagePath}";
    }

    private static string GetContentType(string path) =>
        Path.GetExtension(path).ToLowerInvariant() switch
        {
            ".png" => "image/png",
            ".jpg" or ".jpeg" => "image/jpeg",
            ".gif" => "image/gif",
            ".webp" => "image/webp",
            ".pdf" => "application/pdf",
            _ => "application/octet-stream",
        };
}

