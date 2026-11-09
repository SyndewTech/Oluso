using Microsoft.EntityFrameworkCore;
using Oluso.Core.Domain.Entities;
using Oluso.Core.Domain.Interfaces;

namespace Oluso.EntityFramework.Stores;

public class TenantConfigurationSectionStore : ITenantConfigurationSectionStore
{
    private readonly IOlusoDbContext _context;

    public TenantConfigurationSectionStore(IOlusoDbContext context)
    {
        _context = context;
    }

    public async Task<TenantConfigurationSection?> GetAsync(string tenantId, string sectionKey, CancellationToken cancellationToken = default)
    {
        return await _context.TenantConfigurationSections
            .FirstOrDefaultAsync(s => s.TenantId == tenantId && s.SectionKey == sectionKey, cancellationToken);
    }

    public async Task<IReadOnlyList<TenantConfigurationSection>> GetAllAsync(string tenantId, CancellationToken cancellationToken = default)
    {
        return await _context.TenantConfigurationSections
            .Where(s => s.TenantId == tenantId)
            .ToListAsync(cancellationToken);
    }

    public async Task UpsertAsync(TenantConfigurationSection section, CancellationToken cancellationToken = default)
    {
        var existing = await _context.TenantConfigurationSections
            .FirstOrDefaultAsync(s => s.TenantId == section.TenantId && s.SectionKey == section.SectionKey, cancellationToken);

        if (existing != null)
        {
            existing.Value = section.Value;
            existing.IsEncrypted = section.IsEncrypted;
            existing.Updated = DateTime.UtcNow;
        }
        else
        {
            section.Created = DateTime.UtcNow;
            section.Updated = DateTime.UtcNow;
            _context.TenantConfigurationSections.Add(section);
        }

        await _context.SaveChangesAsync(cancellationToken);
    }

    public async Task RemoveAsync(string tenantId, string sectionKey, CancellationToken cancellationToken = default)
    {
        var section = await _context.TenantConfigurationSections
            .FirstOrDefaultAsync(s => s.TenantId == tenantId && s.SectionKey == sectionKey, cancellationToken);

        if (section != null)
        {
            _context.TenantConfigurationSections.Remove(section);
            await _context.SaveChangesAsync(cancellationToken);
        }
    }
}
