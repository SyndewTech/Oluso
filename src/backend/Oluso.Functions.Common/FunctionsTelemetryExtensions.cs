using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Oluso.Telemetry;
using Oluso.Telemetry.OpenTelemetry;

namespace Oluso.Functions.Common;

/// <summary>
/// Wires OpenTelemetry into an Azure Functions worker host using the
/// same configuration cascade the core platform uses, so an admin who
/// already has Tempo/Loki running for the API gets the Functions traces
/// and logs in the same place without any extra pipeline steps.
/// </summary>
/// <remarks>
/// Reads the same keys the API reads:
///   <c>Oluso:Telemetry:OtlpEndpoint</c> — traces / metrics / logs OTLP
///   <c>Oluso:Telemetry:TempoEndpoint</c> — read-side traces (admin UI)
///   <c>Oluso:Telemetry:LokiEndpoint</c> — read-side logs (admin UI)
/// When none of those are set the host falls back to the no-op
/// telemetry registration so a local <c>func start</c> still boots
/// cleanly.
/// </remarks>
public static class FunctionsTelemetryExtensions
{
    /// <summary>
    /// Registers OpenTelemetry on the Functions worker DI container.
    /// The function host must call this in <c>ConfigureServices</c>
    /// before any DbContext or HTTP client is registered so the
    /// instrumentation hooks into them.
    /// </summary>
    /// <param name="services">The Functions worker service collection.</param>
    /// <param name="configuration">Function host configuration.</param>
    /// <param name="serviceName">
    /// Resource name used in telemetry (becomes <c>service.name</c>),
    /// e.g. <c>"Oluso.&lt;Module&gt;.Functions"</c>. Pick something that
    /// distinguishes this Functions host from the platform API and from
    /// other Function modules.
    /// </param>
    public static IServiceCollection AddOlusoFunctionsTelemetry(
        this IServiceCollection services,
        IConfiguration configuration,
        string serviceName)
    {
        // Ensure IHttpClientFactory is available — required by the
        // shared HeartbeatFunction's optional phone-home POST. Idempotent
        // when the host has already wired up named HTTP clients.
        services.AddHttpClient();

        var otlpEndpoint = configuration["Oluso:Telemetry:OtlpEndpoint"];
        var tempoEndpoint = configuration["Oluso:Telemetry:TempoEndpoint"];
        var lokiEndpoint = configuration["Oluso:Telemetry:LokiEndpoint"];

        if (string.IsNullOrWhiteSpace(otlpEndpoint)
            && string.IsNullOrWhiteSpace(tempoEndpoint)
            && string.IsNullOrWhiteSpace(lokiEndpoint))
        {
            // Nothing wired — register the no-op so DI resolution still
            // works for code that injects IOlusoTelemetry / IOlusoMetrics.
            services.AddOlusoNullTelemetry();
            return services;
        }

        services.AddOlusoOpenTelemetry(opt =>
        {
            opt.ServiceName = serviceName;
            opt.OtlpEndpoint = otlpEndpoint;
            opt.TempoEndpoint = tempoEndpoint;
            opt.LokiEndpoint = lokiEndpoint;
            // Functions modules are deployed as part of the same Oluso
            // licence as the platform API. Skipping the per-process
            // licence check avoids requiring each Functions host to
            // carry its own licence-validator wiring.
            opt.SkipLicenseValidation = true;
        });

        return services;
    }
}
