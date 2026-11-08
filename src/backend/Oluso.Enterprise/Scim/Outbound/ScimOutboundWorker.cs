using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Oluso.Core.Domain.Entities;
using Oluso.Core.Domain.Interfaces;
using Oluso.Enterprise.Scim.Entities;
using Oluso.Enterprise.Scim.Stores;

namespace Oluso.Enterprise.Scim.Outbound;

/// <summary>
/// Drains the outbound queue. Providers are processed in parallel up to a
/// bound, items within one provider strictly in order. Also runs scheduled
/// reconciles and housekeeping. Safe to run on every instance of the host:
/// claims are conditional updates and expired locks are released.
/// </summary>
public sealed class ScimOutboundWorker : BackgroundService
{
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly ScimOptions _options;
    private readonly ILogger<ScimOutboundWorker> _logger;
    private readonly string _workerId = $"{Environment.MachineName}:{Guid.NewGuid():N}"[..Math.Min(128, Environment.MachineName.Length + 33)];
    private DateTime _lastHousekeeping = DateTime.MinValue;

    public ScimOutboundWorker(
        IServiceScopeFactory scopeFactory,
        ScimOptions options,
        ILogger<ScimOutboundWorker> logger)
    {
        _scopeFactory = scopeFactory;
        _options = options;
        _logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        var outbound = _options.Outbound;
        if (!outbound.Enabled)
        {
            _logger.LogInformation("SCIM outbound worker disabled by configuration");
            return;
        }

        try
        {
            await Task.Delay(outbound.InitialDelay, stoppingToken);
        }
        catch (OperationCanceledException)
        {
            return;
        }

        _logger.LogInformation("SCIM outbound worker {WorkerId} started (poll {Poll}s, {Concurrency} providers in parallel)",
            _workerId, outbound.PollInterval.TotalSeconds, outbound.MaxConcurrentProviders);

        while (!stoppingToken.IsCancellationRequested)
        {
            var didWork = false;
            try
            {
                didWork = await RunCycleAsync(stoppingToken);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                break;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "SCIM outbound worker cycle failed");
            }

            try
            {
                await Task.Delay(didWork ? TimeSpan.FromSeconds(1) : outbound.PollInterval, stoppingToken);
            }
            catch (OperationCanceledException)
            {
                break;
            }
        }
    }

    private async Task<bool> RunCycleAsync(CancellationToken cancellationToken)
    {
        var now = DateTime.UtcNow;
        IReadOnlyList<string> dueProviderIds;

        using (var scope = _scopeFactory.CreateScope())
        {
            var outbox = scope.ServiceProvider.GetRequiredService<IScimProviderOutboxStore>();
            var released = await outbox.ReleaseExpiredLocksAsync(now, cancellationToken);
            if (released > 0)
                _logger.LogWarning("SCIM outbound released {Count} expired queue locks", released);

            await RunDueReconcilesAsync(scope.ServiceProvider, now, cancellationToken);
            await RunHousekeepingAsync(scope.ServiceProvider, now, cancellationToken);

            dueProviderIds = await outbox.GetDueProviderIdsAsync(now, 100, cancellationToken);
        }

        if (dueProviderIds.Count == 0)
            return false;

        var processedAny = false;
        using var gate = new SemaphoreSlim(Math.Max(1, _options.Outbound.MaxConcurrentProviders));
        var tasks = dueProviderIds.Select(async providerId =>
        {
            await gate.WaitAsync(cancellationToken);
            try
            {
                if (await ProcessProviderAsync(providerId, cancellationToken))
                    processedAny = true;
            }
            finally
            {
                gate.Release();
            }
        });

        await Task.WhenAll(tasks);
        return processedAny;
    }

    /// <summary>
    /// Processes one provider's queue in order until it is empty, the
    /// per-cycle cap is reached, or the provider is disabled.
    /// </summary>
    private async Task<bool> ProcessProviderAsync(string providerId, CancellationToken cancellationToken)
    {
        var processed = 0;
        Tenant? tenant = null;

        for (var i = 0; i < _options.Outbound.MaxItemsPerProviderPerCycle && !cancellationToken.IsCancellationRequested; i++)
        {
            using var scope = _scopeFactory.CreateScope();
            var services = scope.ServiceProvider;
            var providers = services.GetRequiredService<IScimProviderStore>();
            var outbox = services.GetRequiredService<IScimProviderOutboxStore>();

            var provider = await providers.GetByIdAsync(providerId, cancellationToken);
            if (provider == null || !provider.IsEnabled || string.IsNullOrEmpty(provider.TenantId))
                break;

            if (tenant == null)
            {
                var tenants = services.GetRequiredService<ITenantStore>();
                tenant = await tenants.GetByIdAsync(provider.TenantId, cancellationToken);
                if (tenant == null)
                {
                    _logger.LogWarning("SCIM provider {ProviderId} belongs to unknown tenant {TenantId}; skipping", providerId, provider.TenantId);
                    break;
                }
            }

            var now = DateTime.UtcNow;
            var item = await outbox.ClaimNextAsync(providerId, _workerId, now, _options.Outbound.LockDuration, cancellationToken);
            if (item == null)
                break;

            var tenantAccessor = services.GetRequiredService<ITenantContextAccessor>();
            tenantAccessor.SetTenant(tenant);
            try
            {
                var processor = services.GetRequiredService<IScimOutboundProvisioningService>();
                ScimOutboxProcessResult result;
                try
                {
                    result = await processor.ProcessAsync(item, provider, cancellationToken);
                }
                catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
                {
                    throw;
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "SCIM outbound item {ItemId} threw for provider {ProviderId}", item.Id, providerId);
                    result = ScimOutboxProcessResult.Fail(ex.Message, retryable: true);
                }

                await RecordAsync(outbox, item, result, cancellationToken);
                processed++;
            }
            finally
            {
                tenantAccessor.ClearTenant();
            }
        }

        return processed > 0;
    }

    private async Task RecordAsync(IScimProviderOutboxStore outbox, ScimProviderOutboxItem item, ScimOutboxProcessResult result, CancellationToken cancellationToken)
    {
        if (result.Success)
        {
            await outbox.MarkSucceededAsync(item.Id, result.StatusCode, cancellationToken);
            _logger.LogDebug("SCIM outbound {Operation} {ResourceType} {InternalId} -> provider {ProviderId}: {Note}",
                item.Operation, item.ResourceType, item.InternalId, item.ScimProviderId, result.Note);
            return;
        }

        var error = result.Error ?? "Unknown error";
        if (!result.Retryable || item.Attempts >= _options.Outbound.MaxAttempts)
        {
            await outbox.MarkDeadAsync(item.Id, result.StatusCode, error, cancellationToken);
            _logger.LogWarning("SCIM outbound {Operation} {ResourceType} {InternalId} -> provider {ProviderId} gave up after {Attempts} attempt(s): {Error}",
                item.Operation, item.ResourceType, item.InternalId, item.ScimProviderId, item.Attempts, error);
            return;
        }

        var backoff = Backoff(item.Attempts);
        await outbox.MarkFailedAsync(item.Id, result.StatusCode, error, DateTime.UtcNow.Add(backoff), cancellationToken);
        _logger.LogInformation("SCIM outbound {Operation} {ResourceType} {InternalId} -> provider {ProviderId} failed (attempt {Attempts}), retry in {Backoff}s: {Error}",
            item.Operation, item.ResourceType, item.InternalId, item.ScimProviderId, item.Attempts, (int)backoff.TotalSeconds, error);
    }

    /// <summary>30s, 1m, 2m, 4m ... capped at one hour.</summary>
    internal static TimeSpan Backoff(int attempts)
    {
        var exponent = Math.Clamp(attempts - 1, 0, 8);
        var seconds = 30 * Math.Pow(2, exponent);
        return TimeSpan.FromSeconds(Math.Min(seconds, 3600));
    }

    private async Task RunDueReconcilesAsync(IServiceProvider services, DateTime now, CancellationToken cancellationToken)
    {
        var providers = services.GetRequiredService<IScimProviderStore>();
        var due = await providers.GetReconcileDueAsync(now, cancellationToken);
        if (due.Count == 0)
            return;

        var tenants = services.GetRequiredService<ITenantStore>();
        foreach (var provider in due)
        {
            if (string.IsNullOrEmpty(provider.TenantId))
                continue;

            var tenant = await tenants.GetByIdAsync(provider.TenantId, cancellationToken);
            if (tenant == null)
                continue;

            using var scope = _scopeFactory.CreateScope();
            var accessor = scope.ServiceProvider.GetRequiredService<ITenantContextAccessor>();
            accessor.SetTenant(tenant);
            try
            {
                var scopedProviders = scope.ServiceProvider.GetRequiredService<IScimProviderStore>();
                var scopedProvider = await scopedProviders.GetByIdAsync(provider.Id, cancellationToken);
                if (scopedProvider == null)
                    continue;

                var reconcile = scope.ServiceProvider.GetRequiredService<IScimProviderReconcileService>();
                await reconcile.ReconcileAsync(scopedProvider, cancellationToken);
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                throw;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "SCIM provider {ProviderId} scheduled reconcile failed", provider.Id);
            }
            finally
            {
                accessor.ClearTenant();
            }
        }
    }

    private async Task RunHousekeepingAsync(IServiceProvider services, DateTime now, CancellationToken cancellationToken)
    {
        if (now - _lastHousekeeping < TimeSpan.FromHours(1))
            return;
        _lastHousekeeping = now;

        var outbox = services.GetRequiredService<IScimProviderOutboxStore>();
        var removed = await outbox.DeleteCompletedOlderThanAsync(now - _options.Outbound.CompletedItemRetention, cancellationToken);
        if (removed > 0)
            _logger.LogDebug("SCIM outbound housekeeping removed {Count} completed queue items", removed);

        if (_options.LogRetention is { } retention)
        {
            var logs = services.GetRequiredService<IScimProviderLogStore>();
            await logs.DeleteOlderThanAsync(now - retention, cancellationToken);
        }
    }
}
