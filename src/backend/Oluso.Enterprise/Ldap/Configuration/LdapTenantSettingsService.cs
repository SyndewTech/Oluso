using Microsoft.Extensions.Logging;
using Oluso.Core.Domain.Interfaces;

namespace Oluso.Enterprise.Ldap.Configuration;

/// <summary>
/// Default implementation of ILdapTenantSettingsService.
/// Reads/writes settings from TenantConfigurationSections via ITenantConfigurationService.
/// </summary>
public class LdapTenantSettingsService : ILdapTenantSettingsService
{
    private const string SectionKey = "ldapServer";

    private readonly ITenantContext _tenantContext;
    private readonly ITenantConfigurationService _configService;
    private readonly ILogger<LdapTenantSettingsService> _logger;

    public LdapTenantSettingsService(
        ITenantContext tenantContext,
        ITenantConfigurationService configService,
        ILogger<LdapTenantSettingsService> logger)
    {
        _tenantContext = tenantContext;
        _configService = configService;
        _logger = logger;
    }

    public async Task<TenantLdapSettings> GetSettingsAsync(CancellationToken cancellationToken = default)
    {
        if (!_tenantContext.HasTenant || string.IsNullOrEmpty(_tenantContext.TenantId))
        {
            return TenantLdapSettings.Default;
        }

        return await GetSettingsAsync(_tenantContext.TenantId, cancellationToken);
    }

    public async Task<TenantLdapSettings> GetSettingsAsync(string tenantId, CancellationToken cancellationToken = default)
    {
        var settings = await _configService.GetSectionAsync<TenantLdapSettings>(tenantId, SectionKey, cancellationToken);
        return settings ?? TenantLdapSettings.Default;
    }

    public async Task UpdateSettingsAsync(string tenantId, TenantLdapSettings settings, CancellationToken cancellationToken = default)
    {
        await _configService.SetSectionAsync(tenantId, SectionKey, settings, cancellationToken);
        _logger.LogInformation("Updated LDAP settings for tenant {TenantId}", tenantId);
    }
}
