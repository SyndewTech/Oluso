using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Oluso.Core.Events;
using Oluso.Core.Services;

namespace Oluso.Core.Licensing;

/// <summary>
/// Listens for AdminTenantCreatedEvent and seeds default data for modules
/// whose features are enabled. Skips disabled modules — they get seeded
/// later when the feature is enabled (via FeatureEnabledSeedSink).
/// </summary>
public class TenantSeedEventSink : IOlusoEventSink
{
    private readonly IServiceProvider _serviceProvider;
    private readonly ILogger<TenantSeedEventSink> _logger;

    public TenantSeedEventSink(
        IServiceProvider serviceProvider,
        ILogger<TenantSeedEventSink> logger)
    {
        _serviceProvider = serviceProvider;
        _logger = logger;
    }

    public string Name => "TenantSeed";

    public async Task HandleAsync(OlusoEvent evt, CancellationToken cancellationToken = default)
    {
        if (evt is not AdminTenantCreatedEvent tenantEvent)
            return;

        var tenantId = tenantEvent.TenantId ?? tenantEvent.ResourceId;
        if (string.IsNullOrEmpty(tenantId))
        {
            _logger.LogWarning("AdminTenantCreatedEvent received without TenantId");
            return;
        }

        _logger.LogInformation("Seeding tenant data for {TenantId}", tenantId);

        var featureGate = _serviceProvider.GetService<IFeatureGate>();
        var seedTracker = _serviceProvider.GetService<ITenantSeedTracker>();
        var modules = ModuleRegistry.Instance.GetRegisteredModules();

        var allTemplates = new List<EmailTemplateSeed>();

        foreach (var module in modules)
        {
            var featureKey = ModuleFeatures.GetFeatureKeyForProvider(module.FeatureKey);

            // Only seed modules whose feature is enabled for this tenant.
            // Skipping BOTH the seed AND MarkModuleSeededAsync is load-bearing —
            // it leaves the tracker row absent so FeatureEnabledSeedSink can
            // pick up first-time seeding when the feature is enabled later.
            if (featureKey != null && featureGate != null)
            {
                var gate = await featureGate.CheckFeatureAsync(featureKey, cancellationToken: cancellationToken);
                if (!gate.IsAllowed)
                {
                    _logger.LogDebug("Skipping seed for module {Module} — feature not enabled for tenant {TenantId}",
                        module.FeatureKey, tenantId);
                    continue;
                }
            }

            var seedSucceeded = false;
            try
            {
                await module.SeedTenantAsync(_serviceProvider, tenantId, cancellationToken);
                _logger.LogDebug("Seeded tenant data for module {Module}", module.FeatureKey);
                seedSucceeded = true;
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Failed to seed tenant data for module {Module}", module.FeatureKey);
            }

            // Collect email templates
            try
            {
                allTemplates.AddRange(module.GetEmailTemplateSeeds());
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Failed to get email template seeds from module {Module}", module.FeatureKey);
            }

            // Mark module seeded only when SeedTenantAsync actually succeeded.
            // Marking after a thrown exception used to lock the tenant out of
            // FeatureEnabledSeedSink retry forever — the next attempt would
            // see IsModuleSeededAsync = true and skip silently. Leaving the
            // marker absent on failure preserves the retry path.
            if (seedSucceeded && seedTracker != null)
            {
                try
                {
                    await seedTracker.MarkModuleSeededAsync(tenantId, module.FeatureKey, cancellationToken);
                }
                catch (Exception ex)
                {
                    _logger.LogWarning(ex, "Failed to track seed status for module {Module}", module.FeatureKey);
                }
            }
        }

        // Core per-tenant template seeders — feature-gate-agnostic
        // contributors of tenant-discoverable starter content (e.g. the
        // branded campaign starter). Distinct from module template seeds
        // and from IPlatformTemplateSeeder (which targets _platform only).
        var coreSeeders = _serviceProvider.GetServices<ICoreEmailTemplateSeeder>().ToList();
        foreach (var seeder in coreSeeders)
        {
            try
            {
                allTemplates.AddRange(seeder.GetTenantTemplates());
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex,
                    "ICoreEmailTemplateSeeder {Type} threw while enumerating templates; skipping",
                    seeder.GetType().FullName);
            }
        }

        // Persist email templates
        if (allTemplates.Count > 0)
        {
            var persister = _serviceProvider.GetService<ITenantSeedPersister>();
            if (persister != null)
            {
                try
                {
                    await persister.SeedEmailTemplatesAsync(tenantId, allTemplates, cancellationToken);
                    _logger.LogInformation("Seeded {Count} email templates for tenant {TenantId}",
                        allTemplates.Count, tenantId);
                }
                catch (Exception ex)
                {
                    _logger.LogWarning(ex, "Failed to persist email templates for tenant {TenantId}", tenantId);
                }

                try
                {
                    await persister.SeedWorkflowsForTemplatesAsync(tenantId, allTemplates, cancellationToken);
                }
                catch (Exception ex)
                {
                    _logger.LogWarning(ex, "Failed to seed workflows for email templates for tenant {TenantId}", tenantId);
                }
            }
        }

        _logger.LogInformation("Tenant seeding complete for {TenantId}", tenantId);
    }
}
