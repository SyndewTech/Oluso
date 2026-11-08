using Microsoft.Extensions.Logging;
using Oluso.Core.Domain.Interfaces;

namespace Oluso.Core.Integrations;

/// <summary>
/// Stores integration settings in TenantConfigurationSections via ITenantConfigurationService.
/// </summary>
public class IntegrationTenantSettingsService : IIntegrationTenantSettingsService
{
    private readonly ITenantConfigurationService _configService;
    private readonly ILogger<IntegrationTenantSettingsService> _logger;

    public IntegrationTenantSettingsService(
        ITenantConfigurationService configService,
        ILogger<IntegrationTenantSettingsService> logger)
    {
        _configService = configService;
        _logger = logger;
    }

    public async Task<IntegrationTenantSettings> GetSettingsAsync(string tenantId, CancellationToken cancellationToken = default)
    {
        var settings = await _configService.GetSectionAsync<IntegrationTenantSettings>(tenantId, IntegrationTenantSettings.SectionKey, cancellationToken);
        return settings ?? IntegrationTenantSettings.Default;
    }

    public async Task<IntegrationProviderConfig?> GetProviderAsync(string tenantId, string providerId, CancellationToken cancellationToken = default)
    {
        var settings = await GetSettingsAsync(tenantId, cancellationToken);
        return settings.Providers.GetValueOrDefault(providerId);
    }

    public async Task<(string ProviderId, IntegrationProviderConfig Config)?> GetEnabledProviderByTypeAsync(
        string tenantId, string providerType, CancellationToken cancellationToken = default)
    {
        var settings = await GetSettingsAsync(tenantId, cancellationToken);
        var match = settings.Providers
            .FirstOrDefault(p => p.Value.Enabled && p.Value.ProviderType == providerType);

        if (match.Key == null) return null;
        return (match.Key, match.Value);
    }

    public async Task<IntegrationTenantSettings> UpsertProviderAsync(
        string tenantId, string providerId, IntegrationProviderConfig config, CancellationToken cancellationToken = default)
    {
        var settings = await GetSettingsAsync(tenantId, cancellationToken);
        settings.Providers[providerId] = config;

        await _configService.SetSectionAsync(tenantId, IntegrationTenantSettings.SectionKey, settings, cancellationToken);

        _logger.LogInformation("Updated integration provider {ProviderId} for tenant {TenantId}", providerId, tenantId);

        return settings;
    }

    public async Task<IntegrationTenantSettings> RemoveProviderAsync(string tenantId, string providerId, CancellationToken cancellationToken = default)
    {
        var settings = await GetSettingsAsync(tenantId, cancellationToken);
        settings.Providers.Remove(providerId);

        await _configService.SetSectionAsync(tenantId, IntegrationTenantSettings.SectionKey, settings, cancellationToken);

        _logger.LogInformation("Removed integration provider {ProviderId} for tenant {TenantId}", providerId, tenantId);

        return settings;
    }
}
