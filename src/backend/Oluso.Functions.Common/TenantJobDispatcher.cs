using System.Text.Json;
using Azure.Storage.Queues;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Oluso.Core.Domain.Interfaces;
using Oluso.Core.Licensing;
using Oluso.EntityFramework;

namespace Oluso.Functions.Common;

/// <summary>
/// Dispatches tenant-scoped jobs to Azure Storage Queues for fan-out processing.
/// Timer functions use this to enqueue one message per tenant, and queue-triggered
/// functions process each tenant independently with their own timeout budget.
/// </summary>
public class TenantJobDispatcher
{
    private readonly ITenantProvider _tenantProvider;
    private readonly IServiceProvider _serviceProvider;
    private readonly QueueServiceClient _queueServiceClient;
    private readonly ILogger<TenantJobDispatcher> _logger;

    public TenantJobDispatcher(
        ITenantProvider tenantProvider,
        IServiceProvider serviceProvider,
        QueueServiceClient queueServiceClient,
        ILogger<TenantJobDispatcher> logger)
    {
        _tenantProvider = tenantProvider;
        _serviceProvider = serviceProvider;
        _queueServiceClient = queueServiceClient;
        _logger = logger;
    }

    /// <summary>
    /// Enqueue a job message for each active tenant onto the specified queue.
    ///
    /// <para>When <paramref name="filter"/> is supplied, only tenants the
    /// filter accepts get a message. The filter receives a per-tenant
    /// <see cref="IServiceProvider"/> scope (with tenant context already
    /// set) so it can call <see cref="IFeatureGate"/> or any module-specific
    /// in-flight probe. Mirrors the gating that
    /// <c>TenantAwareBackgroundService&lt;T&gt;</c> applies on the in-proc
    /// host so a tenant who can't run a job in one deployment can't run it
    /// in the other either.</para>
    /// </summary>
    public async Task<int> DispatchToAllTenantsAsync(
        string queueName,
        string jobType,
        TenantDispatchFilter? filter = null,
        CancellationToken cancellationToken = default)
    {
        var tenantIds = await _tenantProvider.GetActiveTenantIdsAsync(cancellationToken);

        if (tenantIds.Count == 0)
            return 0;

        var queueClient = _queueServiceClient.GetQueueClient(queueName);
        await queueClient.CreateIfNotExistsAsync(cancellationToken: cancellationToken);

        var dispatched = 0;
        foreach (var tenantId in tenantIds)
        {
            if (filter != null && !await IsTenantEligibleAsync(tenantId, filter, cancellationToken))
            {
                _logger.LogDebug(
                    "{JobType}: tenant {TenantId} has nothing to do (feature '{Feature}', pending work required: {RequireInFlight}) — skipping enqueue",
                    jobType, tenantId, filter.FeatureKey ?? "-", filter.RequireInFlight);
                continue;
            }

            var message = JsonSerializer.Serialize(new TenantJobMessage(tenantId, jobType));
            await queueClient.SendMessageAsync(message, cancellationToken);
            dispatched++;
        }

        _logger.LogInformation(
            "Dispatched {JobType} to {Dispatched}/{Total} tenants via queue {Queue}",
            jobType, dispatched, tenantIds.Count, queueName);

        return dispatched;
    }

    /// <summary>
    /// Per-tenant eligibility check, evaluated in a scoped DI container with
    /// the tenant context pinned so the feature gate resolves correctly.
    /// </summary>
    private async Task<bool> IsTenantEligibleAsync(
        string tenantId,
        TenantDispatchFilter filter,
        CancellationToken cancellationToken)
    {
        using var scope = _serviceProvider.CreateScope();

        var accessor = scope.ServiceProvider.GetService<ITenantContextAccessor>();
        if (accessor != null)
        {
            var tenants = scope.ServiceProvider.GetRequiredService<ITenantStore>();
            var tenant = await tenants.GetByIdAsync(tenantId, cancellationToken);
            if (tenant != null) accessor.SetTenant(tenant);
        }

        try
        {
            // Work-driven jobs (e.g. resuming delayed automations) enqueue only for
            // tenants that actually have something pending, whatever their features.
            if (filter.RequireInFlight)
            {
                if (filter.InFlightProbe == null)
                {
                    // Misconfigured filter: fail open rather than silently starving every tenant.
                    return true;
                }
                return await filter.InFlightProbe(scope.ServiceProvider, tenantId, cancellationToken);
            }

            if (string.IsNullOrEmpty(filter.FeatureKey))
            {
                return true;
            }

            var gate = scope.ServiceProvider.GetService<IFeatureGate>();
            if (gate == null)
            {
                // No gate registered in this host — fail open rather than
                // silently skipping every tenant.
                return true;
            }

            var allowed = (await gate.CheckFeatureAsync(filter.FeatureKey, cancellationToken: cancellationToken)).IsAllowed;
            if (allowed)
            {
                // Feature on: with a due-work check, only tenants that have something to do get a message.
                return filter.DueWorkProbe == null || await filter.DueWorkProbe(scope.ServiceProvider, tenantId, cancellationToken);
            }

            // Drain: feature is off, but the module says there's still work
            // pending for this tenant.
            if (filter.InFlightProbe != null)
            {
                return await filter.InFlightProbe(scope.ServiceProvider, tenantId, cancellationToken);
            }
            return false;
        }
        finally
        {
            accessor?.ClearTenant();
        }
    }
}

/// <summary>
/// Per-job dispatch filter. Set <see cref="FeatureKey"/> to gate on a tenant
/// feature; supply <see cref="InFlightProbe"/> for drain semantics so a
/// tenant that disabled the feature mid-flight still gets messages until
/// their queue empties; supply <see cref="DueWorkProbe"/> so tenants with the
/// feature on get a message only when they have work due (no due work, no
/// queue message, no worker run).
/// </summary>
public sealed record TenantDispatchFilter(
    string? FeatureKey,
    Func<IServiceProvider, string, CancellationToken, Task<bool>>? InFlightProbe = null,
    bool RequireInFlight = false,
    Func<IServiceProvider, string, CancellationToken, Task<bool>>? DueWorkProbe = null)
{
    /// <summary>
    /// A filter that enqueues only tenants for which <paramref name="probe"/> reports
    /// pending work, regardless of features. For scheduler ticks whose only purpose
    /// is to pick up due work: no due work, no message, no relayed event.
    /// </summary>
    public static TenantDispatchFilter PendingWorkOnly(Func<IServiceProvider, string, CancellationToken, Task<bool>> probe) =>
        new(null, probe, RequireInFlight: true);

    /// <summary>
    /// Tenants with <paramref name="featureKey"/> on whose job runner reports work due.
    /// The check lives on the module's runner, so every host builds the same filter.
    /// </summary>
    public static TenantDispatchFilter WithDueWork<TRunner>(
        string featureKey,
        Func<TRunner, string, CancellationToken, Task<bool>> hasDueWork) where TRunner : notnull =>
        new(featureKey, DueWorkProbe: (sp, tenantId, ct) => hasDueWork(sp.GetRequiredService<TRunner>(), tenantId, ct));

    /// <summary>
    /// <see cref="WithDueWork{TRunner}"/>, and also draining: a tenant that turned the
    /// feature off still gets messages while the runner reports work in flight.
    /// </summary>
    public static TenantDispatchFilter WithDueWorkAndDrain<TRunner>(
        string featureKey,
        Func<TRunner, string, CancellationToken, Task<bool>> hasWork) where TRunner : notnull
    {
        Func<IServiceProvider, string, CancellationToken, Task<bool>> probe = (sp, tenantId, ct) => hasWork(sp.GetRequiredService<TRunner>(), tenantId, ct);
        return new(featureKey, InFlightProbe: probe, DueWorkProbe: probe);
    }
}
