using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Oluso.Core.Services;

namespace Oluso.Jobs;

/// <summary>
/// Keeps signing keys healthy without an admin watching: marks keys past their
/// expiry as Expired, rotates keys ahead of expiry per each tenant's
/// <c>KeyRotationConfig</c>, and archives or deletes keys beyond the grace
/// period. The on-demand auto-provisioning in the credential store remains the
/// last-resort fallback; this job makes sure it is not the plan.
/// Every step is idempotent, so running on each API instance is safe.
/// </summary>
public class SigningKeyMaintenanceBackgroundService : BackgroundService
{
    private static readonly TimeSpan Interval = TimeSpan.FromHours(1);
    private static readonly TimeSpan InitialDelay = TimeSpan.FromMinutes(1);

    private readonly IServiceScopeFactory _scopeFactory;
    private readonly IOptions<OlusoOptions> _options;
    private readonly ILogger<SigningKeyMaintenanceBackgroundService> _logger;

    public SigningKeyMaintenanceBackgroundService(
        IServiceScopeFactory scopeFactory,
        IOptions<OlusoOptions> options,
        ILogger<SigningKeyMaintenanceBackgroundService> logger)
    {
        _scopeFactory = scopeFactory;
        _options = options;
        _logger = logger;
    }

    /// <summary>Gives the platform a signing key before the server takes requests.</summary>
    public override async Task StartAsync(CancellationToken cancellationToken)
    {
        using (var scope = _scopeFactory.CreateScope())
        {
            await scope.ServiceProvider.GetRequiredService<ISigningKeyService>().EnsurePlatformSigningKeyAsync(cancellationToken);
        }
        await base.StartAsync(cancellationToken);
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        if (!_options.Value.RunSigningKeyMaintenanceInProcess)
        {
            _logger.LogInformation("In-process signing key maintenance is off; expecting the maintenance callback to drive it");
            return;
        }

        _logger.LogInformation("Signing key maintenance started (hourly: expire, rotate ahead of expiry, clean up)");

        try
        {
            await Task.Delay(InitialDelay, stoppingToken);
        }
        catch (OperationCanceledException)
        {
            return;
        }

        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                using var scope = _scopeFactory.CreateScope();
                var keyService = scope.ServiceProvider.GetRequiredService<ISigningKeyService>();
                await keyService.ProcessScheduledRotationsAsync(stoppingToken);

                var purger = scope.ServiceProvider.GetService<Oluso.Core.Services.IExpiredRecordsPurger>();
                if (purger is not null) await purger.PurgeAsync(stoppingToken);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                break;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Signing key maintenance failed; will retry next interval");
            }

            try
            {
                await Task.Delay(Interval, stoppingToken);
            }
            catch (OperationCanceledException)
            {
                break;
            }
        }
    }
}
