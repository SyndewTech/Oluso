namespace Oluso.Core.Integrations;

/// <summary>
/// Service for reading and updating integration settings stored in Tenant.Configuration JSON.
/// Follows the same pattern as ISamlTenantSettingsService and ILdapTenantSettingsService.
/// </summary>
public interface IIntegrationTenantSettingsService
{
    /// <summary>
    /// Gets all integration settings for the specified tenant.
    /// </summary>
    Task<IntegrationTenantSettings> GetSettingsAsync(string tenantId, CancellationToken cancellationToken = default);

    /// <summary>
    /// Gets a specific provider's configuration.
    /// </summary>
    Task<IntegrationProviderConfig?> GetProviderAsync(string tenantId, string providerId, CancellationToken cancellationToken = default);

    /// <summary>
    /// Gets the first enabled provider of a given type (e.g., find the active WhatsApp provider).
    /// </summary>
    Task<(string ProviderId, IntegrationProviderConfig Config)?> GetEnabledProviderByTypeAsync(
        string tenantId, string providerType, CancellationToken cancellationToken = default);

    /// <summary>
    /// Creates or updates a provider's configuration.
    /// </summary>
    Task<IntegrationTenantSettings> UpsertProviderAsync(
        string tenantId, string providerId, IntegrationProviderConfig config, CancellationToken cancellationToken = default);

    /// <summary>
    /// Removes a provider's configuration.
    /// </summary>
    Task<IntegrationTenantSettings> RemoveProviderAsync(string tenantId, string providerId, CancellationToken cancellationToken = default);
}
