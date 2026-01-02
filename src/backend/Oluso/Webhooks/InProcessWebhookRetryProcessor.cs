using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Oluso.Core.Events;

namespace Oluso.Webhooks;

/// <summary>
/// Default in-process webhook retry processor using IHostedService.
/// Suitable for development and single-instance deployments.
/// For production multi-instance deployments, use queue-based processing from Oluso.Enterprise.
/// </summary>
public class InProcessWebhookRetryProcessor : BackgroundService, IWebhookRetryProcessor
{
    private readonly IServiceProvider _serviceProvider;
    private readonly ILogger<InProcessWebhookRetryProcessor> _logger;
    private readonly WebhookRetryOptions _options;

    public InProcessWebhookRetryProcessor(
        IServiceProvider serviceProvider,
        ILogger<InProcessWebhookRetryProcessor> logger,
        IOptions<WebhookRetryOptions> options)
    {
        _serviceProvider = serviceProvider;
        _logger = logger;
        _options = options.Value;
    }

    /// <inheritdoc />
    public Task QueueRetryAsync(string deliveryId, DateTime nextRetryAt, CancellationToken cancellationToken = default)
    {
        // In-process mode: retries are already scheduled in the database via NextRetryAt
        // The background service will pick them up on the next processing cycle
        _logger.LogDebug(
            "Retry queued for delivery {DeliveryId} at {NextRetryAt}",
            deliveryId, nextRetryAt);
        return Task.CompletedTask;
    }

    /// <inheritdoc />
    public async Task<int> ProcessPendingRetriesAsync(CancellationToken cancellationToken = default)
    {
        using var scope = _serviceProvider.CreateScope();
        var dispatcher = scope.ServiceProvider.GetRequiredService<WebhookDispatcher>();

        return await dispatcher.ProcessPendingRetriesAsync(cancellationToken);
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        if (!_options.EnableInProcessRetries)
        {
            _logger.LogInformation("In-process webhook retry processing is disabled");
            return;
        }

        _logger.LogInformation(
            "Starting webhook retry processor with interval {Interval}",
            _options.ProcessingInterval);

        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                await Task.Delay(_options.ProcessingInterval, stoppingToken);

                var processed = await ProcessPendingRetriesAsync(stoppingToken);

                if (processed > 0)
                {
                    _logger.LogDebug("Processed {Count} webhook retries", processed);
                }
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                // Normal shutdown
                break;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error processing webhook retries");
                // Continue processing after delay
            }
        }

        _logger.LogInformation("Webhook retry processor stopped");
    }
}
