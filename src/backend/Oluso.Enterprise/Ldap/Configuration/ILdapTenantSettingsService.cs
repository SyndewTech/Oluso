namespace Oluso.Enterprise.Ldap.Configuration;

/// <summary>
/// Service for retrieving LDAP server settings for tenants.
/// Settings are stored in tenant Configuration JSON under "ldapServer" key.
/// </summary>
public interface ILdapTenantSettingsService
{
    /// <summary>
    /// Gets LDAP settings for the current tenant context.
    /// </summary>
    Task<TenantLdapSettings> GetSettingsAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// Gets LDAP settings for a specific tenant.
    /// </summary>
    Task<TenantLdapSettings> GetSettingsAsync(string tenantId, CancellationToken cancellationToken = default);

    /// <summary>
    /// Updates LDAP settings for a specific tenant.
    /// </summary>
    Task UpdateSettingsAsync(string tenantId, TenantLdapSettings settings, CancellationToken cancellationToken = default);
}
