namespace Oluso.Core.Events;

/// <summary>
/// Dispatcher for webhook events to external subscribers.
/// </summary>
public interface IWebhookDispatcher
{
    /// <summary>
    /// Dispatch a webhook event to all subscribed endpoints for the tenant.
    /// </summary>
    Task<WebhookDispatchResult> DispatchAsync(
        string tenantId,
        string eventType,
        object payload,
        Dictionary<string, string>? metadata = null,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Retry a failed delivery.
    /// </summary>
    Task<bool> RetryDeliveryAsync(string deliveryId, CancellationToken cancellationToken = default);

    /// <summary>
    /// Get all available webhook event types from registered providers.
    /// </summary>
    IEnumerable<WebhookEventDefinition> GetAvailableEventTypes();
}
