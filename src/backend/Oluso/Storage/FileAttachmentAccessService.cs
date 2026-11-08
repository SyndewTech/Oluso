using Oluso.Core.Domain.Entities;
using Oluso.Core.Storage;

namespace Oluso.Storage;

public class FileAttachmentAccessService : IFileAttachmentAccessService
{
    private readonly IFileAttachmentService _attachments;
    private readonly IEnumerable<IFileAccessProvider> _providers;

    public FileAttachmentAccessService(IFileAttachmentService attachments, IEnumerable<IFileAccessProvider> providers)
    {
        _attachments = attachments;
        _providers = providers;
    }

    public async Task<FileAttachmentView?> AuthorizeReadAsync(
        string tenantId, string userId, FileAttachment file, CancellationToken cancellationToken = default)
    {
        if (file.IsDeleted || file.TenantId != tenantId) return null;
        var provider = FindProvider(file.EntityType);
        if (provider == null) return null;

        var view = await _attachments.ResolveAsync(file, cancellationToken);
        if (string.IsNullOrEmpty(view.EntityId)) return null;

        return await provider.CanReadFileAsync(tenantId, userId, view, cancellationToken) ? view : null;
    }

    public async Task<bool> CanDeleteAsync(
        string tenantId, string userId, FileAttachment file, CancellationToken cancellationToken = default)
    {
        if (file.IsDeleted || file.TenantId != tenantId) return false;
        var provider = FindProvider(file.EntityType);
        if (provider == null) return false;

        var view = await _attachments.ResolveAsync(file, cancellationToken);
        return await provider.CanDeleteFileAsync(tenantId, userId, file, view, cancellationToken);
    }

    public bool IsAttachmentStoragePath(string storagePath)
    {
        var root = storagePath.Replace('\\', '/').TrimStart('/').Split('/', 2)[0];
        if (string.Equals(root, FileAttachmentService.DefaultStorageRoot, StringComparison.OrdinalIgnoreCase)) return true;
        return _providers.Any(p => !string.IsNullOrEmpty(p.StorageRoot)
            && string.Equals(root, p.StorageRoot, StringComparison.OrdinalIgnoreCase));
    }

    private IFileAccessProvider? FindProvider(string? entityType) =>
        string.IsNullOrEmpty(entityType)
            ? null
            : _providers.FirstOrDefault(p => string.Equals(p.EntityType, entityType, StringComparison.Ordinal));
}
