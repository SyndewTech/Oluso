namespace Oluso.Core.Licensing;

/// <summary>
/// Tracks which modules have been seeded for each tenant.
/// Used to skip seeding for disabled modules on tenant creation,
/// then seed them later when the feature is enabled.
/// </summary>
public interface ITenantSeedTracker
{
    Task<bool> IsModuleSeededAsync(string tenantId, string featureKey, CancellationToken cancellationToken = default);
    Task MarkModuleSeededAsync(string tenantId, string featureKey, CancellationToken cancellationToken = default);
    Task<IReadOnlyList<string>> GetUnseededModulesAsync(string tenantId, CancellationToken cancellationToken = default);
}
