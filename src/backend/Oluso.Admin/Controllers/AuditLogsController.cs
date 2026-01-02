using System.Text;
using System.Text.Json;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Oluso.Core.Api;
using Oluso.Core.Domain.Entities;
using Oluso.Core.Domain.Interfaces;

namespace Oluso.Admin.Controllers;

/// <summary>
/// Admin API for audit logs (requires Professional+ license or AuditLogging add-on)
/// </summary>
[Route("api/admin/auditlogs")]
public class AuditLogsController : AdminBaseController
{
    private readonly IAuditLogService _auditLogService;
    private readonly ILogger<AuditLogsController> _logger;

    public AuditLogsController(
        IAuditLogService auditLogService,
        ITenantContext tenantContext,
        ILogger<AuditLogsController> logger) : base(tenantContext)
    {
        _auditLogService = auditLogService;
        _logger = logger;
    }

    /// <summary>
    /// Check if audit logging is enabled
    /// </summary>
    [HttpGet("status")]
    public ActionResult<AuditLogStatusResponse> GetStatus()
    {
        var isEnabled = _auditLogService.IsEnabled;
        return Ok(new AuditLogStatusResponse
        {
            Enabled = isEnabled,
            Message = isEnabled ? "Audit logging is active" : "Audit logging is not enabled"
        });
    }

    /// <summary>
    /// Get audit logs with filtering and pagination
    /// </summary>
    [HttpGet]
    public async Task<ActionResult<AuditLogPaginatedResult>> GetAuditLogs(
        [FromQuery] string? action,
        [FromQuery] string? category,
        [FromQuery] string? eventType,
        [FromQuery] string? subjectId,
        [FromQuery] string? resourceType,
        [FromQuery] string? resourceId,
        [FromQuery] string? clientId,
        [FromQuery] bool? success,
        [FromQuery] string? search,
        [FromQuery] DateTime? from,
        [FromQuery] DateTime? to,
        [FromQuery] string? sortBy,
        [FromQuery] bool sortDesc = true,
        [FromQuery] int page = 1,
        [FromQuery] int pageSize = 50,
        CancellationToken cancellationToken = default)
    {
        if (!_auditLogService.IsEnabled)
        {
            return StatusCode(402, new
            {
                error = "feature_not_enabled",
                message = "Audit logging is not enabled"
            });
        }

        var query = new AuditLogQuery
        {
            TenantId = TenantId, // Filter by tenant
            Action = action,
            Category = category,
            EventType = eventType,
            SubjectId = subjectId,
            ResourceType = resourceType,
            ResourceId = resourceId,
            ClientId = clientId,
            Success = success,
            SearchTerm = search,
            StartDate = from,
            EndDate = to,
            SortBy = sortBy ?? "Timestamp",
            SortDescending = sortDesc,
            Page = page,
            PageSize = Math.Min(pageSize, 200) // Cap at 200
        };

        var result = await _auditLogService.QueryAsync(query, cancellationToken);

        return Ok(new AuditLogPaginatedResult
        {
            Items = result.Items.Select(MapToDto).ToList(),
            TotalCount = result.TotalCount,
            PageNumber = result.Page,
            PageSize = result.PageSize,
            TotalPages = result.TotalPages
        });
    }

    /// <summary>
    /// Get a specific audit log by ID
    /// </summary>
    [HttpGet("{id:long}")]
    public async Task<ActionResult<AuditLogDto>> GetAuditLog(long id, CancellationToken cancellationToken = default)
    {
        if (!_auditLogService.IsEnabled)
        {
            return StatusCode(402, new
            {
                error = "feature_not_enabled",
                message = "Audit logging is not enabled"
            });
        }

        var log = await _auditLogService.GetByIdAsync(id, cancellationToken);
        if (log == null)
        {
            return NotFound();
        }

        return Ok(MapToDto(log));
    }

    /// <summary>
    /// Get audit logs for a specific resource
    /// </summary>
    [HttpGet("resource/{resourceType}/{resourceId}")]
    public async Task<ActionResult<List<AuditLogDto>>> GetResourceAuditLogs(
        string resourceType,
        string resourceId,
        [FromQuery] int limit = 100,
        CancellationToken cancellationToken = default)
    {
        if (!_auditLogService.IsEnabled)
        {
            return StatusCode(402, new
            {
                error = "feature_not_enabled",
                message = "Audit logging is not enabled"
            });
        }

        var logs = await _auditLogService.GetByResourceAsync(TenantId, resourceType, resourceId, limit, cancellationToken);
        return Ok(logs.Select(MapToDto).ToList());
    }

    /// <summary>
    /// Get audit logs for a specific user
    /// </summary>
    [HttpGet("user/{subjectId}")]
    public async Task<ActionResult<List<AuditLogDto>>> GetUserAuditLogs(
        string subjectId,
        [FromQuery] int limit = 100,
        CancellationToken cancellationToken = default)
    {
        if (!_auditLogService.IsEnabled)
        {
            return StatusCode(402, new
            {
                error = "feature_not_enabled",
                message = "Audit logging is not enabled"
            });
        }

        var logs = await _auditLogService.GetBySubjectAsync(TenantId, subjectId, limit, cancellationToken);
        return Ok(logs.Select(MapToDto).ToList());
    }

    /// <summary>
    /// Export audit logs
    /// </summary>
    [HttpGet("export")]
    public async Task<ActionResult> ExportAuditLogs(
        [FromQuery] string? action,
        [FromQuery] string? category,
        [FromQuery] string? eventType,
        [FromQuery] string? subjectId,
        [FromQuery] string? clientId,
        [FromQuery] DateTime? from,
        [FromQuery] DateTime? to,
        [FromQuery] string format = "json",
        CancellationToken cancellationToken = default)
    {
        if (!_auditLogService.IsEnabled)
        {
            return StatusCode(402, new
            {
                error = "feature_not_enabled",
                message = "Audit logging is not enabled"
            });
        }

        // Get all matching logs (up to 10000 for export)
        var query = new AuditLogQuery
        {
            TenantId = TenantId, // Filter by tenant
            Action = action,
            Category = category,
            EventType = eventType,
            SubjectId = subjectId,
            ClientId = clientId,
            StartDate = from,
            EndDate = to,
            Page = 1,
            PageSize = 10000,
            SortDescending = false // Chronological order for export
        };

        var result = await _auditLogService.QueryAsync(query, cancellationToken);
        var items = result.Items.Select(MapToDto).ToList();

        var timestamp = DateTime.UtcNow.ToString("yyyyMMdd_HHmmss");

        if (format.Equals("csv", StringComparison.OrdinalIgnoreCase))
        {
            var csv = GenerateCsv(items);
            return File(Encoding.UTF8.GetBytes(csv), "text/csv", $"audit_logs_{timestamp}.csv");
        }

        var json = JsonSerializer.Serialize(items, new JsonSerializerOptions { WriteIndented = true });
        return File(Encoding.UTF8.GetBytes(json), "application/json", $"audit_logs_{timestamp}.json");
    }

    /// <summary>
    /// Purge old audit logs (admin only)
    /// </summary>
    [HttpDelete("purge")]
    public async Task<ActionResult<PurgeResult>> PurgeOldLogs(
        [FromQuery] int olderThanDays = 90,
        CancellationToken cancellationToken = default)
    {
        if (!_auditLogService.IsEnabled)
        {
            return StatusCode(402, new
            {
                error = "feature_not_enabled",
                message = "Audit logging is not enabled"
            });
        }

        if (olderThanDays < 30)
        {
            return BadRequest(new { error = "Minimum retention period is 30 days" });
        }

        var cutoffDate = DateTime.UtcNow.AddDays(-olderThanDays);
        var count = await _auditLogService.PurgeOldLogsAsync(TenantId, cutoffDate, cancellationToken);

        _logger.LogInformation("Purged {Count} audit logs for tenant {TenantId} older than {CutoffDate}", count, TenantId, cutoffDate);

        return Ok(new PurgeResult { DeletedCount = count, CutoffDate = cutoffDate });
    }

    private static AuditLogDto MapToDto(AuditLog log)
    {
        return new AuditLogDto
        {
            Id = log.Id,
            Timestamp = log.Timestamp,
            EventType = log.EventType,
            Category = log.Category,
            Action = log.Action,
            SubjectId = log.SubjectId,
            SubjectName = log.SubjectName,
            SubjectEmail = log.SubjectEmail,
            ResourceType = log.ResourceType,
            ResourceId = log.ResourceId,
            ResourceName = log.ResourceName,
            ClientId = log.ClientId,
            IpAddress = log.IpAddress,
            UserAgent = log.UserAgent,
            Success = log.Success,
            ErrorMessage = log.ErrorMessage,
            Details = log.Details,
            Reason = log.Reason,
            ActivityId = log.ActivityId
        };
    }

    private static string GenerateCsv(List<AuditLogDto> items)
    {
        var sb = new StringBuilder();
        sb.AppendLine("Id,Timestamp,EventType,Category,Action,SubjectId,SubjectName,ResourceType,ResourceId,ClientId,IpAddress,Success,ErrorMessage");

        foreach (var item in items)
        {
            sb.AppendLine($"{item.Id},{item.Timestamp:O},{Escape(item.EventType)},{Escape(item.Category)},{Escape(item.Action)},{Escape(item.SubjectId)},{Escape(item.SubjectName)},{Escape(item.ResourceType)},{Escape(item.ResourceId)},{Escape(item.ClientId)},{Escape(item.IpAddress)},{item.Success},{Escape(item.ErrorMessage)}");
        }

        return sb.ToString();
    }

    private static string Escape(string? value)
    {
        if (string.IsNullOrEmpty(value)) return "";
        if (value.Contains(',') || value.Contains('"') || value.Contains('\n'))
        {
            return $"\"{value.Replace("\"", "\"\"")}\"";
        }
        return value;
    }
}

#region DTOs

public class AuditLogStatusResponse
{
    public bool Enabled { get; set; }
    public string? Message { get; set; }
}

public class AuditLogPaginatedResult
{
    public List<AuditLogDto> Items { get; set; } = new();
    public int TotalCount { get; set; }
    public int PageNumber { get; set; }
    public int PageSize { get; set; }
    public int TotalPages { get; set; }
}

public class AuditLogDto
{
    public long Id { get; set; }
    public DateTime Timestamp { get; set; }
    public string EventType { get; set; } = null!;
    public string Category { get; set; } = null!;
    public string? Action { get; set; }
    public string? SubjectId { get; set; }
    public string? SubjectName { get; set; }
    public string? SubjectEmail { get; set; }
    public string? ResourceType { get; set; }
    public string? ResourceId { get; set; }
    public string? ResourceName { get; set; }
    public string? ClientId { get; set; }
    public string? IpAddress { get; set; }
    public string? UserAgent { get; set; }
    public bool Success { get; set; }
    public string? ErrorMessage { get; set; }
    public string? Details { get; set; }
    public string? Reason { get; set; }
    public string? ActivityId { get; set; }
}

public class PurgeResult
{
    public int DeletedCount { get; set; }
    public DateTime CutoffDate { get; set; }
}

#endregion
