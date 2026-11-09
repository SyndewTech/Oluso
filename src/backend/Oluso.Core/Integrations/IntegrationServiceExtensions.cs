using Microsoft.Extensions.DependencyInjection;
using Oluso.Core.Services;

namespace Oluso.Core.Integrations;

/// <summary>
/// Extension methods for registering integration tenant settings services.
/// </summary>
public static class IntegrationServiceExtensions
{
    /// <summary>
    /// Adds the integration tenant settings service (reads/writes Tenant.Configuration JSON).
    /// Platform infra — wired once at the host, independent of any feature module.
    /// </summary>
    public static IServiceCollection AddIntegrationTenantSettings(this IServiceCollection services)
    {
        // Provider credentials (e.g. the POS integration's API key) live in
        // this section; registering it here means every save is
        // DataProtection-encrypted at rest. Rows written before this was
        // registered are read as plaintext and re-encrypted on their next save.
        services.Configure<TenantConfigurationOptions>(o => o.EncryptedSections.Add(IntegrationTenantSettings.SectionKey));
        services.AddScoped<IIntegrationTenantSettingsService, IntegrationTenantSettingsService>();
        return services;
    }
}
