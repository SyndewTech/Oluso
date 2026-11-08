using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Oluso.Core.Domain.Entities;
using Oluso.Core.Licensing;

namespace Oluso.EntityFramework.Stores;

public class TenantSeedTracker : ITenantSeedTracker
{
    private readonly OlusoDbContext _db;
    private readonly ILogger<TenantSeedTracker> _logger;

    public TenantSeedTracker(OlusoDbContext db, ILogger<TenantSeedTracker> logger)
    {
        _db = db;
        _logger = logger;
    }

    public async Task<bool> IsModuleSeededAsync(string tenantId, string featureKey, CancellationToken cancellationToken = default)
    {
        return await _db.TenantModuleSeeds
            .AnyAsync(s => s.TenantId == tenantId && s.FeatureKey == featureKey, cancellationToken);
    }

    public async Task MarkModuleSeededAsync(string tenantId, string featureKey, CancellationToken cancellationToken = default)
    {
        var exists = await IsModuleSeededAsync(tenantId, featureKey, cancellationToken);
        if (exists) return;

        _db.TenantModuleSeeds.Add(new TenantModuleSeed
        {
            TenantId = tenantId,
            FeatureKey = featureKey
        });

        await _db.SaveChangesAsync(cancellationToken);
        _logger.LogDebug("Marked module {FeatureKey} as seeded for tenant {TenantId}", featureKey, tenantId);
    }

    public async Task<IReadOnlyList<string>> GetUnseededModulesAsync(string tenantId, CancellationToken cancellationToken = default)
    {
        var allModules = ModuleRegistry.Instance.GetRegisteredModules()
            .Select(m => m.FeatureKey)
            .ToList();

        var seeded = await _db.TenantModuleSeeds
            .Where(s => s.TenantId == tenantId)
            .Select(s => s.FeatureKey)
            .ToListAsync(cancellationToken);

        return allModules.Except(seeded, StringComparer.OrdinalIgnoreCase).ToList();
    }
}
