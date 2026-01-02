namespace Oluso.Core.Events;

/// <summary>
/// Abstraction for processing webhook retries.
/// Default implementation uses IHostedService for in-process processing.
/// Enterprise implementations can use queue-based processing (Azure Service Bus, Storage Queues, etc.)
/// </summary>
public interface IWebhookRetryProcessor
{
    /// <summary>
    /// Queue a failed delivery for retry processing.
    /// In-process: directly schedules retry
    /// Queue-based: pushes to message queue for external processing
    /// </summary>
    Task QueueRetryAsync(string deliveryId, DateTime nextRetryAt, CancellationToken cancellationToken = default);

    /// <summary>
    /// Process all pending retries that are due.
    /// Called by the background processor (IHostedService or Azure Function trigger).
    /// </summary>
    Task<int> ProcessPendingRetriesAsync(CancellationToken cancellationToken = default);
}
