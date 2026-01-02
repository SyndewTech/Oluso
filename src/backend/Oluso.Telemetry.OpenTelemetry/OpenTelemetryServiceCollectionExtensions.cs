using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using OpenTelemetry.Metrics;
using OpenTelemetry.Resources;
using OpenTelemetry.Trace;
using Oluso.Core.Licensing;

namespace Oluso.Telemetry.OpenTelemetry;

/// <summary>
/// Configuration options for OpenTelemetry telemetry
/// </summary>
public class OlusoOpenTelemetryOptions
{
    /// <summary>
    /// Service name for telemetry (default: "Oluso")
    /// </summary>
    public string ServiceName { get; set; } = "Oluso";

    /// <summary>
    /// Service version (default: "1.0.0")
    /// </summary>
    public string ServiceVersion { get; set; } = "1.0.0";

    /// <summary>
    /// Enable metrics collection (default: true)
    /// </summary>
    public bool EnableMetrics { get; set; } = true;

    /// <summary>
    /// Enable distributed tracing (default: true)
    /// </summary>
    public bool EnableTracing { get; set; } = true;

    /// <summary>
    /// OTLP exporter endpoint (e.g., "http://localhost:4317")
    /// </summary>
    public string? OtlpEndpoint { get; set; }

    /// <summary>
    /// Enable console exporter for development (default: false)
    /// </summary>
    public bool EnableConsoleExporter { get; set; }

    /// <summary>
    /// Additional resource attributes
    /// </summary>
    public Dictionary<string, object> ResourceAttributes { get; set; } = new();

    /// <summary>
    /// Configure metrics builder (advanced customization)
    /// </summary>
    public Action<MeterProviderBuilder>? ConfigureMetrics { get; set; }

    /// <summary>
    /// Configure tracing builder (advanced customization)
    /// </summary>
    public Action<TracerProviderBuilder>? ConfigureTracing { get; set; }

    /// <summary>
    /// Skip license validation (for development/testing only).
    /// In production, telemetry requires a Pro+ license.
    /// </summary>
    public bool SkipLicenseValidation { get; set; }
}

/// <summary>
/// Extension methods for registering OpenTelemetry telemetry services.
///
/// Telemetry is a Pro+ feature. Requires Professional or higher license.
/// </summary>
public static class OpenTelemetryServiceCollectionExtensions
{
    /// <summary>
    /// Adds OpenTelemetry telemetry to Oluso.
    /// Requires Professional or higher license unless SkipLicenseValidation is true.
    /// </summary>
    public static IServiceCollection AddOlusoOpenTelemetry(
        this IServiceCollection services,
        Action<OlusoOpenTelemetryOptions>? configure = null)
    {
        var options = new OlusoOpenTelemetryOptions();
        configure?.Invoke(options);

        // Validate license (deferred to runtime via factory)
        if (!options.SkipLicenseValidation)
        {
            services.AddSingleton<IOlusoTelemetry>(sp =>
            {
                var licenseValidator = sp.GetService<ILicenseValidator>();
                if (licenseValidator != null)
                {
                    var result = licenseValidator.ValidateFeature(LicensedFeatures.Telemetry);
                    if (!result.IsValid)
                    {
                        var logger = sp.GetService<ILogger<OpenTelemetryOlusoTelemetry>>();
                        logger?.LogWarning(
                            "Telemetry feature requires Pro+ license. Using no-op telemetry. {Message}",
                            result.Message);
                        return NullOlusoTelemetry.Instance;
                    }
                }
                return new OpenTelemetryOlusoTelemetry(
                    sp.GetRequiredService<IOlusoMetrics>(),
                    sp.GetRequiredService<IOlusoTracing>());
            });
        }
        else
        {
            services.AddSingleton<IOlusoTelemetry, OpenTelemetryOlusoTelemetry>();
        }

        // Register metrics and tracing implementations
        services.AddSingleton<IOlusoMetrics, OpenTelemetryMetrics>();
        services.AddSingleton<IOlusoTracing, OpenTelemetryTracing>();

        // Build resource
        var resourceBuilder = ResourceBuilder.CreateDefault()
            .AddService(
                serviceName: options.ServiceName,
                serviceVersion: options.ServiceVersion)
            .AddAttributes(options.ResourceAttributes);

        // Configure OpenTelemetry
        if (options.EnableMetrics)
        {
            services.AddOpenTelemetry()
                .WithMetrics(builder =>
                {
                    builder
                        .SetResourceBuilder(resourceBuilder)
                        .AddMeter(OpenTelemetryMetrics.MeterName)
                        .AddAspNetCoreInstrumentation()
                        .AddHttpClientInstrumentation();

                    if (!string.IsNullOrEmpty(options.OtlpEndpoint))
                    {
                        builder.AddOtlpExporter(otlp =>
                        {
                            otlp.Endpoint = new Uri(options.OtlpEndpoint);
                        });
                    }

                    if (options.EnableConsoleExporter)
                    {
                        builder.AddConsoleExporter();
                    }

                    options.ConfigureMetrics?.Invoke(builder);
                });
        }

        if (options.EnableTracing)
        {
            services.AddOpenTelemetry()
                .WithTracing(builder =>
                {
                    builder
                        .SetResourceBuilder(resourceBuilder)
                        .AddSource(OpenTelemetryTracing.ActivitySource.Name)
                        .AddAspNetCoreInstrumentation(opts =>
                        {
                            opts.RecordException = true;
                        })
                        .AddHttpClientInstrumentation();

                    if (!string.IsNullOrEmpty(options.OtlpEndpoint))
                    {
                        builder.AddOtlpExporter(otlp =>
                        {
                            otlp.Endpoint = new Uri(options.OtlpEndpoint);
                        });
                    }

                    if (options.EnableConsoleExporter)
                    {
                        builder.AddConsoleExporter();
                    }

                    options.ConfigureTracing?.Invoke(builder);
                });
        }

        return services;
    }
}
