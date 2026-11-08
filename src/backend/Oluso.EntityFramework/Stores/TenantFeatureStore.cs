using Microsoft.EntityFrameworkCore;
using Oluso.Core.Domain.Entities;
using Oluso.Core.Domain.Interfaces;

namespace Oluso.EntityFramework.Stores;

public class TenantFeatureStore : ITenantFeatureStore
{
    private readonly OlusoDbContext _context;

    public TenantFeatureStore(OlusoDbContext context)
    {
        _context = context;
    }

    public async Task<IReadOnlyList<TenantFeature>> GetFeaturesAsync(string tenantId, CancellationToken cancellationToken = default)
    {
        return await _context.TenantFeatures
            .Where(f => f.TenantId == tenantId)
            .OrderBy(f => f.FeatureKey)
            .ToListAsync(cancellationToken);
    }

    public async Task<bool> IsFeatureEnabledAsync(string tenantId, string featureKey, CancellationToken cancellationToken = default)
    {
        var feature = await _context.TenantFeatures
            .FirstOrDefaultAsync(f => f.TenantId == tenantId && f.FeatureKey == featureKey, cancellationToken);

        if (feature == null)
            return false;

        return feature.Enabled;
    }

    public async Task<bool> HasAnyFeaturesConfiguredAsync(string tenantId, CancellationToken cancellationToken = default)
    {
        return await _context.TenantFeatures
            .AnyAsync(f => f.TenantId == tenantId, cancellationToken);
    }

    public async Task SetFeaturesAsync(string tenantId, IEnumerable<TenantFeatureUpdate> updates, string? grantedByUserId, CancellationToken cancellationToken = default)
    {
        var existing = await _context.TenantFeatures
            .Where(f => f.TenantId == tenantId)
            .ToListAsync(cancellationToken);

        var updateList = updates.ToList();
        var updatedKeys = updateList.Select(u => u.FeatureKey).ToHashSet(StringComparer.OrdinalIgnoreCase);

        // Remove features not in the update list
        var toRemove = existing.Where(e => !updatedKeys.Contains(e.FeatureKey)).ToList();
        _context.TenantFeatures.RemoveRange(toRemove);

        foreach (var update in updateList)
        {
            var existingFeature = existing.FirstOrDefault(e =>
                e.FeatureKey.Equals(update.FeatureKey, StringComparison.OrdinalIgnoreCase));

            if (existingFeature != null)
            {
                existingFeature.Enabled = update.Enabled;
                existingFeature.MaxSeats = update.MaxSeats;
                existingFeature.GrantedByUserId = grantedByUserId;
                existingFeature.GrantedAt = DateTime.UtcNow;
            }
            else
            {
                _context.TenantFeatures.Add(new TenantFeature
                {
                    TenantId = tenantId,
                    FeatureKey = update.FeatureKey,
                    Enabled = update.Enabled,
                    MaxSeats = update.MaxSeats,
                    GrantedByUserId = grantedByUserId,
                    GrantedAt = DateTime.UtcNow,
                });
            }
        }

        await _context.SaveChangesAsync(cancellationToken);
    }

    public async Task EnableFeatureAsync(string tenantId, string featureKey, string? grantedByUserId, CancellationToken cancellationToken = default)
    {
        var feature = await _context.TenantFeatures
            .FirstOrDefaultAsync(f => f.TenantId == tenantId && f.FeatureKey == featureKey, cancellationToken);

        if (feature != null)
        {
            feature.Enabled = true;
            feature.GrantedByUserId = grantedByUserId;
            feature.GrantedAt = DateTime.UtcNow;
        }
        else
        {
            _context.TenantFeatures.Add(new TenantFeature
            {
                TenantId = tenantId,
                FeatureKey = featureKey,
                Enabled = true,
                GrantedByUserId = grantedByUserId,
                GrantedAt = DateTime.UtcNow,
            });
        }

        await _context.SaveChangesAsync(cancellationToken);
    }

    public async Task DisableFeatureAsync(string tenantId, string featureKey, CancellationToken cancellationToken = default)
    {
        var feature = await _context.TenantFeatures
            .FirstOrDefaultAsync(f => f.TenantId == tenantId && f.FeatureKey == featureKey, cancellationToken);

        if (feature != null)
        {
            feature.Enabled = false;
            await _context.SaveChangesAsync(cancellationToken);
        }
    }
}
