using Microsoft.Extensions.Logging;
using Oluso.Core.Domain.Entities;
using Oluso.Core.Domain.Interfaces;
using Oluso.Core.Storage;

namespace Oluso.Storage;

public class FileAttachmentService : IFileAttachmentService
{
    /// <summary>First segment of the default storage layout, used when the provider builds no path of its own.</summary>
    public const string DefaultStorageRoot = "attachments";

    private readonly IFileUploader _fileUploader;
    private readonly IFileAttachmentStore _store;
    private readonly IEnumerable<IFileAccessProvider> _accessProviders;
    private readonly IEntityReferenceProtector _protector;
    private readonly ILogger<FileAttachmentService> _logger;

    public FileAttachmentService(
        IFileUploader fileUploader,
        IFileAttachmentStore store,
        IEnumerable<IFileAccessProvider> accessProviders,
        IEntityReferenceProtector protector,
        ILogger<FileAttachmentService> logger)
    {
        _fileUploader = fileUploader;
        _store = store;
        _accessProviders = accessProviders;
        _protector = protector;
        _logger = logger;
    }

    public async Task<FileAttachment> UploadAsync(UploadAttachmentRequest request, CancellationToken cancellationToken = default)
    {
        var attachmentId = Guid.NewGuid().ToString();
        var ext = Path.GetExtension(request.FileName);
        var provider = FindProvider(request.EntityType);

        // Storage path: provider may override; fall back to the default layout.
        var storagePath = provider?.BuildStoragePath(request, attachmentId, ext)
            ?? $"{DefaultStorageRoot}/{request.TenantId}/{request.EntityType ?? "personal"}/{attachmentId}{ext}";

        var result = await _fileUploader.UploadAsync(
            storagePath, request.Content, request.ContentType, cancellationToken: cancellationToken);

        if (!result.Success)
            throw new InvalidOperationException($"File upload failed: {result.Error}");

        var attachment = new FileAttachment
        {
            Id = attachmentId,
            TenantId = request.TenantId,
            UploadedByUserId = request.UserId,
            UploadedByUserName = request.UserName,
            StoragePath = storagePath,
            StorageProvider = _fileUploader.Provider,
            FileName = request.FileName,
            ContentType = request.ContentType,
            SizeBytes = result.SizeBytes,
            ContentHash = result.ContentHash,
            Folder = request.Folder,
            EntityType = request.EntityType,
            EntityId = request.EntityId,
            SubEntityType = request.SubEntityType,
            SubEntityId = request.SubEntityId,
            CreatedBy = request.UserId,
        };

        // Entity-reference protection: when the provider opts in, the shared
        // FileAttachments row loses its plaintext entityId / filename — only
        // the blind-index hash links it back, and only holders of the tenant's
        // data-protection key can recover the values.
        if (provider?.ProtectsEntityReferences == true)
        {
            if (string.IsNullOrWhiteSpace(provider.ProtectionPurpose))
                throw new InvalidOperationException(
                    $"Provider '{provider.EntityType}' enables ProtectsEntityReferences but did not set ProtectionPurpose.");

            attachment.ProtectionPurpose = provider.ProtectionPurpose;

            if (!string.IsNullOrEmpty(request.EntityId))
            {
                attachment.EntityIdHash = await _protector.BlindIndexAsync(
                    request.TenantId, provider.ProtectionPurpose, request.EntityId, cancellationToken);
                attachment.EntityIdProtected = await _protector.ProtectAsync(
                    request.TenantId, provider.ProtectionPurpose, request.EntityId, cancellationToken);
                attachment.EntityId = null;
            }
            if (!string.IsNullOrEmpty(request.SubEntityId))
            {
                attachment.SubEntityIdHash = await _protector.BlindIndexAsync(
                    request.TenantId, provider.ProtectionPurpose, request.SubEntityId, cancellationToken);
                attachment.SubEntityIdProtected = await _protector.ProtectAsync(
                    request.TenantId, provider.ProtectionPurpose, request.SubEntityId, cancellationToken);
                attachment.SubEntityId = null;
            }

            // Filename: a name like "offer-letter-2025.pdf" can be sensitive; encrypt
            // it and put a non-identifying placeholder in the plaintext column,
            // preserving the extension for content-type routing.
            attachment.FileNameProtected = await _protector.ProtectAsync(
                request.TenantId, provider.ProtectionPurpose, request.FileName, cancellationToken);
            attachment.FileName = $"protected-{attachmentId}{ext}";
        }

        await _store.CreateAsync(attachment, cancellationToken);

        _logger.LogDebug("Created file attachment {Id} for {EntityType} (protected={Protected})",
            attachment.Id, request.EntityType ?? "personal", attachment.IsProtected);

        return attachment;
    }

    public async Task<FileDownloadResult?> DownloadAsync(string attachmentId, CancellationToken cancellationToken = default)
    {
        var attachment = await _store.GetByIdAsync(attachmentId, cancellationToken);
        if (attachment == null) return null;

        var stream = await _fileUploader.DownloadStreamAsync(attachment.StoragePath, cancellationToken);
        if (stream == null) return null;

        return new FileDownloadResult { Content = stream, Metadata = attachment };
    }

    public async Task<string?> GetReadUrlAsync(string attachmentId, CancellationToken cancellationToken = default)
    {
        var attachment = await _store.GetByIdAsync(attachmentId, cancellationToken);
        if (attachment == null) return null;

        // For blob storage, generate a short-lived SAS read URL
        if (_fileUploader.Provider != FileStorageProvider.Local)
        {
            var sasUrl = await _fileUploader.GetSignedUrlAsync(
                attachment.StoragePath, TimeSpan.FromHours(1), SignedUrlPermissions.Read, cancellationToken);
            if (!string.IsNullOrEmpty(sasUrl)) return sasUrl;
        }

        // For local storage, return a path that the controller will proxy
        return null;
    }

    public async Task<bool> DeleteAsync(string attachmentId, CancellationToken cancellationToken = default)
    {
        return await _store.SoftDeleteAsync(attachmentId, cancellationToken);
    }

    public async Task HardDeleteByEntityAsync(string entityType, string entityId, CancellationToken cancellationToken = default)
    {
        // HardDeleteByEntity runs from background cleanup paths; the store
        // matches on `EntityId == X OR EntityIdHash == X` so this works for
        // both plaintext and protected rows when the plaintext id is passed.
        // Callers that hold the plaintext entityId but need to wipe protected
        // rows for a specific tenant should call HardDeleteForTenantAsync —
        // exposed below — which computes the hash explicitly.
        var storagePaths = await _store.HardDeleteByEntityAsync(entityType, entityId, cancellationToken);

        foreach (var path in storagePaths)
        {
            try
            {
                await _fileUploader.DeleteAsync(path, cancellationToken);
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Failed to delete blob at {Path} during entity cleanup", path);
            }
        }
    }

    public async Task<bool> PurgeEntityAsync(string tenantId, string entityType, string entityId, CancellationToken cancellationToken = default)
    {
        var provider = FindProvider(entityType);
        var hashes = provider?.ProtectsEntityReferences == true
            ? await _protector.BlindIndexCandidatesAsync(tenantId, provider.ProtectionPurpose, entityId, cancellationToken)
            : [];
        var rows = await _store.GetAllForEntityAsync(tenantId, entityType, entityId, hashes, cancellationToken);

        var cleared = true;
        foreach (var row in rows)
        {
            try
            {
                // Some stores report a failed delete as not found, so a file still there afterwards wasn't removed.
                if (!await _fileUploader.DeleteAsync(row.StoragePath, cancellationToken) && await _fileUploader.ExistsAsync(row.StoragePath, cancellationToken))
                {
                    _logger.LogWarning("File {Path} of attachment {Id} could not be removed; its row is kept for a later purge", row.StoragePath, row.Id);
                    cleared = false;
                    continue;
                }
                await _store.HardDeleteAsync(row.Id, cancellationToken);
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                _logger.LogWarning(ex, "Attachment {Id} at {Path} could not be removed; it is kept for a later purge", row.Id, row.StoragePath);
                cleared = false;
            }
        }
        return cleared;
    }

    public async Task<IReadOnlyList<FileAttachmentOwner>> ListOwnersAsync(string entityType, FileAttachmentOwner? after, int take, CancellationToken cancellationToken = default)
    {
        if (FindProvider(entityType)?.ProtectsEntityReferences == true) return [];
        return await _store.GetOwnersAsync(entityType, after, take, cancellationToken);
    }

    public async Task<IReadOnlyList<FileAttachmentView>> ListByEntityAsync(
        string tenantId, string entityType, string entityId, CancellationToken cancellationToken = default)
    {
        var provider = FindProvider(entityType);
        IReadOnlyList<FileAttachment> rows;

        if (provider?.ProtectsEntityReferences == true)
        {
            var hashes = await _protector.BlindIndexCandidatesAsync(tenantId, provider.ProtectionPurpose, entityId, cancellationToken);
            rows = await _store.GetByEntityHashAsync(entityType, hashes, cancellationToken);
        }
        else
        {
            rows = await _store.GetByEntityAsync(entityType, entityId, cancellationToken);
        }

        var views = new List<FileAttachmentView>(rows.Count);
        foreach (var row in rows)
            views.Add(await ResolveAsync(row, cancellationToken));
        return views;
    }

    public async Task<FileAttachmentView> ResolveAsync(
        FileAttachment attachment, CancellationToken cancellationToken = default)
    {
        var entityId = attachment.EntityId;
        var subEntityId = attachment.SubEntityId;
        var fileName = attachment.FileName;

        if (attachment.IsProtected && attachment.TenantId != null && attachment.ProtectionPurpose != null)
        {
            entityId = await _protector.UnprotectNullableAsync(
                attachment.TenantId, attachment.ProtectionPurpose, attachment.EntityIdProtected, cancellationToken);
            subEntityId = await _protector.UnprotectNullableAsync(
                attachment.TenantId, attachment.ProtectionPurpose, attachment.SubEntityIdProtected, cancellationToken);
            if (!string.IsNullOrEmpty(attachment.FileNameProtected))
                fileName = await _protector.UnprotectAsync(
                    attachment.TenantId, attachment.ProtectionPurpose, attachment.FileNameProtected, cancellationToken);
        }

        return new FileAttachmentView(
            attachment.Id, attachment.TenantId, attachment.UploadedByUserId, attachment.UploadedByUserName,
            attachment.StoragePath, attachment.StorageProvider,
            fileName, attachment.ContentType, attachment.SizeBytes, attachment.ContentHash,
            attachment.Folder, attachment.EntityType, entityId,
            attachment.SubEntityType, subEntityId,
            attachment.IsProtected, attachment.ProtectionPurpose,
            attachment.CreatedAt);
    }

    private IFileAccessProvider? FindProvider(string? entityType) =>
        string.IsNullOrEmpty(entityType)
            ? null
            : _accessProviders.FirstOrDefault(p => p.EntityType == entityType);
}
