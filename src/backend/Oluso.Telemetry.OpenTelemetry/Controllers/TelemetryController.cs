using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Logging;
using Oluso.Core.Api;
using Oluso.Core.Domain.Interfaces;
using Oluso.Telemetry;
using Oluso.Telemetry.OpenTelemetry.Tracing;

namespace Oluso.Telemetry.OpenTelemetry.Controllers;

/// <summary>
/// Admin API for telemetry data - metrics, logs, and traces.
/// Locked to SuperAdmin: log rows are not tenant-tagged at write time, so
/// exposing this per-tenant would leak cross-tenant data.
/// </summary>
[Route("api/admin/telemetry")]
[Authorize(Policy = "SuperAdmin")]
public class TelemetryController : AdminBaseController
{
    private readonly IAuditLogService _auditLogService;
    private readonly ITelemetryLogService _telemetryLogService;
    private readonly ITelemetryTraceStore? _traceStore;
    private readonly IServerSideSessionStore _sessionStore;
    private readonly IOlusoTelemetry _telemetry;
    private readonly ILogger<TelemetryController> _logger;

    public TelemetryController(ITenantContext tenantContext,
        IOlusoTelemetry telemetry,
        ILogger<TelemetryController> logger,
        IAuditLogService auditLogService,
        ITelemetryLogService telemetryLogService,
        IServerSideSessionStore sessionStore,
        ITelemetryTraceStore? traceStore = null) : base(tenantContext)
    {
        _telemetry = telemetry;
        _logger = logger;
        _auditLogService = auditLogService;
        _telemetryLogService = telemetryLogService;
        _traceStore = traceStore;
        _sessionStore = sessionStore;
    }

    #region Metrics

    /// <summary>
    /// Get overview metrics for the dashboard
    /// </summary>
    [HttpGet("metrics/overview")]
    public async Task<ActionResult<TelemetryOverviewDto>> GetOverviewMetrics(
        [FromQuery] string timeRange = "24h",
        CancellationToken cancellationToken = default)
    {
        var startDate = GetStartDate(timeRange);

        long tokensIssued = 0;
        long tokensFailed = 0;
        long activeUsers = 0;
        long authSuccess = 0;
        long authFailure = 0;
        long activeSessions = 0;

        // Query from audit logs if available
        if (_auditLogService?.IsEnabled == true)
        {
            // Tokens issued
            var tokensQuery = new AuditLogQuery
            {
                Action = "TokenIssued",
                StartDate = startDate,
                Success = true,
                Page = 1,
                PageSize = 1
            };
            var tokensResult = await _auditLogService.QueryAsync(tokensQuery, cancellationToken);
            tokensIssued = tokensResult.TotalCount;

            // Token failures
            var tokenFailedQuery = new AuditLogQuery
            {
                Action = "TokenRequestFailed",
                StartDate = startDate,
                Page = 1,
                PageSize = 1
            };
            var tokenFailedResult = await _auditLogService.QueryAsync(tokenFailedQuery, cancellationToken);
            tokensFailed = tokenFailedResult.TotalCount;

            // Successful logins
            var authSuccessQuery = new AuditLogQuery
            {
                Action = "Login",
                StartDate = startDate,
                Success = true,
                Page = 1,
                PageSize = 1
            };
            var authSuccessResult = await _auditLogService.QueryAsync(authSuccessQuery, cancellationToken);
            authSuccess = authSuccessResult.TotalCount;

            // Failed logins
            var authFailureQuery = new AuditLogQuery
            {
                Action = "LoginFailed",
                StartDate = startDate,
                Page = 1,
                PageSize = 1
            };
            var authFailureResult = await _auditLogService.QueryAsync(authFailureQuery, cancellationToken);
            authFailure = authFailureResult.TotalCount;

            // Count unique users from successful logins
            // This is a rough estimate - in production you'd have a proper metric
            activeUsers = authSuccess; // Simplification
        }

        // Get active sessions from session store
        if (_sessionStore != null)
        {
            var (_, totalSessions) = await _sessionStore.GetAllSessionsAsync(0, 1, cancellationToken);
            activeSessions = totalSessions;
        }

        // Calculate auth success rate
        var totalAuth = authSuccess + authFailure;
        var authSuccessRate = totalAuth > 0 ? (authSuccess * 100.0 / totalAuth) : 100.0;

        var metrics = new TelemetryOverviewDto
        {
            TokensIssued = tokensIssued,
            TokensFailed = tokensFailed,
            ActiveUsers = activeUsers,
            ActiveSessions = activeSessions,
            AuthSuccessRate = authSuccessRate,
            AvgRequestDuration = 0 // Would need APM integration for this
        };

        return Ok(metrics);
    }

    private static DateTime GetStartDate(string timeRange)
    {
        return timeRange switch
        {
            "1h" => DateTime.UtcNow.AddHours(-1),
            "24h" => DateTime.UtcNow.AddHours(-24),
            "7d" => DateTime.UtcNow.AddDays(-7),
            "30d" => DateTime.UtcNow.AddDays(-30),
            _ => DateTime.UtcNow.AddHours(-24)
        };
    }

    /// <summary>
    /// Get time series data for a specific metric
    /// </summary>
    [HttpGet("metrics/{metricName}")]
    public async Task<ActionResult<MetricSeriesDto>> GetMetricSeries(
        string metricName,
        [FromQuery] string timeRange = "24h",
        [FromQuery] string interval = "1h",
        CancellationToken cancellationToken = default)
    {
        var series = new MetricSeriesDto
        {
            Name = metricName,
            Unit = GetMetricUnit(metricName),
            Data = new List<MetricDataPointDto>()
        };

        // Generate time buckets based on the interval
        var startDate = GetStartDate(timeRange);
        var intervalSpan = GetIntervalSpan(interval);
        var buckets = new List<(DateTime Start, DateTime End)>();

        var current = startDate;
        while (current < DateTime.UtcNow)
        {
            buckets.Add((current, current.Add(intervalSpan)));
            current = current.Add(intervalSpan);
        }

        // Query audit logs if available to fill data points
        if (_auditLogService?.IsEnabled == true)
        {
            var action = metricName switch
            {
                "tokens_issued" => "TokenIssued",
                "auth_success" => "Login",
                "auth_failure" => "LoginFailed",
                _ => null
            };

            if (action != null)
            {
                // For each bucket, query the count
                foreach (var bucket in buckets)
                {
                    var query = new AuditLogQuery
                    {
                        Action = action,
                        StartDate = bucket.Start,
                        EndDate = bucket.End,
                        Success = action == "LoginFailed" ? null : true,
                        Page = 1,
                        PageSize = 1
                    };
                    var result = await _auditLogService.QueryAsync(query, cancellationToken);
                    series.Data.Add(new MetricDataPointDto
                    {
                        Timestamp = bucket.Start.ToString("O"),
                        Value = result.TotalCount
                    });
                }
            }
        }

        // If no data was generated, return empty buckets
        if (series.Data.Count == 0)
        {
            foreach (var bucket in buckets)
            {
                series.Data.Add(new MetricDataPointDto
                {
                    Timestamp = bucket.Start.ToString("O"),
                    Value = 0
                });
            }
        }

        return Ok(series);
    }

    private static TimeSpan GetIntervalSpan(string interval)
    {
        return interval switch
        {
            "5m" => TimeSpan.FromMinutes(5),
            "15m" => TimeSpan.FromMinutes(15),
            "1h" => TimeSpan.FromHours(1),
            "6h" => TimeSpan.FromHours(6),
            "1d" => TimeSpan.FromDays(1),
            _ => TimeSpan.FromHours(1)
        };
    }

    #endregion

    #region Logs

    /// <summary>
    /// Configuration status of the log backend. Mirrors the trace status
    /// endpoint so the admin UI can label whether logs are coming from the
    /// in-process database, Loki, or nothing at all (page renders an
    /// "unconfigured" notice in the last case).
    /// </summary>
    [HttpGet("logs/status")]
    public ActionResult<LogBackendStatusDto> GetLogStatus() =>
        Ok(new LogBackendStatusDto
        {
            IsConfigured = _telemetryLogService?.IsEnabled ?? false,
            Backend = _telemetryLogService?.Backend ?? "none",
        });

    /// <summary>
    /// Get application logs
    /// </summary>
    [HttpGet("logs")]
    public async Task<ActionResult<PagedResult<LogEntryDto>>> GetLogs(
        [FromQuery] TelemetryQueryDto query,
        CancellationToken cancellationToken = default)
    {
        if (_telemetryLogService?.IsEnabled != true)
        {
            return Ok(new PagedResult<LogEntryDto>
            {
                Items = new List<LogEntryDto>(),
                TotalCount = 0,
                Page = query.Page ?? 1,
                PageSize = query.PageSize ?? 50,
                TotalPages = 0
            });
        }

        // Parse date strings to DateTime
        DateTime? startDate = null;
        DateTime? endDate = null;
        if (!string.IsNullOrEmpty(query.StartTime) && DateTime.TryParse(query.StartTime, out var parsedStart))
            startDate = parsedStart;
        if (!string.IsNullOrEmpty(query.EndTime) && DateTime.TryParse(query.EndTime, out var parsedEnd))
            endDate = parsedEnd;

        var logQuery = new TelemetryLogQuery
        {
            Level = query.Level,
            Category = query.Category,
            SearchTerm = query.SearchTerm,
            TraceId = query.TraceId,
            StartDate = startDate,
            EndDate = endDate,
            Page = query.Page ?? 1,
            PageSize = query.PageSize ?? 50
        };

        var queryResult = await _telemetryLogService.QueryAsync(logQuery, cancellationToken);

        var result = new PagedResult<LogEntryDto>
        {
            Items = queryResult.Items.Select(l => new LogEntryDto
            {
                Id = l.Id.ToString(),
                Timestamp = l.Timestamp.ToString("O"),
                Level = l.Level,
                Message = l.Message,
                Category = l.Category,
                Exception = l.Exception,
                ExceptionType = l.ExceptionType,
                StackTrace = l.StackTrace,
                TraceId = l.TraceId,
                SpanId = l.SpanId,
                RequestPath = l.RequestPath,
                RequestMethod = l.RequestMethod,
                UserId = l.UserId,
                ClientId = l.ClientId,
                MachineName = l.MachineName,
                Properties = !string.IsNullOrEmpty(l.Properties)
                    ? System.Text.Json.JsonSerializer.Deserialize<Dictionary<string, object>>(l.Properties)
                    : null
            }).ToList(),
            TotalCount = queryResult.TotalCount,
            Page = queryResult.Page,
            PageSize = queryResult.PageSize,
            TotalPages = queryResult.TotalPages
        };

        return Ok(result);
    }

    /// <summary>
    /// Get available log categories
    /// </summary>
    [HttpGet("logs/categories")]
    public async Task<ActionResult<IEnumerable<string>>> GetLogCategories(CancellationToken cancellationToken = default)
    {
        if (_telemetryLogService?.IsEnabled == true)
        {
            var categories = await _telemetryLogService.GetCategoriesAsync(null, cancellationToken);
            if (categories.Any())
                return Ok(categories);
        }

        // Return default categories if no data in database
        var defaultCategories = new[]
        {
            "Oluso.Token",
            "Oluso.Authorize",
            "Oluso.Authentication",
            "Oluso.Database",
            "Oluso.Journey",
            "Oluso.External",
            "Microsoft.AspNetCore",
            "System"
        };

        return Ok(defaultCategories);
    }

    #endregion

    #region Traces

    /// <summary>
    /// Configuration status of the trace backend. Used by the admin UI to
    /// distinguish "no traces in window" from "tracing isn't configured here"
    /// so dev/staging environments without Tempo render correctly.
    /// </summary>
    [HttpGet("traces/status")]
    public ActionResult<TraceBackendStatusDto> GetTraceStatus() =>
        Ok(new TraceBackendStatusDto
        {
            IsConfigured = _traceStore?.IsConfigured ?? false,
            Backend = (_traceStore?.IsConfigured ?? false) ? "tempo" : "none",
        });

    /// <summary>
    /// Search distributed traces in the configured backend (Tempo).
    /// Returns an empty page when the backend is not configured or unreachable.
    /// </summary>
    [HttpGet("traces")]
    public async Task<ActionResult<PagedResult<TraceDto>>> GetTraces(
        [FromQuery] TelemetryQueryDto query,
        CancellationToken cancellationToken = default)
    {
        DateTime? startDate = null;
        DateTime? endDate = null;
        if (!string.IsNullOrEmpty(query.StartTime) && DateTime.TryParse(query.StartTime, out var parsedStart))
            startDate = parsedStart;
        if (!string.IsNullOrEmpty(query.EndTime) && DateTime.TryParse(query.EndTime, out var parsedEnd))
            endDate = parsedEnd;

        if (_traceStore == null)
        {
            return Ok(new PagedResult<TraceDto>
            {
                Items = new List<TraceDto>(),
                TotalCount = 0,
                Page = query.Page ?? 1,
                PageSize = query.PageSize ?? 20,
                TotalPages = 0,
            });
        }

        var result = await _traceStore.SearchAsync(new TraceQuery
        {
            StartDate = startDate,
            EndDate = endDate,
            SearchTerm = query.SearchTerm,
            Page = query.Page ?? 1,
            PageSize = query.PageSize ?? 20,
        }, cancellationToken);

        return Ok(new PagedResult<TraceDto>
        {
            Items = result.Items,
            TotalCount = result.TotalCount,
            Page = result.Page,
            PageSize = result.PageSize,
            TotalPages = result.TotalPages,
        });
    }

    /// <summary>
    /// Get a specific trace by ID. Returns 404 when not found, the backend
    /// is unreachable, or tracing isn't configured.
    /// </summary>
    [HttpGet("traces/{traceId}")]
    public async Task<ActionResult<TraceDto>> GetTrace(string traceId, CancellationToken cancellationToken = default)
    {
        if (_traceStore == null) return NotFound();
        var trace = await _traceStore.GetByIdAsync(traceId, cancellationToken);
        if (trace == null) return NotFound();
        return Ok(trace);
    }

    #endregion

    #region Helpers

    private static string GetMetricUnit(string metricName)
    {
        return metricName switch
        {
            "tokens_issued" or "tokens_failed" => "tokens",
            "auth_success" or "auth_failure" => "authentications",
            "request_duration" or "db_duration" => "ms",
            "active_sessions" or "active_users" => "count",
            _ => "count"
        };
    }

    #endregion
}

#region DTOs

public class TelemetryOverviewDto
{
    public long TokensIssued { get; set; }
    public long TokensFailed { get; set; }
    public long ActiveUsers { get; set; }
    public long ActiveSessions { get; set; }
    public double AuthSuccessRate { get; set; }
    public double AvgRequestDuration { get; set; }
}

public class MetricSeriesDto
{
    public string Name { get; set; } = null!;
    public string Unit { get; set; } = null!;
    public List<MetricDataPointDto> Data { get; set; } = new();
}

public class MetricDataPointDto
{
    public string Timestamp { get; set; } = null!;
    public double Value { get; set; }
    public Dictionary<string, string>? Tags { get; set; }
}

public class LogEntryDto
{
    public string Id { get; set; } = null!;
    public string Timestamp { get; set; } = null!;
    public string Level { get; set; } = null!;
    public string Message { get; set; } = null!;
    public string Category { get; set; } = null!;
    public Dictionary<string, object>? Properties { get; set; }
    public string? Exception { get; set; }
    public string? ExceptionType { get; set; }
    public string? StackTrace { get; set; }
    public string? TraceId { get; set; }
    public string? SpanId { get; set; }
    public string? RequestPath { get; set; }
    public string? RequestMethod { get; set; }
    public string? UserId { get; set; }
    public string? ClientId { get; set; }
    public string? MachineName { get; set; }
}

public class TraceBackendStatusDto
{
    public bool IsConfigured { get; set; }
    public string Backend { get; set; } = "none";
}

public class LogBackendStatusDto
{
    public bool IsConfigured { get; set; }
    public string Backend { get; set; } = "none";
}

public class TelemetryQueryDto
{
    public string? StartTime { get; set; }
    public string? EndTime { get; set; }
    public string? Level { get; set; }
    public string? Category { get; set; }
    public string? SearchTerm { get; set; }
    public string? TraceId { get; set; }
    public int? Page { get; set; }
    public int? PageSize { get; set; }
}



#endregion
