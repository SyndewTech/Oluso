using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Oluso.Core.Domain.Interfaces;
using Oluso.Core.Jobs;

namespace Oluso.Core.Services;

/// <summary>
/// Stand-alone ApiScopes a module owns that require one of its tenant features (<see cref="ApiScopeFeatures"/>).
/// The module still creates its scopes in the tenants it is enabled for; the declarations let
/// <see cref="FeatureGatedScopeBackfillService"/> stamp the required feature on the scopes that already exist in
/// every tenant, including tenants that have since switched the feature off.
/// </summary>
public interface IFeatureGatedScopeSource
{
    IEnumerable<StandaloneApiScopeDefinition> GetFeatureGatedScopes();
}

/// <summary>Registration helpers for <see cref="IFeatureGatedScopeSource"/>.</summary>
public static class FeatureGatedScopeRegistration
{
    /// <summary>
    /// Declares <paramref name="scopes"/> as requiring <paramref name="featureKey"/>, and registers the startup pass
    /// that stamps the requirement on scopes declared before it existed.
    /// </summary>
    public static IServiceCollection AddFeatureGatedScopes(
        this IServiceCollection services,
        string featureKey,
        IEnumerable<(string Name, string DisplayName, string Description)> scopes)
    {
        var definitions = scopes
            .Select(s => new StandaloneApiScopeDefinition(s.Name, s.DisplayName, s.Description, featureKey))
            .ToList();
        services.AddSingleton<IFeatureGatedScopeSource>(new StaticFeatureGatedScopeSource(definitions));
        return services.AddFeatureGatedScopeBackfill();
    }

    /// <summary>Registers a source computed at run time (from options, say) and the startup pass.</summary>
    public static IServiceCollection AddFeatureGatedScopeSource<TSource>(this IServiceCollection services)
        where TSource : class, IFeatureGatedScopeSource
    {
        services.TryAddEnumerable(ServiceDescriptor.Singleton<IFeatureGatedScopeSource, TSource>());
        return services.AddFeatureGatedScopeBackfill();
    }

    /// <summary>All the declarations, merged by scope name (<see cref="StandaloneApiScopes"/> rules).</summary>
    public static IReadOnlyList<StandaloneApiScopeDefinition> Collect(IEnumerable<IFeatureGatedScopeSource> sources) =>
        sources.SelectMany(s => s.GetFeatureGatedScopes())
            .Where(d => !string.IsNullOrWhiteSpace(d.Name) && ApiScopeFeatures.Parse(d.RequiredFeature).Count > 0)
            .GroupBy(d => d.Name, StringComparer.Ordinal)
            .Select(g => g.First() with
            {
                RequiredFeature = ApiScopeFeatures.Format(g.SelectMany(d => ApiScopeFeatures.Parse(d.RequiredFeature)))
            })
            .ToList();

    private static IServiceCollection AddFeatureGatedScopeBackfill(this IServiceCollection services)
    {
        services.TryAddEnumerable(ServiceDescriptor.Singleton<IHostedService, FeatureGatedScopeBackfillService>());
        return services;
    }

    private sealed class StaticFeatureGatedScopeSource : IFeatureGatedScopeSource
    {
        private readonly IReadOnlyList<StandaloneApiScopeDefinition> _definitions;
        public StaticFeatureGatedScopeSource(IReadOnlyList<StandaloneApiScopeDefinition> definitions) => _definitions = definitions;
        public IEnumerable<StandaloneApiScopeDefinition> GetFeatureGatedScopes() => _definitions;
    }
}

/// <summary>
/// One-shot startup pass: in every tenant, the existing ApiScopes of the <see cref="IFeatureGatedScopeSource"/>
/// declarations get the tenant feature they require (<see cref="StandaloneApiScopes.EnsureRequiredFeaturesAsync"/>).
/// Creates nothing. Idempotent; a tenant whose scopes already carry it is left untouched.
/// </summary>
public sealed class FeatureGatedScopeBackfillService : BackgroundService
{
    private readonly IServiceScopeFactory _scopes;
    private readonly IEnumerable<IFeatureGatedScopeSource> _sources;
    private readonly ILogger<FeatureGatedScopeBackfillService> _logger;

    /// <summary>How long to wait for the host's migrations before the pass.</summary>
    private static readonly TimeSpan StartDelay = TimeSpan.FromSeconds(15);

    public FeatureGatedScopeBackfillService(
        IServiceScopeFactory scopes, IEnumerable<IFeatureGatedScopeSource> sources, ILogger<FeatureGatedScopeBackfillService> logger)
    {
        _scopes = scopes;
        _sources = sources;
        _logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        try
        {
            await Task.Delay(StartDelay, stoppingToken);
            await RunAsync(stoppingToken);
        }
        catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
        {
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Stamping the required features on feature-gated scopes failed");
        }
    }

    /// <summary>The pass itself: every tenant, enabled or not. Returns the number of scopes changed.</summary>
    public async Task<int> RunAsync(CancellationToken cancellationToken = default)
    {
        var definitions = FeatureGatedScopeRegistration.Collect(_sources);
        if (definitions.Count == 0)
            return 0;

        List<string> tenantIds;
        using (var discovery = _scopes.CreateScope())
        {
            tenantIds = (await discovery.ServiceProvider.GetRequiredService<ITenantStore>().GetAllAsync(cancellationToken))
                .Select(t => t.Id)
                .ToList();
        }

        var total = 0;
        foreach (var tenantId in tenantIds)
        {
            cancellationToken.ThrowIfCancellationRequested();
            try
            {
                await TenantScope.RunAsync(_scopes, tenantId, async (sp, ct) =>
                {
                    var changed = await sp.GetRequiredService<IResourceStore>().EnsureRequiredFeaturesAsync(definitions, ct);
                    if (changed.Count > 0)
                    {
                        total += changed.Count;
                        _logger.LogInformation("Feature-gated scopes of tenant {TenantId} now require their feature: {Scopes}",
                            tenantId, string.Join(", ", changed));
                    }
                }, cancellationToken);
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                _logger.LogWarning(ex, "Stamping the required features on the scopes of tenant {TenantId} failed", tenantId);
            }
        }
        return total;
    }
}
