using Oluso.Enterprise.Scim.Entities;

namespace Oluso.Enterprise.Scim.Stores;

/// <summary>
/// Store for managing SCIM clients
/// </summary>
public interface IScimClientStore
{
    Task<ScimClient?> GetByIdAsync(string id, CancellationToken cancellationToken = default);
    Task<ScimClient?> GetByTokenHashAsync(string tokenHash, CancellationToken cancellationToken = default);
    Task<IReadOnlyList<ScimClient>> GetByTenantAsync(string tenantId, CancellationToken cancellationToken = default);
    Task<ScimClient> CreateAsync(ScimClient client, CancellationToken cancellationToken = default);
    Task UpdateAsync(ScimClient client, CancellationToken cancellationToken = default);
    Task DeleteAsync(string id, CancellationToken cancellationToken = default);
    Task UpdateActivityAsync(string id, bool success, CancellationToken cancellationToken = default);
}
