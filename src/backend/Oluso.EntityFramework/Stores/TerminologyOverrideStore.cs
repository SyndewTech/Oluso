using Microsoft.EntityFrameworkCore;
using Oluso.Core.Terminology;

namespace Oluso.EntityFramework.Stores;

/// <summary>
/// Entity Framework implementation of the platform terminology override store
/// (backed by the platform <see cref="IOlusoDbContext"/> — terminology is a
/// platform concern, not any module's).
/// </summary>
public class TerminologyOverrideStore : ITerminologyOverrideStore
{
    private readonly IOlusoDbContext _context;

    public TerminologyOverrideStore(IOlusoDbContext context)
    {
        _context = context;
    }

    public async Task<IReadOnlyList<TerminologyOverride>> GetForTenantAsync(
        string tenantId, string locale, CancellationToken cancellationToken = default)
    {
        return await _context.TerminologyOverrides
            .Where(o => o.TenantId == tenantId && o.Locale == locale)
            .ToListAsync(cancellationToken);
    }

    public async Task<TerminologyOverride> UpsertAsync(
        TerminologyOverride entity, CancellationToken cancellationToken = default)
    {
        var existing = await _context.TerminologyOverrides.FirstOrDefaultAsync(
            o => o.TenantId == entity.TenantId && o.Locale == entity.Locale && o.Key == entity.Key,
            cancellationToken);

        if (existing == null)
        {
            entity.CreatedAt = DateTime.UtcNow;
            _context.TerminologyOverrides.Add(entity);
        }
        else
        {
            existing.Value = entity.Value;
            existing.UpdatedAt = DateTime.UtcNow;
            existing.UpdatedBy = entity.UpdatedBy ?? entity.CreatedBy;
            entity = existing;
        }

        await _context.SaveChangesAsync(cancellationToken);
        return entity;
    }

    public async Task<bool> DeleteAsync(
        string tenantId, string locale, string key, CancellationToken cancellationToken = default)
    {
        var existing = await _context.TerminologyOverrides.FirstOrDefaultAsync(
            o => o.TenantId == tenantId && o.Locale == locale && o.Key == key,
            cancellationToken);

        if (existing == null)
            return false;

        _context.TerminologyOverrides.Remove(existing);
        await _context.SaveChangesAsync(cancellationToken);
        return true;
    }
}
