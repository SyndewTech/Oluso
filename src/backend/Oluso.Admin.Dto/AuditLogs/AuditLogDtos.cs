namespace Oluso.Admin.Dto.AuditLogs;

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

    /// <summary>What caused the event: admin, service_client, scim, journey, user, anonymous or system.</summary>
    public string? OriginKind { get; set; }

    /// <summary>The causing actor's id within <see cref="OriginKind"/>.</summary>
    public string? OriginId { get; set; }
    public string? OriginName { get; set; }
}

public class PurgeResult
{
    public int DeletedCount { get; set; }
    public DateTime CutoffDate { get; set; }
}
