namespace Oluso.Core.Events;

/// <summary>
/// Registry that aggregates all webhook event providers.
/// Used to discover all available webhook event types.
/// </summary>
public interface IWebhookEventRegistry
{
    /// <summary>
    /// Get all registered providers
    /// </summary>
    IReadOnlyList<IWebhookEventProvider> GetProviders();

    /// <summary>
    /// Get all available event definitions across all providers
    /// </summary>
    IReadOnlyList<WebhookEventDefinition> GetAllEventDefinitions();

    /// <summary>
    /// Get event definitions grouped by category
    /// </summary>
    IReadOnlyDictionary<string, IReadOnlyList<WebhookEventDefinition>> GetEventsByCategory();

    /// <summary>
    /// Get a specific event definition by type
    /// </summary>
    WebhookEventDefinition? GetEventDefinition(string eventType);

    /// <summary>
    /// Check if an event type is valid
    /// </summary>
    bool IsValidEventType(string eventType);
}
