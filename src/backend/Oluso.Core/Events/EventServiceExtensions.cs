using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace Oluso.Core.Events;

/// <summary>
/// Extension methods for configuring the Oluso event system.
/// </summary>
public static class EventServiceExtensions
{
    /// <summary>
    /// Adds the core Oluso event service.
    /// </summary>
    public static IServiceCollection AddOlusoEvents(
        this IServiceCollection services,
        Action<OlusoEventOptions>? configure = null)
    {
        // Configure options
        if (configure != null)
        {
            services.Configure(configure);
        }

        // Add the event service
        services.TryAddScoped<IOlusoEventService, OlusoEventService>();

        // Add the webhook registry
        services.TryAddSingleton<IWebhookEventRegistry, WebhookEventRegistry>();

        // Add core webhook event provider
        services.TryAddEnumerable(ServiceDescriptor.Singleton<IWebhookEventProvider, CoreWebhookEventProvider>());

        return services;
    }

    /// <summary>
    /// Adds the logger event sink for development/debugging.
    /// </summary>
    public static IServiceCollection AddLoggerEventSink(this IServiceCollection services)
    {
        services.AddScoped<IOlusoEventSink, LoggerEventSink>();
        return services;
    }

    /// <summary>
    /// Adds a custom event sink.
    /// </summary>
    public static IServiceCollection AddEventSink<TSink>(this IServiceCollection services)
        where TSink : class, IOlusoEventSink
    {
        services.AddScoped<IOlusoEventSink, TSink>();
        return services;
    }

    /// <summary>
    /// Adds a callback-based event sink.
    /// </summary>
    public static IServiceCollection AddEventSink(
        this IServiceCollection services,
        Func<OlusoEvent, Task> handler,
        string? name = null)
    {
        services.AddSingleton<IOlusoEventSink>(new CallbackEventSink(handler, name));
        return services;
    }

    /// <summary>
    /// Adds the webhook event sink that automatically dispatches events with WebhookEventType.
    /// Requires IWebhookDispatcher to be registered.
    /// </summary>
    public static IServiceCollection AddWebhookEventSink(this IServiceCollection services)
    {
        services.AddScoped<IOlusoEventSink, WebhookEventSink>();
        return services;
    }

    /// <summary>
    /// Adds a webhook event provider for custom event definitions.
    /// </summary>
    public static IServiceCollection AddWebhookEventProvider<TProvider>(this IServiceCollection services)
        where TProvider : class, IWebhookEventProvider
    {
        services.AddSingleton<IWebhookEventProvider, TProvider>();
        return services;
    }

    /// <summary>
    /// Adds a webhook payload mapper for custom event-to-payload conversion.
    /// </summary>
    public static IServiceCollection AddWebhookPayloadMapper<TMapper>(this IServiceCollection services)
        where TMapper : class, IWebhookPayloadMapper
    {
        services.AddSingleton<IWebhookPayloadMapper, TMapper>();
        return services;
    }

    /// <summary>
    /// Adds the audit event sink that persists events to the audit log database.
    /// Requires IAuditLogStore to be registered.
    /// </summary>
    public static IServiceCollection AddAuditEventSink<TAuditSink>(this IServiceCollection services)
        where TAuditSink : class, IOlusoEventSink
    {
        services.AddScoped<IOlusoEventSink, TAuditSink>();
        return services;
    }
}
