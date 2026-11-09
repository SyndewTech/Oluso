using System.Net;
using System.Text.Json;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Oluso.Telemetry.OpenTelemetry.Tracing;

public class TempoTraceStoreOptions
{
    /// <summary>
    /// Tempo HTTP API base URL (e.g. <c>http://tempo:3200</c>). When unset,
    /// the trace store stays disabled and the admin Traces page renders a
    /// "not configured" notice instead of erroring.
    /// </summary>
    public string? Endpoint { get; set; }

    /// <summary>Default search window when caller doesn't specify one.</summary>
    public TimeSpan DefaultLookback { get; set; } = TimeSpan.FromHours(1);

    /// <summary>Hard timeout per Tempo HTTP call.</summary>
    public TimeSpan RequestTimeout { get; set; } = TimeSpan.FromSeconds(10);
}

/// <summary>
/// Queries Grafana Tempo over its HTTP API. Tempo's search returns trace
/// summaries; the trace-by-id endpoint returns the full OTLP JSON which we
/// flatten into <see cref="TraceDto"/> for the admin UI.
///
/// Network failures are caught and surfaced as empty results so the UI
/// degrades gracefully instead of throwing 500s in environments where the
/// trace backend is unreachable.
/// </summary>
public class TempoTraceStore : ITelemetryTraceStore
{
    private static readonly JsonSerializerOptions JsonOpts = new(JsonSerializerDefaults.Web);

    private readonly HttpClient _http;
    private readonly TempoTraceStoreOptions _options;
    private readonly ILogger<TempoTraceStore> _logger;

    public TempoTraceStore(
        HttpClient httpClient,
        IOptions<TempoTraceStoreOptions> options,
        ILogger<TempoTraceStore> logger)
    {
        _http = httpClient;
        _options = options.Value;
        _logger = logger;

        if (!string.IsNullOrWhiteSpace(_options.Endpoint))
        {
            _http.BaseAddress = new Uri(_options.Endpoint.TrimEnd('/') + "/");
            _http.Timeout = _options.RequestTimeout;
        }
    }

    public bool IsConfigured => !string.IsNullOrWhiteSpace(_options.Endpoint);

    public async Task<TraceQueryResult> SearchAsync(TraceQuery query, CancellationToken cancellationToken = default)
    {
        var empty = new TraceQueryResult
        {
            Items = new List<TraceDto>(),
            TotalCount = 0,
            Page = query.Page,
            PageSize = query.PageSize,
        };

        if (!IsConfigured) return empty;

        try
        {
            var (start, end) = ResolveWindow(query);
            var traceQl = BuildTraceQl(query);
            // Over-fetch so we can paginate client-side. Tempo's search
            // doesn't expose offset/limit cursoring in v2 — pagination
            // beyond the first page is approximate.
            var limit = Math.Max(query.PageSize * query.Page, 100);

            var url = $"api/search?q={Uri.EscapeDataString(traceQl)}&start={start}&end={end}&limit={limit}";

            using var response = await _http.GetAsync(url, cancellationToken);
            if (!response.IsSuccessStatusCode)
            {
                _logger.LogWarning(
                    "Tempo search returned {StatusCode}: {Reason}. Returning empty result.",
                    response.StatusCode, response.ReasonPhrase);
                return empty;
            }

            var payload = await response.Content.ReadAsStringAsync(cancellationToken);
            using var doc = JsonDocument.Parse(payload);

            var summaries = new List<TraceDto>();
            if (doc.RootElement.TryGetProperty("traces", out var tracesEl) &&
                tracesEl.ValueKind == JsonValueKind.Array)
            {
                foreach (var t in tracesEl.EnumerateArray())
                {
                    var dto = ParseSearchSummary(t);
                    if (dto != null) summaries.Add(dto);
                }
            }

            // Apply page slice
            var page = Math.Max(1, query.Page);
            var pageSize = Math.Max(1, query.PageSize);
            var items = summaries.Skip((page - 1) * pageSize).Take(pageSize).ToList();

            return new TraceQueryResult
            {
                Items = items,
                TotalCount = summaries.Count,
                Page = page,
                PageSize = pageSize,
            };
        }
        catch (TaskCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            _logger.LogWarning("Tempo search timed out after {Timeout}", _options.RequestTimeout);
            return empty;
        }
        catch (HttpRequestException ex)
        {
            _logger.LogWarning(ex, "Tempo search HTTP error — backend likely unreachable");
            return empty;
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Tempo search failed unexpectedly");
            return empty;
        }
    }

    public async Task<TraceDto?> GetByIdAsync(string traceId, CancellationToken cancellationToken = default)
    {
        if (!IsConfigured || string.IsNullOrWhiteSpace(traceId)) return null;

        try
        {
            using var response = await _http.GetAsync($"api/traces/{Uri.EscapeDataString(traceId)}", cancellationToken);
            if (response.StatusCode == HttpStatusCode.NotFound) return null;
            if (!response.IsSuccessStatusCode)
            {
                _logger.LogWarning(
                    "Tempo trace lookup for {TraceId} returned {StatusCode}", traceId, response.StatusCode);
                return null;
            }

            var payload = await response.Content.ReadAsStringAsync(cancellationToken);
            using var doc = JsonDocument.Parse(payload);
            return ParseTraceDetail(doc.RootElement, traceId);
        }
        catch (TaskCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            _logger.LogWarning("Tempo trace lookup timed out for {TraceId}", traceId);
            return null;
        }
        catch (HttpRequestException ex)
        {
            _logger.LogWarning(ex, "Tempo trace lookup HTTP error for {TraceId}", traceId);
            return null;
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Tempo trace lookup failed for {TraceId}", traceId);
            return null;
        }
    }

    // ────────── helpers ──────────

    private (long start, long end) ResolveWindow(TraceQuery query)
    {
        var endDt = query.EndDate ?? DateTime.UtcNow;
        var startDt = query.StartDate ?? endDt - _options.DefaultLookback;
        // Tempo expects unix seconds for start/end query params.
        return (
            new DateTimeOffset(DateTime.SpecifyKind(startDt, DateTimeKind.Utc)).ToUnixTimeSeconds(),
            new DateTimeOffset(DateTime.SpecifyKind(endDt, DateTimeKind.Utc)).ToUnixTimeSeconds());
    }

    private static string BuildTraceQl(TraceQuery query)
    {
        // TraceQL filters live inside { ... }. With no filters, "{}" matches any trace.
        var clauses = new List<string>();
        if (!string.IsNullOrWhiteSpace(query.ServiceName))
            clauses.Add($"resource.service.name=\"{Escape(query.ServiceName)}\"");
        if (!string.IsNullOrWhiteSpace(query.OperationName))
            clauses.Add($"name=\"{Escape(query.OperationName)}\"");
        if (!string.IsNullOrWhiteSpace(query.SearchTerm))
            clauses.Add($"name=~\".*{Escape(query.SearchTerm)}.*\"");
        return clauses.Count == 0 ? "{}" : "{ " + string.Join(" && ", clauses) + " }";
    }

    private static string Escape(string s) => s.Replace("\"", "\\\"");

    private static TraceDto? ParseSearchSummary(JsonElement t)
    {
        if (!t.TryGetProperty("traceID", out var traceIdEl)) return null;

        var traceId = traceIdEl.GetString() ?? "";
        var rootService = t.TryGetProperty("rootServiceName", out var rs) ? rs.GetString() ?? "" : "";
        var rootName = t.TryGetProperty("rootTraceName", out var rn) ? rn.GetString() ?? "" : "";
        var startNano = t.TryGetProperty("startTimeUnixNano", out var sn) && sn.ValueKind == JsonValueKind.String
            ? long.TryParse(sn.GetString(), out var v) ? v : 0L
            : 0L;
        var durationMs = t.TryGetProperty("durationMs", out var d) && d.ValueKind == JsonValueKind.Number
            ? d.GetDouble()
            : 0;

        var startTime = NanoToIso(startNano);
        var rootSpan = new TraceSpanDto
        {
            TraceId = traceId,
            SpanId = "",
            OperationName = rootName,
            ServiceName = rootService,
            StartTime = startTime,
            Duration = durationMs,
            Status = "unset",
        };

        return new TraceDto
        {
            TraceId = traceId,
            RootSpan = rootSpan,
            Spans = new List<TraceSpanDto> { rootSpan },
            Duration = durationMs,
            StartTime = startTime,
            ServiceName = rootService,
            Status = "unset",
        };
    }

    private static TraceDto? ParseTraceDetail(JsonElement payloadRoot, string traceId)
    {
        // Tempo returns OTLP JSON — { batches: [{ resource, scopeSpans: [{ scope, spans: [...] }] }] }
        if (!payloadRoot.TryGetProperty("batches", out var batches) ||
            batches.ValueKind != JsonValueKind.Array)
        {
            return null;
        }

        var allSpans = new List<TraceSpanDto>();
        foreach (var batch in batches.EnumerateArray())
        {
            var serviceName = ExtractServiceName(batch);
            if (!batch.TryGetProperty("scopeSpans", out var scopeSpans) ||
                scopeSpans.ValueKind != JsonValueKind.Array)
            {
                continue;
            }

            foreach (var scopeSpan in scopeSpans.EnumerateArray())
            {
                if (!scopeSpan.TryGetProperty("spans", out var spansEl) ||
                    spansEl.ValueKind != JsonValueKind.Array)
                {
                    continue;
                }

                foreach (var spanEl in spansEl.EnumerateArray())
                {
                    var span = ParseSpan(spanEl, serviceName);
                    if (span != null) allSpans.Add(span);
                }
            }
        }

        if (allSpans.Count == 0) return null;

        // Root span = no parent (or parent not in this trace's span set).
        var spanIds = allSpans.Select(s => s.SpanId).ToHashSet();
        var rootSpan = allSpans.FirstOrDefault(s =>
            string.IsNullOrEmpty(s.ParentSpanId) || !spanIds.Contains(s.ParentSpanId!))
            ?? allSpans.OrderBy(s => s.StartTime).First();

        // Trace duration = root span duration; trace start = root start.
        var traceStatus = allSpans.Any(s => s.Status == "error") ? "error" : "ok";

        return new TraceDto
        {
            TraceId = traceId,
            RootSpan = rootSpan,
            Spans = allSpans,
            Duration = rootSpan.Duration,
            StartTime = rootSpan.StartTime,
            ServiceName = rootSpan.ServiceName,
            Status = traceStatus,
        };
    }

    private static TraceSpanDto? ParseSpan(JsonElement spanEl, string serviceName)
    {
        var traceId = GetStringOrEmpty(spanEl, "traceId");
        var spanId = GetStringOrEmpty(spanEl, "spanId");
        if (string.IsNullOrEmpty(spanId)) return null;

        var parentSpanId = spanEl.TryGetProperty("parentSpanId", out var ps)
            ? ps.GetString() : null;
        if (string.IsNullOrEmpty(parentSpanId)) parentSpanId = null;

        var name = GetStringOrEmpty(spanEl, "name");
        var startNano = ParseNano(spanEl, "startTimeUnixNano");
        var endNano = ParseNano(spanEl, "endTimeUnixNano");
        var durationMs = endNano > startNano ? (endNano - startNano) / 1_000_000.0 : 0.0;

        var status = "unset";
        if (spanEl.TryGetProperty("status", out var statusEl) &&
            statusEl.TryGetProperty("code", out var codeEl) &&
            codeEl.ValueKind == JsonValueKind.Number)
        {
            // OTLP: 0=unset, 1=ok, 2=error
            status = codeEl.GetInt32() switch { 1 => "ok", 2 => "error", _ => "unset" };
        }

        var tags = new Dictionary<string, string>();
        if (spanEl.TryGetProperty("attributes", out var attrs) && attrs.ValueKind == JsonValueKind.Array)
        {
            foreach (var a in attrs.EnumerateArray())
            {
                var key = GetStringOrEmpty(a, "key");
                if (string.IsNullOrEmpty(key)) continue;
                if (a.TryGetProperty("value", out var v))
                    tags[key] = ExtractAnyValue(v);
            }
        }

        var events = new List<TraceEventDto>();
        if (spanEl.TryGetProperty("events", out var evs) && evs.ValueKind == JsonValueKind.Array)
        {
            foreach (var ev in evs.EnumerateArray())
            {
                var evName = GetStringOrEmpty(ev, "name");
                var evNano = ParseNano(ev, "timeUnixNano");
                events.Add(new TraceEventDto
                {
                    Name = evName,
                    Timestamp = NanoToIso(evNano),
                });
            }
        }

        return new TraceSpanDto
        {
            TraceId = traceId,
            SpanId = spanId,
            ParentSpanId = parentSpanId,
            OperationName = name,
            ServiceName = serviceName,
            StartTime = NanoToIso(startNano),
            Duration = durationMs,
            Status = status,
            Tags = tags,
            Events = events,
        };
    }

    private static string ExtractServiceName(JsonElement batch)
    {
        if (!batch.TryGetProperty("resource", out var resource)) return "";
        if (!resource.TryGetProperty("attributes", out var attrs) ||
            attrs.ValueKind != JsonValueKind.Array)
        {
            return "";
        }
        foreach (var a in attrs.EnumerateArray())
        {
            if (GetStringOrEmpty(a, "key") == "service.name" &&
                a.TryGetProperty("value", out var v))
            {
                return ExtractAnyValue(v);
            }
        }
        return "";
    }

    private static string ExtractAnyValue(JsonElement value)
    {
        if (value.TryGetProperty("stringValue", out var s)) return s.GetString() ?? "";
        if (value.TryGetProperty("intValue", out var i)) return i.ToString();
        if (value.TryGetProperty("boolValue", out var b)) return b.GetBoolean().ToString();
        if (value.TryGetProperty("doubleValue", out var d)) return d.GetDouble().ToString();
        return value.ToString();
    }

    private static long ParseNano(JsonElement el, string property)
    {
        if (!el.TryGetProperty(property, out var p)) return 0;
        if (p.ValueKind == JsonValueKind.String && long.TryParse(p.GetString(), out var v)) return v;
        if (p.ValueKind == JsonValueKind.Number) return p.GetInt64();
        return 0;
    }

    private static string GetStringOrEmpty(JsonElement el, string property) =>
        el.TryGetProperty(property, out var p) && p.ValueKind == JsonValueKind.String
            ? p.GetString() ?? ""
            : "";

    private static string NanoToIso(long nano)
    {
        if (nano <= 0) return DateTime.UtcNow.ToString("O");
        var ms = nano / 1_000_000;
        return DateTimeOffset.FromUnixTimeMilliseconds(ms).UtcDateTime.ToString("O");
    }
}
