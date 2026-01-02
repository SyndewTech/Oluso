using Oluso.Enterprise.Scim.Entities;

namespace Oluso.Enterprise.Scim.Stores;

/// <summary>
/// Store for SCIM provisioning logs
/// </summary>
public interface IScimProvisioningLogStore
{
    Task<ScimProvisioningLog> CreateAsync(ScimProvisioningLog log, CancellationToken cancellationToken = default);
    Task<IReadOnlyList<ScimProvisioningLog>> GetByClientAsync(
        string scimClientId,
        int skip = 0,
        int take = 50,
        CancellationToken cancellationToken = default);
    Task<IReadOnlyList<ScimProvisioningLog>> GetByTenantAsync(
        string tenantId,
        int skip = 0,
        int take = 50,
        CancellationToken cancellationToken = default);
    Task<int> GetCountByClientAsync(string scimClientId, CancellationToken cancellationToken = default);
    Task DeleteOlderThanAsync(DateTime cutoff, CancellationToken cancellationToken = default);
}
