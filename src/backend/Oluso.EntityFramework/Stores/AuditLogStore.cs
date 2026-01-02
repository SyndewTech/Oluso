using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Oluso.Core.Domain.Interfaces;

namespace Oluso.EntityFramework.Stores;

/// <summary>
/// Entity Framework implementation of IAuditLogStore
/// </summary>
public class AuditLogStore : IAuditLogStore
{
    private readonly IOlusoDbContext _context;
    private readonly ILogger<AuditLogStore> _logger;

    public AuditLogStore(IOlusoDbContext context, ILogger<AuditLogStore> logger)
    {
        _context = context;
        _logger = logger;
    }

    public async Task WriteAsync(AuditLog auditLog, CancellationToken cancellationToken = default)
    {
        try
        {
            _context.AuditLogs.Add(auditLog);
            await _context.SaveChangesAsync(cancellationToken);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to write audit log entry");
            throw;
        }
    }

    public async Task WriteBatchAsync(IEnumerable<AuditLog> auditLogs, CancellationToken cancellationToken = default)
    {
        try
        {
            _context.AuditLogs.AddRange(auditLogs);
            await _context.SaveChangesAsync(cancellationToken);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to write batch of audit log entries");
            throw;
        }
    }
}

/// <summary>
/// Entity Framework implementation of IAuditLogService for querying
/// </summary>
public class AuditLogService : IAuditLogService
{
    private readonly IOlusoDbContext _context;
    private readonly ITenantContext? _tenantContext;

    public bool IsEnabled => true;

    public AuditLogService(IOlusoDbContext context, ITenantContext? tenantContext = null)
    {
        _context = context;
        _tenantContext = tenantContext;
    }

    public async Task<AuditLogQueryResult> QueryAsync(AuditLogQuery query, CancellationToken cancellationToken = default)
    {
        var baseQuery = _context.AuditLogs.AsQueryable();

        // Apply tenant filter
        if (!string.IsNullOrEmpty(query.TenantId))
            baseQuery = baseQuery.Where(a => a.TenantId == query.TenantId);

        // Apply filters
        if (!string.IsNullOrEmpty(query.Action))
            baseQuery = baseQuery.Where(a => a.Action == query.Action);

        if (!string.IsNullOrEmpty(query.Category))
            baseQuery = baseQuery.Where(a => a.Category == query.Category);

        if (!string.IsNullOrEmpty(query.EventType))
            baseQuery = baseQuery.Where(a => a.EventType == query.EventType);

        if (!string.IsNullOrEmpty(query.SubjectId))
            baseQuery = baseQuery.Where(a => a.SubjectId == query.SubjectId);

        if (!string.IsNullOrEmpty(query.ResourceType))
            baseQuery = baseQuery.Where(a => a.ResourceType == query.ResourceType);

        if (!string.IsNullOrEmpty(query.ResourceId))
            baseQuery = baseQuery.Where(a => a.ResourceId == query.ResourceId);

        if (!string.IsNullOrEmpty(query.ClientId))
            baseQuery = baseQuery.Where(a => a.ClientId == query.ClientId);

        if (query.Success.HasValue)
            baseQuery = baseQuery.Where(a => a.Success == query.Success.Value);

        if (query.StartDate.HasValue)
            baseQuery = baseQuery.Where(a => a.Timestamp >= query.StartDate.Value);

        if (query.EndDate.HasValue)
            baseQuery = baseQuery.Where(a => a.Timestamp <= query.EndDate.Value);

        if (!string.IsNullOrEmpty(query.SearchTerm))
        {
            var term = query.SearchTerm.ToLower();
            baseQuery = baseQuery.Where(a =>
                (a.SubjectName != null && a.SubjectName.ToLower().Contains(term)) ||
                (a.SubjectEmail != null && a.SubjectEmail.ToLower().Contains(term)) ||
                (a.ResourceName != null && a.ResourceName.ToLower().Contains(term)) ||
                (a.Details != null && a.Details.ToLower().Contains(term)));
        }

        // Count total
        var totalCount = await baseQuery.CountAsync(cancellationToken);

        // Apply sorting
        baseQuery = query.SortBy.ToLowerInvariant() switch
        {
            "timestamp" => query.SortDescending
                ? baseQuery.OrderByDescending(a => a.Timestamp)
                : baseQuery.OrderBy(a => a.Timestamp),
            "eventtype" => query.SortDescending
                ? baseQuery.OrderByDescending(a => a.EventType)
                : baseQuery.OrderBy(a => a.EventType),
            "category" => query.SortDescending
                ? baseQuery.OrderByDescending(a => a.Category)
                : baseQuery.OrderBy(a => a.Category),
            _ => baseQuery.OrderByDescending(a => a.Timestamp)
        };

        // Apply pagination
        var items = await baseQuery
            .Skip((query.Page - 1) * query.PageSize)
            .Take(query.PageSize)
            .ToListAsync(cancellationToken);

        return new AuditLogQueryResult
        {
            Items = items,
            TotalCount = totalCount,
            Page = query.Page,
            PageSize = query.PageSize
        };
    }

    public async Task<AuditLog?> GetByIdAsync(long id, CancellationToken cancellationToken = default)
    {
        return await _context.AuditLogs.FirstOrDefaultAsync(a => a.Id == id, cancellationToken);
    }

    public async Task<IEnumerable<AuditLog>> GetByResourceAsync(string? tenantId, string resourceType, string resourceId, int limit, CancellationToken cancellationToken = default)
    {
        var query = _context.AuditLogs
            .Where(a => a.ResourceType == resourceType && a.ResourceId == resourceId);

        if (!string.IsNullOrEmpty(tenantId))
            query = query.Where(a => a.TenantId == tenantId);

        return await query
            .OrderByDescending(a => a.Timestamp)
            .Take(limit)
            .ToListAsync(cancellationToken);
    }

    public async Task<IEnumerable<AuditLog>> GetBySubjectAsync(string? tenantId, string subjectId, int limit, CancellationToken cancellationToken = default)
    {
        var query = _context.AuditLogs
            .Where(a => a.SubjectId == subjectId);

        if (!string.IsNullOrEmpty(tenantId))
            query = query.Where(a => a.TenantId == tenantId);

        return await query
            .OrderByDescending(a => a.Timestamp)
            .Take(limit)
            .ToListAsync(cancellationToken);
    }

    public async Task<int> PurgeOldLogsAsync(string? tenantId, DateTime cutoffDate, CancellationToken cancellationToken = default)
    {
        var query = _context.AuditLogs
            .Where(a => a.Timestamp < cutoffDate);

        if (!string.IsNullOrEmpty(tenantId))
            query = query.Where(a => a.TenantId == tenantId);

        return await query.ExecuteDeleteAsync(cancellationToken);
    }
}
