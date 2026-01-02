using Azure.Storage.Queues;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Oluso.Core.Events;
using System.Text.Json;

namespace Oluso.Enterprise.AzureWebhooks;

/// <summary>
/// Queue-based webhook retry processor using Azure Storage Queues.
/// Suitable for production multi-instance deployments.
///
/// In this mode, failed webhook deliveries are pushed to an Azure Storage Queue,
/// and an Azure Function or separate worker processes the retries.
/// </summary>
public class QueueWebhookRetryProcessor : IWebhookRetryProcessor
{
    private readonly QueueClient _queueClient;
    private readonly IServiceProvider _serviceProvider;
    private readonly ILogger<QueueWebhookRetryProcessor> _logger;
    private readonly QueueWebhookOptions _options;

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        WriteIndented = false
    };

    public QueueWebhookRetryProcessor(
        QueueClient queueClient,
        IServiceProvider serviceProvider,
        ILogger<QueueWebhookRetryProcessor> logger,
        IOptions<QueueWebhookOptions> options)
    {
        _queueClient = queueClient;
        _serviceProvider = serviceProvider;
        _logger = logger;
        _options = options.Value;
    }

    /// <inheritdoc />
    public async Task QueueRetryAsync(
        string deliveryId,
        DateTime nextRetryAt,
        CancellationToken cancellationToken = default)
    {
        var message = new WebhookRetryMessage
        {
            DeliveryId = deliveryId,
            ScheduledAt = DateTime.UtcNow
        };

        var messageJson = JsonSerializer.Serialize(message, JsonOptions);

        // Calculate visibility timeout (delay until message becomes visible)
        var delay = nextRetryAt - DateTime.UtcNow;
        if (delay < TimeSpan.Zero)
            delay = TimeSpan.Zero;

        // Azure Storage Queues max visibility timeout is 7 days
        if (delay > TimeSpan.FromDays(7))
        {
            _logger.LogWarning(
                "Retry delay {Delay} exceeds 7 days for delivery {DeliveryId}, capping at 7 days",
                delay, deliveryId);
            delay = TimeSpan.FromDays(7);
        }

        await _queueClient.SendMessageAsync(
            messageJson,
            visibilityTimeout: delay,
            cancellationToken: cancellationToken);

        _logger.LogDebug(
            "Queued retry for delivery {DeliveryId} with delay {Delay}",
            deliveryId, delay);
    }

    /// <inheritdoc />
    public async Task<int> ProcessPendingRetriesAsync(CancellationToken cancellationToken = default)
    {
        // In queue mode, this is called by an Azure Function trigger or worker
        // We process messages from the queue

        var processed = 0;
        var messages = await _queueClient.ReceiveMessagesAsync(
            maxMessages: _options.BatchSize,
            cancellationToken: cancellationToken);

        foreach (var message in messages.Value)
        {
            if (cancellationToken.IsCancellationRequested)
                break;

            try
            {
                var retryMessage = JsonSerializer.Deserialize<WebhookRetryMessage>(
                    message.MessageText, JsonOptions);

                if (retryMessage == null)
                {
                    _logger.LogWarning("Invalid message in webhook queue: {MessageId}", message.MessageId);
                    await _queueClient.DeleteMessageAsync(message.MessageId, message.PopReceipt, cancellationToken);
                    continue;
                }

                // Process the retry
                using var scope = _serviceProvider.CreateScope();
                var dispatcher = scope.ServiceProvider.GetRequiredService<IWebhookDispatcher>();
                await dispatcher.RetryDeliveryAsync(retryMessage.DeliveryId, cancellationToken);

                // Delete the message after successful processing
                await _queueClient.DeleteMessageAsync(message.MessageId, message.PopReceipt, cancellationToken);
                processed++;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error processing webhook retry message {MessageId}", message.MessageId);
                // Message will become visible again after visibility timeout
            }
        }

        if (processed > 0)
        {
            _logger.LogInformation("Processed {Count} webhook retries from queue", processed);
        }

        return processed;
    }
}

/// <summary>
/// Options for queue-based webhook processing
/// </summary>
public class QueueWebhookOptions
{
    /// <summary>
    /// Azure Storage connection string.
    /// If not set, uses DefaultAzureCredential with QueueServiceUri.
    /// </summary>
    public string? ConnectionString { get; set; }

    /// <summary>
    /// Queue service URI for DefaultAzureCredential authentication.
    /// Example: https://yourstorageaccount.queue.core.windows.net
    /// </summary>
    public Uri? QueueServiceUri { get; set; }

    /// <summary>
    /// Name of the queue for webhook retries.
    /// </summary>
    public string QueueName { get; set; } = "oluso-webhook-retries";

    /// <summary>
    /// Maximum number of messages to process per batch.
    /// </summary>
    public int BatchSize { get; set; } = 32;

    /// <summary>
    /// Whether to create the queue if it doesn't exist.
    /// </summary>
    public bool CreateQueueIfNotExists { get; set; } = true;
}
