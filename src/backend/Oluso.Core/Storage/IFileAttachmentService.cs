using Oluso.Core.Domain.Entities;

namespace Oluso.Core.Storage;

/// <summary>
/// Orchestrates file upload (blob storage) + metadata (database) in a single operation.
/// Controllers call this instead of IFileUploader directly for entity-associated files.
/// </summary>
public interface IFileAttachmentService
{
    /// <summary>
    /// Upload a file and create a database record.
    /// </summary>
    Task<FileAttachment> UploadAsync(UploadAttachmentRequest request, CancellationToken cancellationToken = default);

    /// <summary>
    /// Get the file content and metadata for download.
    /// Caller is responsible for access control (check entity access before calling this).
    /// </summary>
    Task<FileDownloadResult?> DownloadAsync(string attachmentId, CancellationToken cancellationToken = default);

    /// <summary>
    /// Get a time-limited read URL for the file.
    /// For blob storage, returns a SAS URL (client downloads directly from blob).
    /// For local storage, returns a server-proxied path.
    /// </summary>
    Task<string?> GetReadUrlAsync(string attachmentId, CancellationToken cancellationToken = default);

    /// <summary>
    /// Soft-delete an attachment (marks as deleted, does not remove blob).
    /// </summary>
    Task<bool> DeleteAsync(string attachmentId, CancellationToken cancellationToken = default);

    /// <summary>
    /// Hard-delete all attachments for an entity and clean up blob storage.
    /// </summary>
    Task HardDeleteByEntityAsync(string entityType, string entityId, CancellationToken cancellationToken = default);

    /// <summary>
    /// Removes every attachment of the entity in the tenant for good, soft-deleted ones too, as when the entity itself
    /// is deleted or erased: each file's stored content first, then its row, so a file whose content couldn't be
    /// removed keeps its row and a later purge finds it again. True when none is left.
    /// </summary>
    Task<bool> PurgeEntityAsync(string tenantId, string entityType, string entityId, CancellationToken cancellationToken = default);

    /// <summary>
    /// The tenants and entity ids that attachments of the entity type are kept on, a page at a time after
    /// <paramref name="after"/>, for sweeps that remove the files of entities that are gone. An entity type whose
    /// references are protected lists none, as its ids are held only encrypted.
    /// </summary>
    Task<IReadOnlyList<FileAttachmentOwner>> ListOwnersAsync(string entityType, FileAttachmentOwner? after, int take, CancellationToken cancellationToken = default);

    /// <summary>
    /// List attachments for an entity, transparently handling protected
    /// providers — the service computes the blind-index hash, queries
    /// by hash, and decrypts entity refs + filename for the caller.
    /// </summary>
    Task<IReadOnlyList<FileAttachmentView>> ListByEntityAsync(
        string tenantId, string entityType, string entityId,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Resolve a raw attachment row into a view with decrypted entity refs
    /// + filename. Safe to call on unprotected rows (returns plaintext as-is).
    /// </summary>
    Task<FileAttachmentView> ResolveAsync(
        FileAttachment attachment, CancellationToken cancellationToken = default);
}

/// <summary>
/// Decrypted view of a <see cref="FileAttachment"/>. Returned by the service
/// after entity-reference protection has been unwrapped. The raw entity
/// SHOULD NOT be returned past the service layer when it carries protected
/// references — the view ensures callers always see plaintext or nothing.
/// </summary>
public record FileAttachmentView(
    string Id,
    string? TenantId,
    string UploadedByUserId,
    string? UploadedByUserName,
    string StoragePath,
    FileStorageProvider StorageProvider,
    string FileName,
    string ContentType,
    long SizeBytes,
    string? ContentHash,
    string? Folder,
    string? EntityType,
    string? EntityId,
    string? SubEntityType,
    string? SubEntityId,
    bool IsProtected,
    string? ProtectionPurpose,
    DateTime CreatedAt);

public class UploadAttachmentRequest
{
    public string TenantId { get; set; } = null!;
    public string UserId { get; set; } = null!;
    public string? UserName { get; set; }

    // File data
    public string FileName { get; set; } = null!;
    public string ContentType { get; set; } = null!;
    public Stream Content { get; set; } = null!;

    // Organization
    public string? Folder { get; set; }

    // Entity association (null = personal file)
    public string? EntityType { get; set; }
    public string? EntityId { get; set; }
    public string? SubEntityType { get; set; }
    public string? SubEntityId { get; set; }
}

public class FileDownloadResult
{
    public Stream Content { get; set; } = null!;
    public FileAttachment Metadata { get; set; } = null!;
}
