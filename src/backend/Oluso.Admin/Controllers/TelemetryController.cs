using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Oluso.Core.Api;
using Oluso.Core.Domain.Interfaces;
using Oluso.Telemetry;

namespace Oluso.Admin.Controllers;

/// <summary>
/// Admin API for telemetry data - logs, metrics, and traces
/// </summary>
[Route("api/admin/telemetry")]
public class TelemetryController : AdminBaseController
{
    private readonly ITenantContext _tenantContext;
    private readonly IAuditLogService? _auditLogService;
    private readonly IServerSideSessionStore? _sessionStore;
    private readonly IOlusoTelemetry _telemetry;
    private readonly ILogger<TelemetryController> _logger;

    public TelemetryController(
        ITenantContext tenantContext,
        IOlusoTelemetry telemetry,
        ILogger<TelemetryController> logger,
        IAuditLogService? auditLogService = null,
        IServerSideSessionStore? sessionStore = null) : base(tenantContext)
    {
        _tenantContext = tenantContext;
        _telemetry = telemetry;
        _logger = logger;
        _auditLogService = auditLogService;
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
    /// Get application logs
    /// </summary>
    [HttpGet("logs")]
    public ActionResult<PagedResultDto<LogEntryDto>> GetLogs([FromQuery] TelemetryQueryDto query)
    {
        // In a real implementation, query from a log store (Elasticsearch, Application Insights, etc.)
        var result = new PagedResultDto<LogEntryDto>
        {
            Items = new List<LogEntryDto>(),
            TotalCount = 0,
            Page = query.Page ?? 1,
            PageSize = query.PageSize ?? 50,
            TotalPages = 0
        };

        return Ok(result);
    }

    /// <summary>
    /// Get available log categories
    /// </summary>
    [HttpGet("logs/categories")]
    public ActionResult<IEnumerable<string>> GetLogCategories()
    {
        var categories = new[]
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

        return Ok(categories);
    }

    #endregion

    #region Traces

    /// <summary>
    /// Get distributed traces
    /// </summary>
    [HttpGet("traces")]
    public ActionResult<PagedResultDto<TraceDto>> GetTraces([FromQuery] TelemetryQueryDto query)
    {
        // In a real implementation, query from a trace store (Jaeger, Zipkin, etc.)
        var result = new PagedResultDto<TraceDto>
        {
            Items = new List<TraceDto>(),
            TotalCount = 0,
            Page = query.Page ?? 1,
            PageSize = query.PageSize ?? 20,
            TotalPages = 0
        };

        return Ok(result);
    }

    /// <summary>
    /// Get a specific trace by ID
    /// </summary>
    [HttpGet("traces/{traceId}")]
    public ActionResult<TraceDto> GetTrace(string traceId)
    {
        // In a real implementation, fetch from trace store
        return NotFound();
    }

    #endregion

    #region Audit Logs

    /// <summary>
    /// Get audit logs
    /// </summary>
    [HttpGet("audit")]
    public async Task<ActionResult<PagedResultDto<AuditLogEntryDto>>> GetAuditLogs(
        [FromQuery] TelemetryQueryDto query,
        CancellationToken cancellationToken = default)
    {
        if (_auditLogService == null)
        {
            return Ok(new PagedResultDto<AuditLogEntryDto>
            {
                Items = new List<AuditLogEntryDto>(),
                TotalCount = 0,
                Page = query.Page ?? 1,
                PageSize = query.PageSize ?? 50,
                TotalPages = 0
            });
        }

        var auditQuery = new AuditLogQuery
        {
            Category = query.Category,
            SearchTerm = query.SearchTerm,
            StartDate = query.StartTime != null ? DateTime.Parse(query.StartTime) : null,
            EndDate = query.EndTime != null ? DateTime.Parse(query.EndTime) : null,
            Page = query.Page ?? 1,
            PageSize = query.PageSize ?? 50
        };

        var result = await _auditLogService.QueryAsync(auditQuery, cancellationToken);

        return Ok(new PagedResultDto<AuditLogEntryDto>
        {
            Items = result.Items.Select(MapToAuditDto).ToList(),
            TotalCount = result.TotalCount,
            Page = result.Page,
            PageSize = result.PageSize,
            TotalPages = result.TotalPages
        });
    }

    /// <summary>
    /// Get available audit log categories
    /// </summary>
    [HttpGet("audit/categories")]
    public ActionResult<IEnumerable<string>> GetAuditCategories()
    {
        var categories = new[]
        {
            "Authentication",
            "User",
            "Security",
            "Token"
        };

        return Ok(categories);
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

    private static AuditLogEntryDto MapToAuditDto(AuditLog log) => new()
    {
        Id = log.Id.ToString(),
        Timestamp = log.Timestamp.ToString("O"),
        EventType = log.EventType,
        Category = log.Category,
        Action = log.Action,
        Success = log.Success,
        SubjectId = log.SubjectId,
        SubjectName = log.SubjectName,
        SubjectEmail = log.SubjectEmail,
        ResourceType = log.ResourceType,
        ResourceId = log.ResourceId,
        ClientId = log.ClientId,
        IpAddress = log.IpAddress,
        UserAgent = log.UserAgent,
        Details = ParseDetails(log.Details),
        ErrorMessage = log.ErrorMessage,
        ActivityId = log.ActivityId
    };

    private static Dictionary<string, object>? ParseDetails(string? detailsJson)
    {
        if (string.IsNullOrEmpty(detailsJson))
            return null;

        try
        {
            return System.Text.Json.JsonSerializer.Deserialize<Dictionary<string, object>>(detailsJson);
        }
        catch
        {
            return new Dictionary<string, object> { ["raw"] = detailsJson };
        }
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
    public string? TraceId { get; set; }
    public string? SpanId { get; set; }
}

public class TraceDto
{
    public string TraceId { get; set; } = null!;
    public TraceSpanDto RootSpan { get; set; } = null!;
    public List<TraceSpanDto> Spans { get; set; } = new();
    public double Duration { get; set; }
    public string StartTime { get; set; } = null!;
    public string ServiceName { get; set; } = null!;
    public string Status { get; set; } = null!;
}

public class TraceSpanDto
{
    public string TraceId { get; set; } = null!;
    public string SpanId { get; set; } = null!;
    public string? ParentSpanId { get; set; }
    public string OperationName { get; set; } = null!;
    public string ServiceName { get; set; } = null!;
    public string StartTime { get; set; } = null!;
    public double Duration { get; set; }
    public string Status { get; set; } = null!;
    public Dictionary<string, string> Tags { get; set; } = new();
    public List<TraceEventDto> Events { get; set; } = new();
}

public class TraceEventDto
{
    public string Name { get; set; } = null!;
    public string Timestamp { get; set; } = null!;
    public Dictionary<string, object>? Attributes { get; set; }
}

public class AuditLogEntryDto
{
    public string Id { get; set; } = null!;
    public string Timestamp { get; set; } = null!;
    public string EventType { get; set; } = null!;
    public string Category { get; set; } = null!;
    public string Action { get; set; } = null!;
    public bool Success { get; set; }
    public string? SubjectId { get; set; }
    public string? SubjectName { get; set; }
    public string? SubjectEmail { get; set; }
    public string? ResourceType { get; set; }
    public string? ResourceId { get; set; }
    public string? ClientId { get; set; }
    public string? IpAddress { get; set; }
    public string? UserAgent { get; set; }
    public Dictionary<string, object>? Details { get; set; }
    public string? ErrorMessage { get; set; }
    public string? ActivityId { get; set; }
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

public class PagedResultDto<T>
{
    public List<T> Items { get; set; } = new();
    public int TotalCount { get; set; }
    public int Page { get; set; }
    public int PageSize { get; set; }
    public int TotalPages { get; set; }
}

#endregion
