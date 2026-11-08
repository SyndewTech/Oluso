namespace Oluso.Telemetry.OpenTelemetry.Tracing;

/// <summary>
/// Distributed-trace DTO returned to the admin UI. Mirrors the frontend
/// <c>Trace</c> model so search/detail responses can map 1:1.
/// </summary>
public class TraceDto
{
    public string TraceId { get; set; } = null!;
    public TraceSpanDto RootSpan { get; set; } = null!;
    public List<TraceSpanDto> Spans { get; set; } = new();
    /// <summary>Trace duration in milliseconds.</summary>
    public double Duration { get; set; }
    public string StartTime { get; set; } = null!;
    public string ServiceName { get; set; } = null!;
    public string Status { get; set; } = "unset";
}

public class TraceSpanDto
{
    public string TraceId { get; set; } = null!;
    public string SpanId { get; set; } = null!;
    public string? ParentSpanId { get; set; }
    public string OperationName { get; set; } = null!;
    public string ServiceName { get; set; } = null!;
    public string StartTime { get; set; } = null!;
    /// <summary>Span duration in milliseconds.</summary>
    public double Duration { get; set; }
    public string Status { get; set; } = "unset";
    public Dictionary<string, string> Tags { get; set; } = new();
    public List<TraceEventDto> Events { get; set; } = new();
}

public class TraceEventDto
{
    public string Name { get; set; } = null!;
    public string Timestamp { get; set; } = null!;
    public Dictionary<string, object>? Attributes { get; set; }
}

public class TraceQuery
{
    public DateTime? StartDate { get; set; }
    public DateTime? EndDate { get; set; }
    public string? ServiceName { get; set; }
    public string? OperationName { get; set; }
    public string? SearchTerm { get; set; }
    public int Page { get; set; } = 1;
    public int PageSize { get; set; } = 20;
}

public class TraceQueryResult
{
    public List<TraceDto> Items { get; set; } = new();
    public int TotalCount { get; set; }
    public int Page { get; set; }
    public int PageSize { get; set; }
    public int TotalPages =>
        PageSize > 0 ? (int)Math.Ceiling((double)TotalCount / PageSize) : 0;
}
