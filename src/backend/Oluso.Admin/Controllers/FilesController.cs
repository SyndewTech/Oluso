using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Options;
using Oluso.Core.Api;
using Oluso.Core.Domain.Entities;
using Oluso.Core.Domain.Interfaces;
using Oluso.Core.Storage;
using Oluso.Admin.Dto.Files;

namespace Oluso.Admin.Controllers;

/// <summary>
/// File upload API. Supports direct upload (local/blob) and
/// SAS-token–based client-side upload for blob storage.
/// </summary>
[Route("api/admin/files")]
public class FilesController : AdminBaseController
{
    private readonly IFileUploader _fileUploader;
    private readonly PublicAssetOptions _publicAssets;
    private readonly IPublicAssetPathRegistry _publicAssetPaths;
    private readonly IPublicImageVariantGenerator _variantGenerator;
    private readonly IFileAttachmentAccessService _attachmentAccess;

    private static readonly HashSet<string> AllowedContentTypes = new(StringComparer.OrdinalIgnoreCase)
    {
        "image/png", "image/jpeg", "image/gif", "image/webp", "image/svg+xml",
    };

    public FilesController(
        IFileUploader fileUploader,
        IOptions<PublicAssetOptions> publicAssets,
        IPublicAssetPathRegistry publicAssetPaths,
        IPublicImageVariantGenerator variantGenerator,
        IFileAttachmentAccessService attachmentAccess,
        ITenantContext tenantContext) : base(tenantContext)
    {
        _attachmentAccess = attachmentAccess;
        _fileUploader = fileUploader;
        _publicAssets = publicAssets.Value;
        _publicAssetPaths = publicAssetPaths;
        _variantGenerator = variantGenerator;
    }

    /// <summary>
    /// Upload a file directly. The server stores the file and returns an imageUrl.
    /// Works with both local and blob storage providers.
    /// </summary>
    [HttpPost("upload")]
    public async Task<ActionResult<FileUploadResponse>> Upload(
        IFormFile file,
        [FromQuery] string? folder = null,
        [FromQuery] string? subPath = null,
        CancellationToken cancellationToken = default)
    {
        if (file.Length == 0)
            return BadRequest(new { error = "File is empty" });

        if (file.Length > 10 * 1024 * 1024)
            return BadRequest(new { error = "File exceeds 10 MB limit" });

        if (!AllowedContentTypes.Contains(file.ContentType))
            return BadRequest(new { error = $"Content type '{file.ContentType}' is not allowed" });

        var ext = Path.GetExtension(file.FileName);
        var prefix = string.IsNullOrEmpty(folder) ? "uploads" : SanitizeSubPath(folder);
        if (prefix is null)
            return BadRequest(new { error = "Invalid folder" });
        if (_attachmentAccess.IsAttachmentStoragePath(prefix))
            return BadRequest(new { error = $"Folder '{prefix}' is reserved" });

        // Public vs private is derived from the folder allowlist — never a caller
        // toggle. Public assets keep the tenant high in the path, then an optional
        // caller subPath (e.g. a productId), then the file.
        var isPublic = _publicAssetPaths.IsPublic(prefix);
        var cleanSub = SanitizeSubPath(subPath);
        if (subPath is { Length: > 0 } && cleanSub is null)
            return BadRequest(new { error = "Invalid subPath" });

        var middle = cleanSub is null ? TenantId : $"{TenantId}/{cleanSub}";
        var storagePath = $"{prefix}/{middle}/{Guid.NewGuid()}{ext}";

        FileUploadResult result;
        if (isPublic)
        {
            // Buffer once so we can both upload the original and pre-generate
            // size variants from the same bytes.
            using var ms = new MemoryStream();
            await file.CopyToAsync(ms, cancellationToken);
            var bytes = ms.ToArray();

            result = await _fileUploader.UploadAsync(storagePath, bytes, FileAccessLevel.Public, file.ContentType, cancellationToken: cancellationToken);

            if (result.Success && _publicAssetPaths.GeneratesVariants(prefix))
                await _variantGenerator.GenerateAsync(storagePath, bytes, cancellationToken);
        }
        else
        {
            using var stream = file.OpenReadStream();
            result = await _fileUploader.UploadAsync(storagePath, stream, file.ContentType, cancellationToken: cancellationToken);
        }

        if (!result.Success)
            return StatusCode(500, new { error = result.Error ?? "Upload failed" });

        var imageUrl = isPublic
            ? BuildPublicUrl(storagePath, result)
            : await ResolveImageUrl(storagePath, result, cancellationToken);

        return Ok(new FileUploadResponse
        {
            ImageUrl = imageUrl,
            StoragePath = storagePath,
            Provider = _fileUploader.Provider.ToString(),
            SizeBytes = result.SizeBytes,
        });
    }

    /// <summary>
    /// The stable public URL for a public asset: the provider's direct/CDN URL
    /// (Azure public container) when available, else the app's anonymous serve
    /// endpoint (local/dev). Never a SAS — public URLs must not expire.
    /// </summary>
    private string BuildPublicUrl(string storagePath, FileUploadResult result)
    {
        if (!string.IsNullOrEmpty(result.PublicUrl))
            return result.PublicUrl;

        var baseUrl = _publicAssets.ProxyBaseUrl?.TrimEnd('/') ?? $"{Request.Scheme}://{Request.Host}";
        return $"{baseUrl}/api/files/raw/{storagePath}";
    }

    /// <summary>
    /// Uploads are stored as {folder}/{tenantId}/..., so a path is the caller's own when one of its folders is their
    /// tenant id. Dot segments are refused so a path can't step out of that folder.
    /// </summary>
    private bool IsInCallersTenant(string path)
    {
        var segments = path.Replace('\\', '/').Split('/', StringSplitOptions.RemoveEmptyEntries);
        if (segments.Any(seg => seg is "." or "..")) return false;
        return segments.Take(segments.Length - 1).Contains(TenantId, StringComparer.Ordinal);
    }

    /// <summary>
    /// Sanitize a caller-supplied sub-path (e.g. a productId). Rejects traversal
    /// and odd characters; returns null when the input is unusable.
    /// </summary>
    private static string? SanitizeSubPath(string? subPath)
    {
        if (string.IsNullOrWhiteSpace(subPath)) return null;
        var s = subPath.Replace('\\', '/').Trim('/');
        if (s.Length == 0 || s.Contains("..")) return null;
        foreach (var ch in s)
            if (!(char.IsLetterOrDigit(ch) || ch is '-' or '_' or '/')) return null;
        return s;
    }

    /// <summary>
    /// Get a pre-signed upload URL for client-side blob upload.
    /// The client PUTs the file directly to <c>uploadUrl</c>, then uses <c>imageUrl</c> to reference it.
    /// Returns 400 if the storage provider does not support signed URLs (e.g. local storage).
    /// </summary>
    [HttpPost("upload-url")]
    public async Task<ActionResult<SasUploadResponse>> GetUploadUrl(
        [FromBody] SasUploadRequest request,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(request.FileName))
            return BadRequest(new { error = "fileName is required" });

        var contentType = request.ContentType ?? "application/octet-stream";
        if (!AllowedContentTypes.Contains(contentType))
            return BadRequest(new { error = $"Content type '{contentType}' is not allowed" });

        var ext = Path.GetExtension(request.FileName);
        var prefix = string.IsNullOrEmpty(request.Folder) ? "uploads" : SanitizeSubPath(request.Folder);
        if (prefix is null)
            return BadRequest(new { error = "Invalid folder" });
        if (_attachmentAccess.IsAttachmentStoragePath(prefix))
            return BadRequest(new { error = $"Folder '{prefix}' is reserved" });
        var storagePath = $"{prefix}/{TenantId}/{Guid.NewGuid()}{ext}";

        var uploadUrl = await _fileUploader.GetSignedUrlAsync(
            storagePath,
            TimeSpan.FromMinutes(15),
            SignedUrlPermissions.Write,
            cancellationToken);

        if (string.IsNullOrEmpty(uploadUrl))
            return BadRequest(new { error = "Storage provider does not support signed upload URLs. Use POST /api/admin/files/upload instead." });

        var imageUrl = await _fileUploader.GetSignedUrlAsync(
            storagePath,
            TimeSpan.FromHours(24),
            SignedUrlPermissions.Read,
            cancellationToken);

        return Ok(new SasUploadResponse
        {
            UploadUrl = uploadUrl,
            ImageUrl = imageUrl ?? storagePath,
            StoragePath = storagePath,
            ExpiresInSeconds = 900,
        });
    }

    /// <summary>
    /// Serve a stored file. Used by local storage; blob storage clients should use the imageUrl directly.
    /// Files written by the attachment pipeline are not served here; their access provider decides
    /// who reads them through <c>api/workspace/file-attachments/{id}</c>.
    /// </summary>
    [HttpGet("{**path}")]
    public async Task<IActionResult> GetFile(string path, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(path) || _attachmentAccess.IsAttachmentStoragePath(path) || !IsInCallersTenant(path))
            return NotFound();

        var bytes = await _fileUploader.DownloadAsync(path, cancellationToken);
        if (bytes == null)
            return NotFound();

        var contentType = GetContentType(path);
        return File(bytes, contentType);
    }

    private async Task<string> ResolveImageUrl(string storagePath, FileUploadResult result, CancellationToken cancellationToken)
    {
        // For blob providers, try to get a read SAS URL
        if (_fileUploader.Provider != FileStorageProvider.Local)
        {
            if (!string.IsNullOrEmpty(result.PublicUrl))
                return result.PublicUrl;

            var sasUrl = await _fileUploader.GetSignedUrlAsync(
                storagePath, TimeSpan.FromHours(24), SignedUrlPermissions.Read, cancellationToken);
            if (!string.IsNullOrEmpty(sasUrl))
                return sasUrl;
        }

        // For local storage, return the serve endpoint
        return $"/api/admin/files/{storagePath}";
    }

    private static string GetContentType(string path) =>
        Path.GetExtension(path).ToLowerInvariant() switch
        {
            ".png" => "image/png",
            ".jpg" or ".jpeg" => "image/jpeg",
            ".gif" => "image/gif",
            ".webp" => "image/webp",
            ".svg" => "image/svg+xml",
            _ => "application/octet-stream",
        };
}
