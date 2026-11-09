namespace Oluso.Core.Domain.Entities;

/// <summary>
/// A file uploaded to the system, optionally associated with a parent entity.
///
/// Access rules:
/// - Personal files (EntityType is null): only the uploader can see them
/// - Entity-attached files: anyone who can access the parent entity can see them
/// </summary>
public class FileAttachment : AuditableTenantEntity
{
    public string Id { get; set; } = Guid.NewGuid().ToString();

    // Who uploaded this file
    public string UploadedByUserId { get; set; } = null!;
    public string? UploadedByUserName { get; set; }

    // Storage
    public string StoragePath { get; set; } = null!;
    public FileStorageProvider StorageProvider { get; set; }

    // File metadata
    public string FileName { get; set; } = null!;
    public string ContentType { get; set; } = "application/octet-stream";
    public long SizeBytes { get; set; }
    public string? ContentHash { get; set; }

    // Organization — user-defined folder for personal files (e.g. "Receipts", "Contracts")
    public string? Folder { get; set; }

    // Polymorphic entity association (null = personal/unattached file).
    //
    // <para>For most entity types these are stored as plaintext. When the
    // registered <c>IFileAccessProvider</c> opts in to entity-reference
    // protection (e.g. sensitive files), <see cref="EntityId"/> /
    // <see cref="SubEntityId"/> are set to <c>null</c> and the real values
    // live in <see cref="EntityIdHash"/> (for equality lookups) and
    // <see cref="EntityIdProtected"/> (for display after decrypt).</para>
    public string? EntityType { get; set; }
    public string? EntityId { get; set; }

    // Optional sub-entity (e.g. WorkOrderItem within a WorkOrder)
    public string? SubEntityType { get; set; }
    public string? SubEntityId { get; set; }

    // ─── Entity-reference protection (opt-in per provider) ──────────────────
    //
    // Set when the file's <c>IFileAccessProvider</c> declares
    // <c>ProtectsEntityReferences = true</c>. Lets sensitive files
    // hide the join key from anyone with read access to the shared
    // FileAttachments table — only holders of the tenant's data-protection
    // key can recover the protected entity ID or filename.

    /// <summary>HMAC of the entity ID (deterministic, indexed) for equality lookups.</summary>
    public string? EntityIdHash { get; set; }

    /// <summary>AES-GCM ciphertext of the entity ID (random nonce; recoverable).</summary>
    public string? EntityIdProtected { get; set; }

    /// <summary>HMAC of the sub-entity ID.</summary>
    public string? SubEntityIdHash { get; set; }

    /// <summary>AES-GCM ciphertext of the sub-entity ID.</summary>
    public string? SubEntityIdProtected { get; set; }

    /// <summary>AES-GCM ciphertext of the original filename (the plaintext <see cref="FileName"/> is sanitised when protection is on).</summary>
    public string? FileNameProtected { get; set; }

    /// <summary>
    /// Protection-key purpose used at upload time (e.g. <c>"sensitive"</c>). Records
    /// which key bucket must be queried to decrypt — stays correct even after
    /// a provider's protection config changes.
    /// </summary>
    public string? ProtectionPurpose { get; set; }

    /// <summary>True when this attachment has its entity references encrypted.</summary>
    public bool IsProtected => !string.IsNullOrEmpty(ProtectionPurpose);

    // Soft delete
    public bool IsDeleted { get; set; }
    public DateTime? DeletedAt { get; set; }
}
