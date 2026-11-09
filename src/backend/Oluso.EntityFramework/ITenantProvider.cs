namespace Oluso.EntityFramework;

/// <summary>
/// Provides tenant IDs for background job iteration.
/// Only returns enabled Production tenants — background jobs should not run
/// against Development, QA, Staging, or UAT tenants.
/// </summary>
public interface ITenantProvider
{
    Task<IReadOnlyList<string>> GetActiveTenantIdsAsync(CancellationToken cancellationToken = default);
}
