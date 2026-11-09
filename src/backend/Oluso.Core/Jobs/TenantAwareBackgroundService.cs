using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Oluso.Core.Domain.Entities;
using Oluso.Core.Domain.Interfaces;
using Oluso.Core.Licensing;

namespace Oluso.Core.Jobs;

/// <summary>
/// Base class for multi-tenant background services that delegate to a portable job runner.
/// Discovers active tenants, creates a scoped DI container per tenant with proper tenant context,
/// and calls the job runner with the tenant ID. This ensures the same IJobRunner implementation
/// is shared between BackgroundServices and Azure Functions.
/// </summary>
/// <typeparam name="TRunner">The job runner interface (e.g., IAccountingJobRunner)</typeparam>
public abstract class TenantAwareBackgroundService<TRunner> : BackgroundService where TRunner : class
{
    private readonly IServiceProvider _serviceProvider;
    private readonly ILogger _logger;

    protected TenantAwareBackgroundService(
        IServiceProvider serviceProvider,
        ILogger logger)
    {
        _serviceProvider = serviceProvider;
        _logger = logger;
    }

    /// <summary>
    /// Display name for logging (e.g., "Exchange Rate Sync")
    /// </summary>
    protected abstract string JobName { get; }

    /// <summary>
    /// How often the job runs
    /// </summary>
    protected abstract TimeSpan Interval { get; }

    /// <summary>
    /// Initial delay before the first run (default: 2 minutes to let the app start)
    /// </summary>
    protected virtual TimeSpan InitialDelay => TimeSpan.FromMinutes(2);

    /// <summary>
    /// Whether this job is enabled. Override to check options.
    /// </summary>
    protected virtual bool IsEnabled => true;

    /// <summary>
    /// Optional gate: return false to skip a specific run (e.g., only run on 1st of month)
    /// </summary>
    protected virtual bool ShouldRunNow() => true;

    /// <summary>
    /// Feature key this job belongs to (the owning module's key).
    /// When set, each tenant is checked against <see cref="IFeatureGate"/> before
    /// the runner is invoked — tenants without the feature are skipped at debug
    /// level. Tenants that <em>had</em> the feature enabled and turned it off
    /// can still get processed via <see cref="HasInFlightWorkAsync"/> so their
    /// in-flight items drain rather than dangle forever.
    ///
    /// <para>Override to gate per tenant. Default <c>null</c> = no gating
    /// (job runs for every active production tenant — the original behaviour).</para>
    /// </summary>
    protected virtual string? RequiredFeatureKey => null;

    /// <summary>
    /// Optional escape hatch for tenants that lack <see cref="RequiredFeatureKey"/>
    /// but still have unfinished work the job created earlier. Returning true
    /// keeps the runner alive for that tenant so the queue drains.
    ///
    /// <para>Default: <c>false</c> — feature-gated jobs skip ineligible tenants
    /// outright. Modules that need drain semantics (e.g. approval timeouts
    /// continuing to fire on in-flight requests after the module is disabled)
    /// override and do a cheap "is there pending work?" probe.</para>
    /// </summary>
    protected virtual ValueTask<bool> HasInFlightWorkAsync(
        IServiceProvider scope,
        string tenantId,
        CancellationToken cancellationToken)
        => ValueTask.FromResult(false);

    /// <summary>
    /// Execute the job for a single tenant. This is the only method subclasses must implement.
    /// </summary>
    protected abstract Task<int> RunForTenantAsync(TRunner runner, string tenantId, CancellationToken cancellationToken);

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        if (!IsEnabled)
        {
            _logger.LogInformation("{JobName} is disabled", JobName);
            return;
        }

        _logger.LogInformation("{JobName} starting with interval {Interval}", JobName, Interval);

        await Task.Delay(InitialDelay, stoppingToken);

        while (!stoppingToken.IsCancellationRequested)
        {
            if (ShouldRunNow())
            {
                try
                {
                    await RunAllTenantsAsync(stoppingToken);
                }
                catch (Exception ex) when (ex is not OperationCanceledException)
                {
                    _logger.LogError(ex, "Error in {JobName}", JobName);
                }
            }

            await Task.Delay(Interval, stoppingToken);
        }
    }

    /// <summary>
    /// Two-stage eligibility check, evaluated inside the per-tenant scope so
    /// <see cref="IFeatureGate"/> resolves licensing for the right tenant:
    /// <list type="number">
    ///   <item>If <see cref="RequiredFeatureKey"/> is null → always eligible
    ///         (preserves the original behaviour for jobs that haven't opted
    ///         into gating yet).</item>
    ///   <item>If the feature gate allows the tenant → eligible.</item>
    ///   <item>Otherwise → fall back to <see cref="HasInFlightWorkAsync"/>.
    ///         Modules with drain semantics return true here so a recently
    ///         disabled feature still finishes outstanding work.</item>
    /// </list>
    /// </summary>
    private async ValueTask<bool> IsTenantEligibleAsync(
        IServiceProvider scope,
        string tenantId,
        CancellationToken cancellationToken)
    {
        if (string.IsNullOrEmpty(RequiredFeatureKey)) return true;

        var gate = scope.GetService<IFeatureGate>();
        if (gate == null)
        {
            // No gate registered in this host (e.g. minimal test wiring).
            // Fail open rather than silently skipping every tenant.
            return true;
        }

        var result = await gate.CheckFeatureAsync(RequiredFeatureKey, cancellationToken: cancellationToken);
        if (result.IsAllowed) return true;

        // Feature off: see if there's still work to drain.
        return await HasInFlightWorkAsync(scope, tenantId, cancellationToken);
    }

    private async Task RunAllTenantsAsync(CancellationToken cancellationToken)
    {
        // Discover tenants in an unscoped scope (no tenant context = global query filter passes all)
        List<Tenant> tenants;
        using (var discoveryScope = _serviceProvider.CreateScope())
        {
            var tenantStore = discoveryScope.ServiceProvider.GetRequiredService<ITenantStore>();
            var allTenants = await tenantStore.GetAllAsync(cancellationToken);
            tenants = allTenants
                .Where(t => t.Enabled && t.Environment == TenantEnvironment.Production)
                .ToList();
        }

        if (tenants.Count == 0)
        {
            _logger.LogDebug("{JobName}: no active tenants found", JobName);
            return;
        }

        var totalProcessed = 0;

        foreach (var tenant in tenants)
        {
            try
            {
                using var scope = _serviceProvider.CreateScope();
                var tenantAccessor = scope.ServiceProvider.GetRequiredService<ITenantContextAccessor>();
                tenantAccessor.SetTenant(tenant);

                try
                {
                    if (!await IsTenantEligibleAsync(scope.ServiceProvider, tenant.Id, cancellationToken))
                    {
                        // Debug only — this is correct behaviour, not a failure.
                        // Logging at info would drown out the actual job activity
                        // on shared deployments with many tenants.
                        _logger.LogDebug(
                            "{JobName}: tenant {TenantId} lacks feature '{Feature}' and has no in-flight work — skipping",
                            JobName, tenant.Id, RequiredFeatureKey);
                        continue;
                    }

                    var runner = scope.ServiceProvider.GetRequiredService<TRunner>();
                    var count = await RunForTenantAsync(runner, tenant.Id, cancellationToken);
                    totalProcessed += count;
                }
                finally
                {
                    tenantAccessor.ClearTenant();
                }
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                _logger.LogError(ex, "{JobName} failed for tenant {TenantId}", JobName, tenant.Id);
            }
        }

        if (totalProcessed > 0)
        {
            _logger.LogInformation("{JobName} completed: {Count} items processed across {TenantCount} tenants",
                JobName, totalProcessed, tenants.Count);
        }
    }
}
