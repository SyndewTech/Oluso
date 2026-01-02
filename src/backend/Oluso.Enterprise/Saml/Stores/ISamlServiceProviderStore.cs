using Oluso.Enterprise.Saml.Entities;

namespace Oluso.Enterprise.Saml.Stores;

/// <summary>
/// Store for SAML Service Providers - apps that use this system as their SAML IdP.
/// </summary>
public interface ISamlServiceProviderStore
{
    /// <summary>
    /// Gets a Service Provider by its Entity ID
    /// </summary>
    Task<SamlServiceProvider?> GetByEntityIdAsync(
        string entityId,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Gets a Service Provider by its database ID
    /// </summary>
    Task<SamlServiceProvider?> GetByIdAsync(
        int id,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Gets all Service Providers
    /// </summary>
    Task<IReadOnlyList<SamlServiceProvider>> GetAllAsync(
        bool includeDisabled = false,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Adds a new Service Provider
    /// </summary>
    Task<SamlServiceProvider> AddAsync(
        SamlServiceProvider entity,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Updates an existing Service Provider
    /// </summary>
    Task<SamlServiceProvider> UpdateAsync(
        SamlServiceProvider entity,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Deletes a Service Provider by ID
    /// </summary>
    Task<bool> DeleteAsync(
        int id,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Checks if an Entity ID exists
    /// </summary>
    Task<bool> ExistsAsync(
        string entityId,
        int? excludeId = null,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Updates the last accessed time for a Service Provider
    /// </summary>
    Task UpdateLastAccessedAsync(
        string entityId,
        CancellationToken cancellationToken = default);
}
