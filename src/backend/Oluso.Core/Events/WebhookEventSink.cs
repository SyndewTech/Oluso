using Microsoft.Extensions.Logging;

namespace Oluso.Core.Events;

/// <summary>
/// Event sink that automatically dispatches events as webhooks.
/// Only dispatches events that have a WebhookEventType defined.
/// Uses IWebhookPayloadMapper instances to convert events to external-safe payloads.
/// </summary>
public class WebhookEventSink : IOlusoEventSink
{
    private readonly IWebhookDispatcher _dispatcher;
    private readonly IEnumerable<IWebhookPayloadMapper> _mappers;
    private readonly IEnumerable<IEventPropertySource> _propertySources;
    private readonly ILogger<WebhookEventSink> _logger;

    public string Name => "Webhook";

    public WebhookEventSink(
        IWebhookDispatcher dispatcher,
        IEnumerable<IWebhookPayloadMapper> mappers,
        IEnumerable<IEventPropertySource> propertySources,
        ILogger<WebhookEventSink> logger)
    {
        _dispatcher = dispatcher;
        _mappers = mappers;
        _propertySources = propertySources;
        _logger = logger;
    }

    public async Task HandleAsync(OlusoEvent evt, CancellationToken cancellationToken = default)
    {
        // Only dispatch if the event has a webhook event type
        if (string.IsNullOrEmpty(evt.WebhookEventType))
            return;

        // Skip if no tenant context
        if (string.IsNullOrEmpty(evt.TenantId))
        {
            _logger.LogDebug(
                "Skipping webhook for {EventType} - no tenant context",
                evt.EventType);
            return;
        }

        try
        {
            // Map the event to external payload
            var payloadData = MapToPayloadData(evt);

            var result = await _dispatcher.DispatchAsync(
                evt.TenantId,
                evt.WebhookEventType,
                payloadData,
                new Dictionary<string, string>
                {
                    ["event_id"] = evt.Id,
                    ["activity_id"] = evt.ActivityId ?? ""
                },
                evt.Origin,
                cancellationToken);

            if (result.Success)
            {
                _logger.LogDebug(
                    "Dispatched webhook {WebhookEventType} to {Count} endpoints",
                    evt.WebhookEventType, result.EndpointsNotified);
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex,
                "Failed to dispatch webhook for event {EventType}",
                evt.EventType);
            // Don't rethrow - webhook failures shouldn't break the event flow
        }
    }

    private object MapToPayloadData(OlusoEvent evt)
    {
        // Try custom mappers first
        foreach (var mapper in _mappers)
        {
            if (mapper.CanMap(evt))
            {
                return mapper.MapToPayloadData(evt);
            }
        }

        // Fall back to default mapping
        return DefaultMapper(evt);
    }

    /// <summary>
    /// Default mapping using generated extractors — no manual switch expression.
    /// Falls back to basic event info if no source can extract the event.
    /// </summary>
    private object DefaultMapper(OlusoEvent evt)
    {
        foreach (var source in _propertySources)
        {
            var data = source.ExtractData(evt);
            if (data != null)
            {
                // Add standard fields
                data["event_type"] = evt.EventType;
                data["timestamp"] = evt.Timestamp;
                return data;
            }
        }

        // Generic fallback for unknown event types
        return new
        {
            event_type = evt.EventType,
            timestamp = evt.Timestamp
        };
    }
}
