using Microsoft.EntityFrameworkCore;
using Oluso.Enterprise.Scim.Entities;
using Oluso.Enterprise.Scim.Stores;

namespace Oluso.Enterprise.Scim.EntityFramework;

/// <summary>
/// Entity Framework implementation of IScimResourceMappingStore
/// </summary>
public class ScimResourceMappingStore : IScimResourceMappingStore
{
    private readonly ScimDbContext _context;

    public ScimResourceMappingStore(ScimDbContext context)
    {
        _context = context;
    }

    public async Task<ScimResourceMapping?> GetByExternalIdAsync(
        string scimClientId,
        string resourceType,
        string externalId,
        CancellationToken cancellationToken = default)
    {
        return await _context.ScimResourceMappings
            .FirstOrDefaultAsync(x =>
                x.ScimClientId == scimClientId &&
                x.ResourceType == resourceType &&
                x.ExternalId == externalId,
                cancellationToken);
    }

    public async Task<ScimResourceMapping?> GetByInternalIdAsync(
        string scimClientId,
        string resourceType,
        string internalId,
        CancellationToken cancellationToken = default)
    {
        return await _context.ScimResourceMappings
            .FirstOrDefaultAsync(x =>
                x.ScimClientId == scimClientId &&
                x.ResourceType == resourceType &&
                x.InternalId == internalId,
                cancellationToken);
    }

    public async Task<ScimResourceMapping> CreateAsync(ScimResourceMapping mapping, CancellationToken cancellationToken = default)
    {
        _context.ScimResourceMappings.Add(mapping);
        await _context.SaveChangesAsync(cancellationToken);
        return mapping;
    }

    public async Task DeleteAsync(string id, CancellationToken cancellationToken = default)
    {
        var mapping = await _context.ScimResourceMappings
            .FirstOrDefaultAsync(x => x.Id == id, cancellationToken);

        if (mapping != null)
        {
            _context.ScimResourceMappings.Remove(mapping);
            await _context.SaveChangesAsync(cancellationToken);
        }
    }

    public async Task DeleteByInternalIdAsync(string resourceType, string internalId, CancellationToken cancellationToken = default)
    {
        await _context.ScimResourceMappings
            .Where(x => x.ResourceType == resourceType && x.InternalId == internalId)
            .ExecuteDeleteAsync(cancellationToken);
    }
}
