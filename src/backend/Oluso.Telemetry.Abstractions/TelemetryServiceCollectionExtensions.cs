using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace Oluso.Telemetry;

/// <summary>
/// Extension methods for registering telemetry services
/// </summary>
public static class TelemetryServiceCollectionExtensions
{
    /// <summary>
    /// Adds the no-op telemetry implementation.
    /// This is called by default if no other telemetry provider is registered.
    /// </summary>
    public static IServiceCollection AddOlusoNullTelemetry(this IServiceCollection services)
    {
        services.TryAddSingleton<IOlusoTelemetry>(NullOlusoTelemetry.Instance);
        services.TryAddSingleton<IOlusoMetrics>(NullOlusoMetrics.Instance);
        services.TryAddSingleton<IOlusoTracing>(NullOlusoTracing.Instance);

        return services;
    }

    /// <summary>
    /// Ensures telemetry services are registered (falls back to no-op if none configured)
    /// </summary>
    public static IServiceCollection EnsureTelemetryServices(this IServiceCollection services)
    {
        // Only add no-op if no telemetry is registered
        services.TryAddSingleton<IOlusoTelemetry>(NullOlusoTelemetry.Instance);
        services.TryAddSingleton<IOlusoMetrics>(sp =>
            sp.GetRequiredService<IOlusoTelemetry>().Metrics);
        services.TryAddSingleton<IOlusoTracing>(sp =>
            sp.GetRequiredService<IOlusoTelemetry>().Tracing);

        return services;
    }
}
