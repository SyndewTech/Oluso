using Microsoft.EntityFrameworkCore;
using Oluso.Enterprise.Scim.Entities;
using Oluso.Enterprise.Scim.Stores;

namespace Oluso.Enterprise.Scim.EntityFramework;

/// <summary>
/// Entity Framework implementation of IScimAttributeMappingStore
/// </summary>
public class ScimAttributeMappingStore : IScimAttributeMappingStore
{
    private readonly ScimDbContext _context;

    public ScimAttributeMappingStore(ScimDbContext context)
    {
        _context = context;
    }

    public async Task<ScimAttributeMapping?> GetByIdAsync(string id, CancellationToken cancellationToken = default)
    {
        return await _context.ScimAttributeMappings
            .FirstOrDefaultAsync(x => x.Id == id, cancellationToken);
    }

    public async Task<IReadOnlyList<ScimAttributeMapping>> GetByClientAsync(string scimClientId, CancellationToken cancellationToken = default)
    {
        return await _context.ScimAttributeMappings
            .Where(x => x.ScimClientId == scimClientId)
            .OrderBy(x => x.Priority)
            .ThenBy(x => x.ScimAttribute)
            .ToListAsync(cancellationToken);
    }

    public async Task<ScimAttributeMapping> CreateAsync(ScimAttributeMapping mapping, CancellationToken cancellationToken = default)
    {
        _context.ScimAttributeMappings.Add(mapping);
        await _context.SaveChangesAsync(cancellationToken);
        return mapping;
    }

    public async Task UpdateAsync(ScimAttributeMapping mapping, CancellationToken cancellationToken = default)
    {
        _context.ScimAttributeMappings.Update(mapping);
        await _context.SaveChangesAsync(cancellationToken);
    }

    public async Task DeleteAsync(string id, CancellationToken cancellationToken = default)
    {
        var mapping = await _context.ScimAttributeMappings
            .FirstOrDefaultAsync(x => x.Id == id, cancellationToken);

        if (mapping != null)
        {
            _context.ScimAttributeMappings.Remove(mapping);
            await _context.SaveChangesAsync(cancellationToken);
        }
    }

    public async Task DeleteByClientAsync(string scimClientId, CancellationToken cancellationToken = default)
    {
        await _context.ScimAttributeMappings
            .Where(x => x.ScimClientId == scimClientId)
            .ExecuteDeleteAsync(cancellationToken);
    }
}
