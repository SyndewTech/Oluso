using System.Globalization;
using System.Net;
using System.Text.Json;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Oluso.Core.Domain.Interfaces;

namespace Oluso.Telemetry.OpenTelemetry.Logging;

public class LokiLogServiceOptions
{
    /// <summary>
    /// Loki HTTP base URL (e.g. <c>http://loki:3100</c>). When unset, the
    /// service falls back to <see cref="ITelemetryLogService.IsEnabled"/>=false
    /// and the admin Logs page renders empty results without erroring.
    /// </summary>
    public string? Endpoint { get; set; }

    /// <summary>Default search window when caller doesn't specify one.</summary>
    public TimeSpan DefaultLookback { get; set; } = TimeSpan.FromHours(1);

    /// <summary>Hard timeout per Loki HTTP call.</summary>
    public TimeSpan RequestTimeout { get; set; } = TimeSpan.FromSeconds(15);

    /// <summary>
    /// Hard cap Loki accepts on a single query_range call. Loki defaults to
    /// 5000; we stay conservative because the admin UI rarely needs more
    /// than the most recent few hundred lines per page.
    /// </summary>
    public int MaxLimit { get; set; } = 1000;

    /// <summary>
    /// Base LogQL stream selector. Loki requires at least one stream
    /// selector before pipe filters; this matches every stream that has a
    /// <c>service_name</c> label (the canonical OTLP-native promotion of
    /// <c>service.name</c> in Loki 3.x).
    /// </summary>
    public string BaseSelector { get; set; } = "{service_name=~\".+\"}";

    /// <summary>
    /// Loki label whose values should populate the admin UI's "category"
    /// dropdown. The .NET ILogger category becomes a structured-metadata
    /// field by default — installations that promoted it to a label can
    /// switch this to e.g. <c>logger</c> or <c>category</c>.
    /// </summary>
    public string CategoryLabel { get; set; } = "category";
}

/// <summary>
/// Reads logs from Grafana Loki via its HTTP API. Maps Loki's stream-and-
/// values response onto the <see cref="TelemetryLog"/> shape the admin UI
/// already understands so the Logs page swaps backend transparently.
///
/// Loki has no integer ID and no app-side retention — <see cref="GetByIdAsync"/>
/// returns null and <see cref="PurgeOldLogsAsync"/> returns 0; both are
/// concepts of the EF-backed implementation. Network failures degrade to
/// empty results so the admin UI doesn't 500 when Loki is unreachable.
/// </summary>
public class LokiTelemetryLogService : ITelemetryLogService
{
    private static readonly JsonSerializerOptions JsonOpts = new(JsonSerializerDefaults.Web);

    private readonly HttpClient _http;
    private readonly LokiLogServiceOptions _options;
    private readonly ILogger<LokiTelemetryLogService> _logger;

    public LokiTelemetryLogService(
        HttpClient httpClient,
        IOptions<LokiLogServiceOptions> options,
        ILogger<LokiTelemetryLogService> logger)
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

    public bool IsEnabled => !string.IsNullOrWhiteSpace(_options.Endpoint);
    public string Backend => "loki";

    public async Task<TelemetryLogQueryResult> QueryAsync(TelemetryLogQuery query, CancellationToken cancellationToken = default)
    {
        var empty = new TelemetryLogQueryResult
        {
            Items = new List<TelemetryLog>(),
            TotalCount = 0,
            Page = query.Page,
            PageSize = query.PageSize,
        };

        if (!IsEnabled) return empty;

        try
        {
            var (startNs, endNs) = ResolveWindow(query);
            var logQl = BuildLogQl(query);
            // Loki has no offset/skip — over-fetch enough to satisfy the
            // requested page client-side. This is the same approach the
            // Tempo trace store takes for its (similarly cursor-less) API.
            var page = Math.Max(1, query.Page);
            var pageSize = Math.Max(1, query.PageSize);
            var limit = Math.Min(_options.MaxLimit, page * pageSize);

            var direction = query.SortDescending ? "backward" : "forward";
            var url = "loki/api/v1/query_range" +
                $"?query={Uri.EscapeDataString(logQl)}" +
                $"&start={startNs}" +
                $"&end={endNs}" +
                $"&limit={limit}" +
                $"&direction={direction}";

            using var response = await _http.GetAsync(url, cancellationToken);
            if (!response.IsSuccessStatusCode)
            {
                _logger.LogWarning(
                    "Loki query_range returned {StatusCode}: {Reason}. Returning empty result.",
                    response.StatusCode, response.ReasonPhrase);
                return empty;
            }

            var payload = await response.Content.ReadAsStringAsync(cancellationToken);
            using var doc = JsonDocument.Parse(payload);

            var allLogs = ParseStreams(doc.RootElement, query.TenantId);

            // Apply sort + page slice client-side. Loki returns ordered per
            // stream but interleaving across streams isn't guaranteed; sort
            // here so paging is deterministic.
            allLogs = (query.SortDescending
                ? allLogs.OrderByDescending(l => l.Timestamp)
                : allLogs.OrderBy(l => l.Timestamp))
                .ToList();

            var items = allLogs.Skip((page - 1) * pageSize).Take(pageSize).ToList();

            return new TelemetryLogQueryResult
            {
                Items = items,
                TotalCount = allLogs.Count,
                Page = page,
                PageSize = pageSize,
            };
        }
        catch (TaskCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            _logger.LogWarning("Loki query timed out after {Timeout}", _options.RequestTimeout);
            return empty;
        }
        catch (HttpRequestException ex)
        {
            _logger.LogWarning(ex, "Loki query HTTP error — backend likely unreachable");
            return empty;
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Loki query failed unexpectedly");
            return empty;
        }
    }

    public Task<TelemetryLog?> GetByIdAsync(long id, CancellationToken cancellationToken = default)
    {
        // Loki keys lines by (timestamp, stream-hash) — no integer ID. The
        // admin UI's "view single log" path is EF-only; on Loki it stays
        // disabled rather than synthesising a fake id.
        return Task.FromResult<TelemetryLog?>(null);
    }

    public async Task<IEnumerable<string>> GetCategoriesAsync(string? tenantId, CancellationToken cancellationToken = default)
    {
        if (!IsEnabled) return Array.Empty<string>();

        try
        {
            var url = $"loki/api/v1/label/{Uri.EscapeDataString(_options.CategoryLabel)}/values";
            using var response = await _http.GetAsync(url, cancellationToken);
            if (!response.IsSuccessStatusCode) return Array.Empty<string>();

            var payload = await response.Content.ReadAsStringAsync(cancellationToken);
            using var doc = JsonDocument.Parse(payload);

            if (!doc.RootElement.TryGetProperty("data", out var dataEl) ||
                dataEl.ValueKind != JsonValueKind.Array)
            {
                return Array.Empty<string>();
            }

            var values = new List<string>();
            foreach (var v in dataEl.EnumerateArray())
            {
                var s = v.GetString();
                if (!string.IsNullOrEmpty(s)) values.Add(s);
            }
            return values.OrderBy(c => c).Take(100).ToList();
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Loki label/values lookup for {Label} failed", _options.CategoryLabel);
            return Array.Empty<string>();
        }
    }

    public Task<int> PurgeOldLogsAsync(string? tenantId, DateTime cutoffDate, CancellationToken cancellationToken = default)
    {
        // Loki retention is enforced by Loki itself (compactor + retention
        // rules in the deployment config); the app must not try to delete
        // log streams remotely.
        return Task.FromResult(0);
    }

    // ────────── helpers ──────────

    private (long start, long end) ResolveWindow(TelemetryLogQuery query)
    {
        var endDt = query.EndDate ?? DateTime.UtcNow;
        var startDt = query.StartDate ?? endDt - _options.DefaultLookback;
        // Loki accepts unix nanoseconds for start/end.
        return (
            ToUnixNanos(DateTime.SpecifyKind(startDt, DateTimeKind.Utc)),
            ToUnixNanos(DateTime.SpecifyKind(endDt, DateTimeKind.Utc)));
    }

    private string BuildLogQl(TelemetryLogQuery query)
    {
        var sb = new System.Text.StringBuilder();
        sb.Append(_options.BaseSelector);

        if (!string.IsNullOrWhiteSpace(query.Level))
        {
            sb.Append(" | level=`").Append(NormaliseLevel(query.Level)).Append('`');
        }
        if (!string.IsNullOrWhiteSpace(query.Category))
        {
            // Substring match against the category label/field. Backticked
            // strings in LogQL don't need escaping for double-quotes.
            sb.Append(" | ").Append(_options.CategoryLabel)
              .Append("=~`.*").Append(EscapeRegex(query.Category)).Append(".*`");
        }
        if (!string.IsNullOrWhiteSpace(query.TraceId))
        {
            sb.Append(" | trace_id=`").Append(query.TraceId).Append('`');
        }
        if (!string.IsNullOrWhiteSpace(query.UserId))
        {
            sb.Append(" | user_id=`").Append(query.UserId).Append('`');
        }
        if (!string.IsNullOrWhiteSpace(query.ClientId))
        {
            sb.Append(" | client_id=`").Append(query.ClientId).Append('`');
        }
        if (!string.IsNullOrWhiteSpace(query.SearchTerm))
        {
            // Case-insensitive line filter. `(?i)` is RE2's inline flag.
            sb.Append(" |~ `(?i)").Append(EscapeRegex(query.SearchTerm)).Append('`');
        }

        return sb.ToString();
    }

    private static string NormaliseLevel(string level) => level.ToLowerInvariant() switch
    {
        "trace" => "Trace",
        "debug" => "Debug",
        "info" or "information" => "Information",
        "warn" or "warning" => "Warning",
        "error" => "Error",
        "fatal" or "critical" => "Critical",
        _ => level,
    };

    private static string EscapeRegex(string s)
    {
        // RE2 metacharacters that need escaping inside a regex literal.
        return System.Text.RegularExpressions.Regex.Escape(s);
    }

    private List<TelemetryLog> ParseStreams(JsonElement root, string? tenantFilter)
    {
        var logs = new List<TelemetryLog>();
        if (!root.TryGetProperty("data", out var data)) return logs;
        if (!data.TryGetProperty("result", out var result) ||
            result.ValueKind != JsonValueKind.Array)
        {
            return logs;
        }

        foreach (var stream in result.EnumerateArray())
        {
            var labels = ExtractLabels(stream);
            if (!stream.TryGetProperty("values", out var values) ||
                values.ValueKind != JsonValueKind.Array)
            {
                continue;
            }

            foreach (var entry in values.EnumerateArray())
            {
                var log = ParseEntry(entry, labels);
                if (log == null) continue;
                if (!string.IsNullOrEmpty(tenantFilter) && log.TenantId != tenantFilter) continue;
                logs.Add(log);
            }
        }
        return logs;
    }

    private static Dictionary<string, string> ExtractLabels(JsonElement stream)
    {
        var dict = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        if (stream.TryGetProperty("stream", out var labelsEl) &&
            labelsEl.ValueKind == JsonValueKind.Object)
        {
            foreach (var prop in labelsEl.EnumerateObject())
            {
                dict[prop.Name] = prop.Value.GetString() ?? "";
            }
        }
        return dict;
    }

    private static TelemetryLog? ParseEntry(JsonElement entry, IReadOnlyDictionary<string, string> labels)
    {
        if (entry.ValueKind != JsonValueKind.Array) return null;
        if (entry.GetArrayLength() < 2) return null;

        var tsRaw = entry[0].GetString();
        if (string.IsNullOrEmpty(tsRaw) || !long.TryParse(tsRaw, NumberStyles.Integer, CultureInfo.InvariantCulture, out var tsNs))
        {
            return null;
        }

        var line = entry[1].GetString() ?? string.Empty;

        // Loki 3.x adds a third element with structured metadata; fall back
        // gracefully when the cluster is on the older two-element shape.
        var metadata = entry.GetArrayLength() >= 3 ? ExtractMetadata(entry[2]) : new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

        var fields = new Dictionary<string, string>(metadata, StringComparer.OrdinalIgnoreCase);
        foreach (var kv in labels)
        {
            // Stream labels override per-line metadata only when metadata
            // is missing — labels are tenant-wide attributes, metadata is
            // line-specific (richer where it exists).
            if (!fields.ContainsKey(kv.Key)) fields[kv.Key] = kv.Value;
        }

        return new TelemetryLog
        {
            Timestamp = DateTimeOffset.FromUnixTimeMilliseconds(tsNs / 1_000_000).UtcDateTime,
            Level = LookupOrDefault(fields, "level", "severity_text", "severity") ?? "Information",
            Message = line,
            Category = LookupOrDefault(fields, "category", "logger_name", "logger", "scope_name") ?? "",
            Exception = LookupOrDefault(fields, "exception", "exception_message"),
            ExceptionType = LookupOrDefault(fields, "exception_type"),
            StackTrace = LookupOrDefault(fields, "exception_stacktrace", "stack_trace"),
            TraceId = LookupOrDefault(fields, "trace_id", "traceid"),
            SpanId = LookupOrDefault(fields, "span_id", "spanid"),
            RequestPath = LookupOrDefault(fields, "request_path", "http.target", "url_path"),
            RequestMethod = LookupOrDefault(fields, "request_method", "http.method"),
            UserId = LookupOrDefault(fields, "user_id", "userid"),
            ClientId = LookupOrDefault(fields, "client_id", "clientid"),
            MachineName = LookupOrDefault(fields, "machine_name", "host.name", "host_name"),
            TenantId = LookupOrDefault(fields, "tenant_id", "tenantid") ?? string.Empty,
        };
    }

    private static Dictionary<string, string> ExtractMetadata(JsonElement metaEl)
    {
        var dict = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        if (metaEl.ValueKind != JsonValueKind.Object) return dict;
        foreach (var prop in metaEl.EnumerateObject())
        {
            dict[prop.Name] = prop.Value.ValueKind switch
            {
                JsonValueKind.String => prop.Value.GetString() ?? "",
                JsonValueKind.Number => prop.Value.ToString(),
                JsonValueKind.True or JsonValueKind.False => prop.Value.GetBoolean().ToString(),
                _ => prop.Value.ToString(),
            };
        }
        return dict;
    }

    private static string? LookupOrDefault(IReadOnlyDictionary<string, string> fields, params string[] keys)
    {
        foreach (var k in keys)
        {
            if (fields.TryGetValue(k, out var v) && !string.IsNullOrEmpty(v)) return v;
        }
        return null;
    }

    private static long ToUnixNanos(DateTime utc)
    {
        var offset = new DateTimeOffset(utc, TimeSpan.Zero);
        return offset.ToUnixTimeMilliseconds() * 1_000_000;
    }
}
