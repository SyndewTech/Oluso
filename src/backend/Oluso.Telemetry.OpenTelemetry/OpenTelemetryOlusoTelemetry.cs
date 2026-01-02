namespace Oluso.Telemetry.OpenTelemetry;

/// <summary>
/// OpenTelemetry implementation of the combined telemetry service
/// </summary>
public class OpenTelemetryOlusoTelemetry : IOlusoTelemetry
{
    public OpenTelemetryOlusoTelemetry(
        IOlusoMetrics metrics,
        IOlusoTracing tracing)
    {
        Metrics = metrics;
        Tracing = tracing;
    }

    public IOlusoMetrics Metrics { get; }
    public IOlusoTracing Tracing { get; }
    public bool IsEnabled => true;
}
