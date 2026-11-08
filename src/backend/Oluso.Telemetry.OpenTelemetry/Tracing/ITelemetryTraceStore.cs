namespace Oluso.Telemetry.OpenTelemetry.Tracing;

/// <summary>
/// Abstraction over a distributed-trace backing store (Tempo, Jaeger, etc).
/// Implementations must degrade gracefully when the store is unreachable —
/// return empty results, never throw, so the admin UI stays responsive in
/// dev/staging environments where the trace backend isn't deployed.
/// </summary>
public interface ITelemetryTraceStore
{
    /// <summary>
    /// True when a real trace backend is configured. The admin UI uses this
    /// to render "tracing not configured" messaging instead of "no traces".
    /// </summary>
    bool IsConfigured { get; }

    Task<TraceQueryResult> SearchAsync(TraceQuery query, CancellationToken cancellationToken = default);

    Task<TraceDto?> GetByIdAsync(string traceId, CancellationToken cancellationToken = default);
}

/// <summary>
/// Null-object implementation used when no trace backend is configured.
/// Returns empty results from every method so the controller surface is
/// uniform across environments.
/// </summary>
public sealed class NoOpTelemetryTraceStore : ITelemetryTraceStore
{
    public bool IsConfigured => false;

    public Task<TraceQueryResult> SearchAsync(TraceQuery query, CancellationToken cancellationToken = default) =>
        Task.FromResult(new TraceQueryResult
        {
            Items = new List<TraceDto>(),
            TotalCount = 0,
            Page = query.Page,
            PageSize = query.PageSize,
        });

    public Task<TraceDto?> GetByIdAsync(string traceId, CancellationToken cancellationToken = default) =>
        Task.FromResult<TraceDto?>(null);
}
