using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Oluso.Core.Licensing;

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
        services.AddHttpContextAccessor();
        services.TryAddSingleton<IEventOriginResolver, HttpContextEventOriginResolver>();

        // Add the webhook registry
        services.TryAddSingleton<IWebhookEventRegistry, WebhookEventRegistry>();

        // Add event type resolver (used by EventRelayController for deserialization)
        services.TryAddSingleton<EventTypeRegistry>();

        // Add core event property source (auto-generated, also registers IWebhookEventProvider)
        services.AddEventPropertySource<CoreEventPropertySource>();

        return services;
    }

    /// <summary>
    /// Adds just the event service without webhook registry, property sources, or providers.
    /// Use this in Azure Functions or standalone apps that only need event forwarding.
    /// Pair with <see cref="EventForwardingExtensions.AddEventForwarding"/> to relay events to the platform.
    /// </summary>
    public static IServiceCollection AddOlusoEventsMinimal(this IServiceCollection services)
    {
        services.TryAddScoped<IOlusoEventService, OlusoEventService>();
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
        // Once only: AddOlusoWebhooks registers it too, and two would send every webhook twice.
        services.TryAddEnumerable(ServiceDescriptor.Scoped<IOlusoEventSink, WebhookEventSink>());
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
    /// Registers an auto-generated event property source.
    /// The source also implements <see cref="IWebhookEventProvider"/>, so this single call
    /// registers both event properties and webhook event definitions for the module.
    /// </summary>
    public static IServiceCollection AddEventPropertySource<TSource>(this IServiceCollection services)
        where TSource : class, IEventPropertySource, IWebhookEventProvider
    {
        services.TryAddEnumerable(ServiceDescriptor.Singleton<IEventPropertySource, TSource>());
        services.TryAddEnumerable(ServiceDescriptor.Singleton<IWebhookEventProvider, TSource>());
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

    /// <summary>
    /// Adds the tenant seeding event sink that seeds default data for new tenants.
    /// Listens for AdminTenantCreatedEvent and calls SeedTenantAsync on registered modules.
    /// </summary>
    public static IServiceCollection AddTenantSeeding(this IServiceCollection services)
    {
        services.AddScoped<IOlusoEventSink, TenantSeedEventSink>();
        services.AddScoped<IOlusoEventSink, FeatureEnabledSeedSink>();
        return services;
    }
}
