using Oluso.Core.Domain.Entities;

namespace Oluso.Core.Storage;

/// <summary>
/// Per-entity-type authorization for files stored via <see cref="IFileAttachmentService"/>.
///
/// <para>The generic attachment endpoint calls into the registered provider
/// for the file's <c>EntityType</c> on every read, signed-URL and delete request. If no
/// provider is registered for an entity type, the controller fails closed
/// — adding a new module cannot accidentally leak files because the framework
/// demands an explicit access decision.</para>
///
/// <para>Modules register their provider via DI: <c>services.AddScoped&lt;IFileAccessProvider, MyProvider&gt;()</c>.
/// The controller fans out across <c>IEnumerable&lt;IFileAccessProvider&gt;</c>
/// and matches by <see cref="EntityType"/>.</para>
/// </summary>
public interface IFileAccessProvider
{
    /// <summary>
    /// The <c>FileAttachment.EntityType</c> this provider authorizes for.
    /// Returning a value here is the opt-in step — only registered entity
    /// types can be read/deleted via the generic controller.
    /// </summary>
    string EntityType { get; }

    /// <summary>
    /// Build the blob-storage path for a newly uploaded file. Implementations
    /// can pick a custom prefix (e.g. <c>sensitive/{tenantId}/…</c>) so operators
    /// can apply distinct IAM / CMK / lifecycle policies to that prefix. The
    /// returned path SHOULD NOT contain the plaintext entity ID — derive a
    /// shard from it (HMAC, hash) instead so the path itself doesn't leak.
    /// <para>Returning <c>null</c> falls back to the default layout
    /// (<c>attachments/{tenantId}/{entityType}/{guid}{ext}</c>).</para>
    /// </summary>
    string? BuildStoragePath(UploadAttachmentRequest request, string attachmentId, string extension) => null;

    /// <summary>
    /// The first segment of every path <see cref="BuildStoragePath"/> returns
    /// (e.g. <c>sensitive</c> for <c>sensitive/{tenantId}/…</c>). Raw storage-path
    /// endpoints refuse paths under it, so the files are reachable only through
    /// the attachment endpoint that asks this provider. <c>null</c> when the
    /// provider keeps the default layout.
    /// </summary>
    string? StorageRoot => null;

    /// <summary>
    /// When <c>true</c>, the <c>FileAttachmentService</c> writes
    /// <c>EntityId</c>, <c>SubEntityId</c> and <c>FileName</c> through the
    /// platform's <c>IEntityReferenceProtector</c> before persisting. Reads
    /// decrypt for the caller. A read-only observer of the shared
    /// <c>FileAttachments</c> table cannot recover the protected entity ID without
    /// the tenant's data-protection key.
    /// </summary>
    bool ProtectsEntityReferences => false;

    /// <summary>
    /// Key bucket the <c>IEntityReferenceProtector</c> queries when
    /// <see cref="ProtectsEntityReferences"/> is on. Examples: <c>"sensitive"</c>,
    /// <c>"confidential"</c>. Each purpose has its own <c>SigningKey</c>
    /// row per tenant, so rotation / revocation can be scoped narrowly.
    /// Must be non-empty when <see cref="ProtectsEntityReferences"/> is true.
    /// </summary>
    string ProtectionPurpose => string.Empty;

    /// <summary>
    /// True if <paramref name="userId"/> may read the attachment.
    /// <paramref name="subEntityType"/> / <paramref name="subEntityId"/> let
    /// the provider apply contextual rules — e.g. the originating document
    /// request can grant the requester read access even when they don't have
    /// blanket entity-file access.
    /// </summary>
    Task<bool> CanReadAsync(
        string tenantId,
        string userId,
        string entityId,
        string? subEntityType,
        string? subEntityId,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// True if <paramref name="userId"/> may read this specific file. Called by
    /// the attachment endpoint for every download and signed-URL request, with
    /// the entity references already decrypted. Providers override it when the
    /// decision or its audit trail needs the file itself.
    /// </summary>
    Task<bool> CanReadFileAsync(
        string tenantId,
        string userId,
        FileAttachmentView file,
        CancellationToken cancellationToken = default)
        => string.IsNullOrEmpty(file.EntityId)
            ? Task.FromResult(false)
            : CanReadAsync(tenantId, userId, file.EntityId, file.SubEntityType, file.SubEntityId, cancellationToken);

    /// <summary>
    /// True if <paramref name="userId"/> may delete the attachment. Called by
    /// the attachment endpoint with the entity references already decrypted in
    /// <paramref name="view"/>.
    /// </summary>
    Task<bool> CanDeleteFileAsync(
        string tenantId,
        string userId,
        FileAttachment file,
        FileAttachmentView view,
        CancellationToken cancellationToken = default)
        => CanDeleteAsync(tenantId, userId, file, cancellationToken);

    /// <summary>
    /// True if <paramref name="userId"/> may delete the attachment. Default
    /// expectation is <b>false</b> — entity-scoped files are typically audit
    /// artifacts that survive the request that produced them. Providers opt
    /// in narrowly (e.g. a draft-state escape hatch, or a compliance-purge
    /// permission). The framework will never delete an entity-scoped file
    /// without a provider returning <c>true</c> here.
    /// </summary>
    Task<bool> CanDeleteAsync(
        string tenantId,
        string userId,
        FileAttachment file,
        CancellationToken cancellationToken = default);
}
