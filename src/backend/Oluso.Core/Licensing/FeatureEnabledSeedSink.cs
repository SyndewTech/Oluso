using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Oluso.Core.Domain.Interfaces;
using Oluso.Core.Events;

namespace Oluso.Core.Licensing;

/// <summary>
/// Listens for FeatureEnabledEvent and seeds module data for tenants
/// that missed seeding because the feature was disabled at tenant creation.
///
/// - Organization scope: seeds all tenants in the org that haven't been seeded for this module.
/// - Tenant scope: seeds just that tenant if it hasn't been seeded for this module.
/// </summary>
public class FeatureEnabledSeedSink : IOlusoEventSink
{
    private readonly IServiceProvider _serviceProvider;
    private readonly ILogger<FeatureEnabledSeedSink> _logger;

    public FeatureEnabledSeedSink(
        IServiceProvider serviceProvider,
        ILogger<FeatureEnabledSeedSink> logger)
    {
        _serviceProvider = serviceProvider;
        _logger = logger;
    }

    public string Name => "FeatureEnabledSeed";

    public async Task HandleAsync(OlusoEvent evt, CancellationToken cancellationToken = default)
    {
        if (evt is not FeatureEnabledEvent featureEvent)
            return;

        var module = ModuleRegistry.Instance.GetModule(featureEvent.FeatureKey);
        if (module == null)
        {
            _logger.LogDebug("No module descriptor found for feature {FeatureKey}", featureEvent.FeatureKey);
            return;
        }

        var seedTracker = _serviceProvider.GetService<ITenantSeedTracker>();
        if (seedTracker == null)
        {
            _logger.LogWarning("ITenantSeedTracker not registered — cannot run deferred seeding");
            return;
        }

        if (featureEvent.FeatureScope == "tenant")
        {
            var tenantId = featureEvent.TenantId;
            if (string.IsNullOrEmpty(tenantId)) return;

            await SeedModuleForTenantAsync(module, tenantId, seedTracker, cancellationToken);
        }
        else if (featureEvent.FeatureScope == "organization" && !string.IsNullOrEmpty(featureEvent.OrganizationId))
        {
            // Org-level enablement only makes the feature AVAILABLE to tenants
            // under the org — it doesn't activate it for any specific tenant.
            // Only seed module data for tenants that have ALSO turned the
            // feature on at the tenant level. Without this gate, every tenant
            // in the org gets module workflows/templates regardless of whether
            // they opted in.
            var tenantStore = _serviceProvider.GetRequiredService<ITenantStore>();
            var tenantFeatureStore = _serviceProvider.GetService<ITenantFeatureStore>();
            var tenants = await tenantStore.GetByOrganizationAsync(featureEvent.OrganizationId, cancellationToken);

            foreach (var tenant in tenants)
            {
                if (tenantFeatureStore != null)
                {
                    var tenantHasFeature = await tenantFeatureStore.IsFeatureEnabledAsync(
                        tenant.Id, featureEvent.FeatureKey, cancellationToken);
                    if (!tenantHasFeature)
                    {
                        _logger.LogDebug(
                            "Skipping seed for module {Module} on tenant {TenantId} — org-level enabled but tenant has not turned on the feature",
                            module.FeatureKey, tenant.Id);
                        continue;
                    }
                }

                await SeedModuleForTenantAsync(module, tenant.Id, seedTracker, cancellationToken);
            }
        }
    }

    private async Task SeedModuleForTenantAsync(
        IModuleDescriptor module,
        string tenantId,
        ITenantSeedTracker seedTracker,
        CancellationToken cancellationToken)
    {
        var alreadySeeded = await seedTracker.IsModuleSeededAsync(tenantId, module.FeatureKey, cancellationToken);
        if (alreadySeeded)
        {
            _logger.LogDebug("Module {Module} already seeded for tenant {TenantId}", module.FeatureKey, tenantId);
            return;
        }

        _logger.LogInformation("Running deferred seeding for module {Module} on tenant {TenantId}",
            module.FeatureKey, tenantId);

        try
        {
            await module.SeedTenantAsync(_serviceProvider, tenantId, cancellationToken);

            // Seed email templates for this module
            var templates = module.GetEmailTemplateSeeds().ToList();
            if (templates.Count > 0)
            {
                var persister = _serviceProvider.GetService<ITenantSeedPersister>();
                if (persister != null)
                {
                    await persister.SeedEmailTemplatesAsync(tenantId, templates, cancellationToken);
                    await persister.SeedWorkflowsForTemplatesAsync(tenantId, templates, cancellationToken);
                }
            }

            await seedTracker.MarkModuleSeededAsync(tenantId, module.FeatureKey, cancellationToken);
            _logger.LogInformation("Deferred seeding complete for module {Module} on tenant {TenantId}",
                module.FeatureKey, tenantId);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed deferred seeding for module {Module} on tenant {TenantId}",
                module.FeatureKey, tenantId);
        }
    }
}
