using System.Collections.Concurrent;
using System.Text.Json;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Oluso.Core.Events;

/// <summary>
/// Configuration for event forwarding from standalone apps to the platform.
/// </summary>
public class EventForwardingOptions
{
    /// <summary>
    /// The platform's event relay endpoint URL (e.g., "https://platform.example.com/api/events/relay").
    /// </summary>
    public string PlatformUrl { get; set; } = "";

    /// <summary>
    /// API key for authenticating with the platform relay endpoint.
    /// </summary>
    public string ApiKey { get; set; } = "";
}

/// <summary>
/// Wire format for forwarded events.
/// </summary>
public class EventEnvelope
{
    public string EventType { get; set; } = "";
    public JsonElement Payload { get; set; }
}

/// <summary>
/// Event sink that forwards events to the platform via HTTP.
/// Used by standalone apps (e.g., OlusoMarketplace.Web) to bridge events
/// back to the platform where other modules can act on them.
/// </summary>
public class HttpEventForwardingSink : IOlusoEventSink
{
    private readonly HttpClient _httpClient;
    private readonly EventForwardingOptions _options;
    private readonly ILogger<HttpEventForwardingSink> _logger;

    public string Name => "HttpEventForwarding";

    public HttpEventForwardingSink(
        IHttpClientFactory httpClientFactory,
        IOptions<EventForwardingOptions> options,
        ILogger<HttpEventForwardingSink> logger)
    {
        _httpClient = httpClientFactory.CreateClient("EventForwarding");
        _options = options.Value;
        _logger = logger;
    }

    public async Task HandleAsync(OlusoEvent evt, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrEmpty(_options.PlatformUrl))
            return;

        try
        {
            var envelope = new EventEnvelope
            {
                EventType = evt.EventType,
                Payload = JsonSerializer.SerializeToElement(evt, evt.GetType())
            };

            using var request = new HttpRequestMessage(HttpMethod.Post, _options.PlatformUrl);
            request.Content = new StringContent(
                JsonSerializer.Serialize(envelope),
                System.Text.Encoding.UTF8,
                "application/json");

            if (!string.IsNullOrEmpty(_options.ApiKey))
                request.Headers.TryAddWithoutValidation("X-Event-Relay-Key", _options.ApiKey);

            var response = await _httpClient.SendAsync(request, cancellationToken);

            if (!response.IsSuccessStatusCode)
            {
                _logger.LogWarning(
                    "Event forwarding failed for {EventType}: {StatusCode}",
                    evt.EventType, response.StatusCode);
            }
        }
        catch (Exception ex)
        {
            // Don't let forwarding failures break the local operation
            _logger.LogWarning(ex, "Failed to forward event {EventType} to platform", evt.EventType);
        }
    }
}

/// <summary>
/// Resolves OlusoEvent concrete types by name using generated IEventPropertySource instances.
/// No assembly scanning reflection — uses compile-time type resolution.
/// </summary>
public class EventTypeRegistry
{
    private readonly IEnumerable<IEventPropertySource> _sources;

    public EventTypeRegistry(IEnumerable<IEventPropertySource> sources)
    {
        _sources = sources;
    }

    public Type? Resolve(string eventTypeName)
    {
        foreach (var source in _sources)
        {
            var type = source.ResolveEventType(eventTypeName);
            if (type != null) return type;
        }
        return null;
    }
}

/// <summary>
/// Extension methods for event forwarding registration.
/// </summary>
public static class EventForwardingExtensions
{
    /// <summary>
    /// Adds the HTTP event forwarding sink for standalone apps.
    /// Events are forwarded to the platform's relay endpoint.
    /// </summary>
    public static IServiceCollection AddEventForwarding(
        this IServiceCollection services,
        Action<EventForwardingOptions> configure)
    {
        // Ensure the event service is registered (lightweight — no webhook registry or providers).
        // TryAdd so the full platform's AddOlusoEvents() takes precedence if already registered.
        services.TryAddScoped<IOlusoEventService, OlusoEventService>();

        services.Configure(configure);
        services.AddScoped<IOlusoEventSink, HttpEventForwardingSink>();
        return services;
    }
}

