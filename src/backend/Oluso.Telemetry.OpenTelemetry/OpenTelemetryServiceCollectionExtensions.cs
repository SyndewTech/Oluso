using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using OpenTelemetry.Logs;
using OpenTelemetry.Metrics;
using OpenTelemetry.Resources;
using OpenTelemetry.Trace;
using Oluso.Core.Domain.Interfaces;
using Oluso.Core.Licensing;
using Oluso.Telemetry.OpenTelemetry.Logging;
using Oluso.Telemetry.OpenTelemetry.Tracing;

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
    /// Enable OTLP log export (default: true). When true and a logs endpoint
    /// is resolvable, ILogger output is shipped via OTLP in addition to the
    /// existing in-process database logger. Pro+ licensed.
    /// </summary>
    public bool EnableLogs { get; set; } = true;

    /// <summary>
    /// OTLP exporter endpoint (e.g., "http://localhost:4317")
    /// </summary>
    public string? OtlpEndpoint { get; set; }

    /// <summary>
    /// Dedicated OTLP endpoint for log records. When unset, falls back to
    /// <see cref="OtlpEndpoint"/>. Set this when logs go to a different
    /// receiver than traces/metrics (e.g., Loki on 4319 vs Tempo on 4317).
    /// </summary>
    public string? LogsOtlpEndpoint { get; set; }

    /// <summary>
    /// Grafana Tempo HTTP API endpoint (e.g., "http://tempo:3200") used by
    /// the admin Telemetry → Traces page to read back spans previously
    /// exported via OTLP. When unset, the Traces page renders a graceful
    /// "tracing not configured" notice instead of erroring.
    /// </summary>
    public string? TempoEndpoint { get; set; }

    /// <summary>
    /// Grafana Loki HTTP base URL (e.g., "http://loki:3100") used by the
    /// admin Telemetry → Logs page to read logs previously shipped via
    /// OTLP. When unset, the Logs page falls back to the in-process
    /// database sink (TelemetryLogs table). Loki 3.x's native OTLP
    /// receiver is assumed; older clusters using the Promtail-style
    /// ingest path also work as long as the standard label/structured-
    /// metadata mapping is in place.
    /// </summary>
    public string? LokiEndpoint { get; set; }

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
    /// Configure logging builder (advanced customization)
    /// </summary>
    public Action<OpenTelemetryLoggerOptions>? ConfigureLogs { get; set; }

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

        // Register the trace-read store for the admin UI. Tempo when an
        // endpoint is configured; otherwise a no-op store so the controller
        // can return empty results without null-checking.
        services.Configure<TempoTraceStoreOptions>(opt => opt.Endpoint = options.TempoEndpoint);
        if (!string.IsNullOrWhiteSpace(options.TempoEndpoint))
        {
            services.AddHttpClient<ITelemetryTraceStore, TempoTraceStore>();
        }
        else
        {
            services.AddSingleton<ITelemetryTraceStore, NoOpTelemetryTraceStore>();
        }

        // Register the log-read service for the admin UI. When a Loki
        // endpoint is configured, swap the EF-backed ITelemetryLogService
        // (registered earlier by AddOluso → Entity Framework) for the Loki
        // client so the Logs page reads from the same store the OTLP
        // exporter writes into. Without LokiEndpoint, the EF impl stays as
        // the default and the Logs page reads from the TelemetryLogs table.
        if (!string.IsNullOrWhiteSpace(options.LokiEndpoint))
        {
            services.Configure<LokiLogServiceOptions>(opt => opt.Endpoint = options.LokiEndpoint);
            services.RemoveAll<ITelemetryLogService>();
            services.AddHttpClient<ITelemetryLogService, LokiTelemetryLogService>();
        }

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

        // OTLP log export. The DB logger (Oluso.Telemetry.OpenTelemetry.Logging.DatabaseLoggerProvider)
        // is the free-tier path and stays wired by the consumer; this block adds OTLP shipping
        // on top so logs reach Loki / OpenObserve / OTel Collector / etc. License-gated Pro+
        // — the warning below fires at startup when the endpoint is configured without a
        // valid license, but the pipeline still runs (matches existing metrics/tracing
        // soft-gate behavior).
        var logsEndpoint = string.IsNullOrWhiteSpace(options.LogsOtlpEndpoint)
            ? options.OtlpEndpoint
            : options.LogsOtlpEndpoint;

        if (options.EnableLogs && !string.IsNullOrWhiteSpace(logsEndpoint))
        {
            services.AddHttpContextAccessor();
            services.TryAddSingleton<TenantContextLogProcessor>();

            services.AddOpenTelemetry()
                .WithLogging(builder =>
                {
                    builder
                        .SetResourceBuilder(resourceBuilder)
                        .AddProcessor(sp => sp.GetRequiredService<TenantContextLogProcessor>())
                        .AddOtlpExporter(otlp =>
                        {
                            otlp.Endpoint = new Uri(logsEndpoint!);
                        });

                    if (options.EnableConsoleExporter)
                    {
                        builder.AddConsoleExporter();
                    }
                },
                opts =>
                {
                    opts.IncludeFormattedMessage = true;
                    opts.IncludeScopes = true;
                    opts.ParseStateValues = true;
                    options.ConfigureLogs?.Invoke(opts);
                });

            if (!options.SkipLicenseValidation)
            {
                services.AddHostedService<OtlpLogsLicenseProbe>();
            }
        }

        return services;
    }
}

/// <summary>
/// Surfaces a startup warning when OTLP log export is configured but the
/// running license does not include Telemetry. Doesn't tear the pipeline
/// down — matches the existing soft-gate convention for metrics/tracing.
/// </summary>
internal sealed class OtlpLogsLicenseProbe : IHostedService
{
    private readonly ILicenseValidator? _validator;
    private readonly ILogger<OtlpLogsLicenseProbe> _logger;

    public OtlpLogsLicenseProbe(ILogger<OtlpLogsLicenseProbe> logger, ILicenseValidator? validator = null)
    {
        _logger = logger;
        _validator = validator;
    }

    public Task StartAsync(CancellationToken cancellationToken)
    {
        if (_validator is null) return Task.CompletedTask;

        var result = _validator.ValidateFeature(LicensedFeatures.Telemetry);
        if (!result.IsValid)
        {
            _logger.LogWarning(
                "OTLP log export is configured but the current license does not include Telemetry. " +
                "Logs will continue to ship via OTLP for now; upgrade to Professional+ to keep this enabled. {Message}",
                result.Message);
        }
        return Task.CompletedTask;
    }

    public Task StopAsync(CancellationToken cancellationToken) => Task.CompletedTask;
}
