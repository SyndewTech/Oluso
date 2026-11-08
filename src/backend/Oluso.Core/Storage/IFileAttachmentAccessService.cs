using Oluso.Core.Domain.Entities;

namespace Oluso.Core.Storage;

/// <summary>
/// The single access decision for entity-scoped files: asks the
/// <see cref="IFileAccessProvider"/> registered for the file's <c>EntityType</c>
/// and fails closed when there is none. Every endpoint that hands out a file's
/// bytes or a signed URL to it goes through here.
/// </summary>
public interface IFileAttachmentAccessService
{
    /// <summary>
    /// The decrypted view of <paramref name="file"/> when <paramref name="userId"/> may read it;
    /// null for personal files, deleted files, other tenants' files, files without a provider,
    /// and files the provider refuses.
    /// </summary>
    Task<FileAttachmentView?> AuthorizeReadAsync(
        string tenantId, string userId, FileAttachment file, CancellationToken cancellationToken = default);

    /// <summary>True when <paramref name="userId"/> may delete the entity-scoped <paramref name="file"/>.</summary>
    Task<bool> CanDeleteAsync(
        string tenantId, string userId, FileAttachment file, CancellationToken cancellationToken = default);

    /// <summary>
    /// True when <paramref name="storagePath"/> lies under a root the attachment pipeline writes to
    /// (the default <c>attachments/</c> layout or a provider's <see cref="IFileAccessProvider.StorageRoot"/>).
    /// Raw storage-path endpoints refuse these paths.
    /// </summary>
    bool IsAttachmentStoragePath(string storagePath);
}
