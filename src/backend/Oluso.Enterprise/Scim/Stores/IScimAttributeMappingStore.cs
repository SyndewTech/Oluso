using Oluso.Enterprise.Scim.Entities;

namespace Oluso.Enterprise.Scim.Stores;

/// <summary>
/// Store for SCIM attribute mappings
/// </summary>
public interface IScimAttributeMappingStore
{
    Task<ScimAttributeMapping?> GetByIdAsync(string id, CancellationToken cancellationToken = default);
    Task<IReadOnlyList<ScimAttributeMapping>> GetByClientAsync(string scimClientId, CancellationToken cancellationToken = default);
    Task<ScimAttributeMapping> CreateAsync(ScimAttributeMapping mapping, CancellationToken cancellationToken = default);
    Task UpdateAsync(ScimAttributeMapping mapping, CancellationToken cancellationToken = default);
    Task DeleteAsync(string id, CancellationToken cancellationToken = default);
    Task DeleteByClientAsync(string scimClientId, CancellationToken cancellationToken = default);
}
