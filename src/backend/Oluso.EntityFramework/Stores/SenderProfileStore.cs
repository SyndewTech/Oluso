using Microsoft.EntityFrameworkCore;
using Oluso.Core.Domain.Entities;
using Oluso.Core.Domain.Interfaces;

namespace Oluso.EntityFramework.Stores;

public class SenderProfileStore : ISenderProfileStore
{
    private readonly IOlusoDbContext _context;

    public SenderProfileStore(IOlusoDbContext context)
    {
        _context = context;
    }

    public Task<SenderProfile?> GetAsync(string tenantId, string id, CancellationToken cancellationToken = default)
    {
        return _context.SenderProfiles
            .FirstOrDefaultAsync(s => s.TenantId == tenantId && s.Id == id, cancellationToken);
    }

    public Task<SenderProfile?> GetDefaultAsync(string tenantId, CancellationToken cancellationToken = default)
    {
        return _context.SenderProfiles
            .Where(s => s.TenantId == tenantId && s.IsDefault && s.IsActive)
            .FirstOrDefaultAsync(cancellationToken);
    }

    public async Task<IReadOnlyList<SenderProfile>> ListAsync(string tenantId, CancellationToken cancellationToken = default)
    {
        return await _context.SenderProfiles
            .Where(s => s.TenantId == tenantId)
            .OrderByDescending(s => s.IsDefault)
            .ThenBy(s => s.Name)
            .ToListAsync(cancellationToken);
    }

    public async Task CreateAsync(SenderProfile profile, CancellationToken cancellationToken = default)
    {
        profile.CreatedAt = DateTime.UtcNow;
        _context.SenderProfiles.Add(profile);
        await _context.SaveChangesAsync(cancellationToken);
    }

    public async Task UpdateAsync(SenderProfile profile, CancellationToken cancellationToken = default)
    {
        profile.UpdatedAt = DateTime.UtcNow;
        _context.SenderProfiles.Update(profile);
        await _context.SaveChangesAsync(cancellationToken);
    }

    public async Task DeleteAsync(string tenantId, string id, CancellationToken cancellationToken = default)
    {
        var existing = await _context.SenderProfiles
            .FirstOrDefaultAsync(s => s.TenantId == tenantId && s.Id == id, cancellationToken);

        if (existing == null)
            return;

        _context.SenderProfiles.Remove(existing);
        await _context.SaveChangesAsync(cancellationToken);
    }

    public async Task SetDefaultAsync(string tenantId, string id, CancellationToken cancellationToken = default)
    {
        var target = await _context.SenderProfiles
            .FirstOrDefaultAsync(s => s.TenantId == tenantId && s.Id == id, cancellationToken);

        if (target == null)
            throw new InvalidOperationException($"SenderProfile {id} not found for tenant {tenantId}.");

        var currentDefaults = await _context.SenderProfiles
            .Where(s => s.TenantId == tenantId && s.IsDefault && s.Id != id)
            .ToListAsync(cancellationToken);

        var now = DateTime.UtcNow;
        foreach (var other in currentDefaults)
        {
            other.IsDefault = false;
            other.UpdatedAt = now;
        }

        target.IsDefault = true;
        target.UpdatedAt = now;

        await _context.SaveChangesAsync(cancellationToken);
    }
}
