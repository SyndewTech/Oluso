using Oluso.Enterprise.Scim.Entities;

namespace Oluso.Enterprise.Scim.Stores;

/// <summary>
/// Store for SCIM resource mappings (external ID to internal ID)
/// </summary>
public interface IScimResourceMappingStore
{
    Task<ScimResourceMapping?> GetByExternalIdAsync(
        string scimClientId,
        string resourceType,
        string externalId,
        CancellationToken cancellationToken = default);

    Task<ScimResourceMapping?> GetByInternalIdAsync(
        string scimClientId,
        string resourceType,
        string internalId,
        CancellationToken cancellationToken = default);

    Task<ScimResourceMapping> CreateAsync(ScimResourceMapping mapping, CancellationToken cancellationToken = default);
    Task DeleteAsync(string id, CancellationToken cancellationToken = default);
    Task DeleteByInternalIdAsync(string resourceType, string internalId, CancellationToken cancellationToken = default);
}
