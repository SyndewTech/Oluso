using Microsoft.EntityFrameworkCore;
using Oluso.Core.Domain.Entities;
using Oluso.Core.Domain.Interfaces;
using Oluso.Core.Storage;

namespace Oluso.EntityFramework.Stores;

public class FileAttachmentStore : IFileAttachmentStore
{
    private readonly IOlusoDbContext _context;

    public FileAttachmentStore(IOlusoDbContext context)
    {
        _context = context;
    }

    public async Task<FileAttachment> CreateAsync(FileAttachment attachment, CancellationToken cancellationToken = default)
    {
        _context.FileAttachments.Add(attachment);
        await _context.SaveChangesAsync(cancellationToken);
        return attachment;
    }

    public async Task<FileAttachment> UpdateAsync(FileAttachment attachment, CancellationToken cancellationToken = default)
    {
        _context.FileAttachments.Update(attachment);
        await _context.SaveChangesAsync(cancellationToken);
        return attachment;
    }

    public async Task<FileAttachment?> GetByIdAsync(string id, CancellationToken cancellationToken = default)
    {
        return await _context.FileAttachments.FirstOrDefaultAsync(f => f.Id == id, cancellationToken);
    }

    public async Task<IReadOnlyList<FileAttachment>> GetByEntityAsync(
        string entityType, string entityId, CancellationToken cancellationToken = default)
    {
        return await _context.FileAttachments
            .Where(f => f.EntityType == entityType && f.EntityId == entityId)
            .OrderByDescending(f => f.CreatedAt)
            .ToListAsync(cancellationToken);
    }

    public async Task<IReadOnlyList<FileAttachment>> GetByEntityHashAsync(
        string entityType, IReadOnlyCollection<string> entityIdHashes, CancellationToken cancellationToken = default)
    {
        if (entityIdHashes.Count == 0) return [];
        return await _context.FileAttachments
            .Where(f => f.EntityType == entityType && f.EntityIdHash != null && entityIdHashes.Contains(f.EntityIdHash))
            .OrderByDescending(f => f.CreatedAt)
            .ToListAsync(cancellationToken);
    }

    public async Task<IReadOnlyList<FileAttachment>> GetBySubEntityAsync(
        string entityType, string entityId,
        string subEntityType, string subEntityId, CancellationToken cancellationToken = default)
    {
        return await _context.FileAttachments
            .Where(f => f.EntityType == entityType && f.EntityId == entityId
                        && f.SubEntityType == subEntityType && f.SubEntityId == subEntityId)
            .OrderByDescending(f => f.CreatedAt)
            .ToListAsync(cancellationToken);
    }

    public async Task<IReadOnlyList<FileAttachment>> GetPersonalFilesAsync(
        string userId, string? folder = null, CancellationToken cancellationToken = default)
    {
        var query = _context.FileAttachments
            .Where(f => f.UploadedByUserId == userId && f.EntityType == null);

        if (folder != null)
            query = query.Where(f => f.Folder == folder);

        return await query
            .OrderByDescending(f => f.CreatedAt)
            .ToListAsync(cancellationToken);
    }

    public async Task<IReadOnlyList<string>> GetUserFoldersAsync(string userId, CancellationToken cancellationToken = default)
    {
        return await _context.FileAttachments
            .Where(f => f.UploadedByUserId == userId && f.EntityType == null && f.Folder != null)
            .Select(f => f.Folder!)
            .Distinct()
            .OrderBy(f => f)
            .ToListAsync(cancellationToken);
    }

    public async Task<bool> SoftDeleteAsync(string id, CancellationToken cancellationToken = default)
    {
        var attachment = await _context.FileAttachments.FirstOrDefaultAsync(f => f.Id == id, cancellationToken);
        if (attachment == null) return false;

        attachment.IsDeleted = true;
        attachment.DeletedAt = DateTime.UtcNow;
        await _context.SaveChangesAsync(cancellationToken);
        return true;
    }

    public async Task<IReadOnlyList<string>> HardDeleteByEntityAsync(
        string entityType, string entityId, CancellationToken cancellationToken = default)
    {
        // Matches both plaintext-keyed and protected (hash-keyed) rows. The
        // caller is expected to pass the plaintext entityId; protected
        // matching happens via the hash column when the row has IsProtected.
        var attachments = await _context.FileAttachments
            .IgnoreQueryFilters()
            .Where(f => f.EntityType == entityType
                        && (f.EntityId == entityId || f.EntityIdHash == entityId))
            .ToListAsync(cancellationToken);

        var paths = attachments.Select(a => a.StoragePath).ToList();
        _context.FileAttachments.RemoveRange(attachments);
        await _context.SaveChangesAsync(cancellationToken);
        return paths;
    }

    public async Task<IReadOnlyList<FileAttachment>> GetAllForEntityAsync(
        string tenantId, string entityType, string entityId, IReadOnlyCollection<string> entityIdHashes, CancellationToken cancellationToken = default)
    {
        return await _context.FileAttachments
            .IgnoreQueryFilters()
            .Where(f => f.TenantId == tenantId && f.EntityType == entityType
                        && (f.EntityId == entityId || (f.EntityIdHash != null && entityIdHashes.Contains(f.EntityIdHash))))
            .ToListAsync(cancellationToken);
    }

    public async Task<bool> HardDeleteAsync(string id, CancellationToken cancellationToken = default)
    {
        return await _context.FileAttachments.IgnoreQueryFilters().Where(f => f.Id == id).ExecuteDeleteAsync(cancellationToken) > 0;
    }

    public async Task<IReadOnlyList<FileAttachmentOwner>> GetOwnersAsync(
        string entityType, FileAttachmentOwner? after, int take, CancellationToken cancellationToken = default)
    {
        var query = _context.FileAttachments
            .IgnoreQueryFilters()
            .Where(f => f.EntityType == entityType && f.TenantId != null && f.EntityId != null);
        if (after != null)
        {
            var (tenantId, entityId) = (after.TenantId, after.EntityId);
            query = query.Where(f => string.Compare(f.TenantId, tenantId) > 0
                                     || (f.TenantId == tenantId && string.Compare(f.EntityId, entityId) > 0));
        }

        var owners = await query
            .Select(f => new { f.TenantId, f.EntityId })
            .Distinct()
            .OrderBy(f => f.TenantId).ThenBy(f => f.EntityId)
            .Take(take)
            .ToListAsync(cancellationToken);
        return owners.Select(o => new FileAttachmentOwner(o.TenantId!, o.EntityId!)).ToList();
    }
}
