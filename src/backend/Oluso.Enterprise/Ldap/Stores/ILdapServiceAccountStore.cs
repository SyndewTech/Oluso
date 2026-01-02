using Oluso.Enterprise.Ldap.Entities;

namespace Oluso.Enterprise.Ldap.Stores;

/// <summary>
/// Store for managing LDAP service accounts.
/// </summary>
public interface ILdapServiceAccountStore
{
    /// <summary>
    /// Get all service accounts for a tenant.
    /// </summary>
    Task<IReadOnlyList<LdapServiceAccount>> GetAllAsync(string tenantId, CancellationToken cancellationToken = default);

    /// <summary>
    /// Get a service account by ID.
    /// </summary>
    Task<LdapServiceAccount?> GetByIdAsync(string tenantId, string id, CancellationToken cancellationToken = default);

    /// <summary>
    /// Get a service account by bind DN.
    /// </summary>
    Task<LdapServiceAccount?> GetByBindDnAsync(string bindDn, CancellationToken cancellationToken = default);

    /// <summary>
    /// Create a new service account.
    /// </summary>
    Task<LdapServiceAccount> CreateAsync(LdapServiceAccount account, CancellationToken cancellationToken = default);

    /// <summary>
    /// Update an existing service account.
    /// </summary>
    Task<LdapServiceAccount> UpdateAsync(LdapServiceAccount account, CancellationToken cancellationToken = default);

    /// <summary>
    /// Delete a service account.
    /// </summary>
    Task DeleteAsync(string tenantId, string id, CancellationToken cancellationToken = default);

    /// <summary>
    /// Update the last used timestamp for a service account.
    /// </summary>
    Task UpdateLastUsedAsync(string id, CancellationToken cancellationToken = default);

    /// <summary>
    /// Validate service account credentials.
    /// </summary>
    Task<LdapServiceAccount?> ValidateCredentialsAsync(string bindDn, string password, CancellationToken cancellationToken = default);
}
