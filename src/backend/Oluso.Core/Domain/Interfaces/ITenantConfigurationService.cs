namespace Oluso.Core.Domain.Interfaces;

/// <summary>
/// Centralized service for reading and writing tenant configuration sections.
/// Each module uses a unique section key. Sensitive sections are encrypted at rest.
/// </summary>
public interface ITenantConfigurationService
{
    /// <summary>
    /// Get a configuration section for a tenant, deserialized to the specified type.
    /// Returns null if the section does not exist.
    /// </summary>
    Task<T?> GetSectionAsync<T>(string tenantId, string sectionKey, CancellationToken cancellationToken = default) where T : class;

    /// <summary>
    /// Create or update a configuration section for a tenant.
    /// If the section key is registered as encrypted, the value will be encrypted at rest.
    /// </summary>
    Task SetSectionAsync<T>(string tenantId, string sectionKey, T value, CancellationToken cancellationToken = default) where T : class;

    /// <summary>
    /// Remove a configuration section for a tenant.
    /// </summary>
    Task RemoveSectionAsync(string tenantId, string sectionKey, CancellationToken cancellationToken = default);
}
