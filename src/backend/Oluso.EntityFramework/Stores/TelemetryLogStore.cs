using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Oluso.Core.Domain.Interfaces;

namespace Oluso.EntityFramework.Stores;

/// <summary>
/// Entity Framework implementation of ITelemetryLogStore
/// </summary>
public class TelemetryLogStore : ITelemetryLogStore
{
    private readonly IOlusoDbContext _context;
    private readonly ILogger<TelemetryLogStore> _logger;

    public TelemetryLogStore(IOlusoDbContext context, ILogger<TelemetryLogStore> logger)
    {
        _context = context;
        _logger = logger;
    }

    public async Task WriteAsync(TelemetryLog log, CancellationToken cancellationToken = default)
    {
        try
        {
            _context.TelemetryLogs.Add(log);
            await _context.SaveChangesAsync(cancellationToken);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to write telemetry log entry");
            // Don't throw - logging should not break the application
        }
    }

    public async Task WriteBatchAsync(IEnumerable<TelemetryLog> logs, CancellationToken cancellationToken = default)
    {
        try
        {
            _context.TelemetryLogs.AddRange(logs);
            await _context.SaveChangesAsync(cancellationToken);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to write batch of telemetry log entries");
            // Don't throw - logging should not break the application
        }
    }
}

/// <summary>
/// Entity Framework implementation of ITelemetryLogService for querying
/// </summary>
public class TelemetryLogService : ITelemetryLogService
{
    private readonly IOlusoDbContext _context;
    private readonly ITenantContext? _tenantContext;

    public bool IsEnabled => true;

    public TelemetryLogService(IOlusoDbContext context, ITenantContext? tenantContext = null)
    {
        _context = context;
        _tenantContext = tenantContext;
    }

    public async Task<TelemetryLogQueryResult> QueryAsync(TelemetryLogQuery query, CancellationToken cancellationToken = default)
    {
        var baseQuery = _context.TelemetryLogs.AsQueryable();

        // Apply tenant filter
        if (!string.IsNullOrEmpty(query.TenantId))
            baseQuery = baseQuery.Where(l => l.TenantId == query.TenantId);

        // Apply filters
        if (!string.IsNullOrEmpty(query.Level))
            baseQuery = baseQuery.Where(l => l.Level == query.Level);

        if (!string.IsNullOrEmpty(query.Category))
            baseQuery = baseQuery.Where(l => l.Category.Contains(query.Category));

        if (!string.IsNullOrEmpty(query.TraceId))
            baseQuery = baseQuery.Where(l => l.TraceId == query.TraceId);

        if (!string.IsNullOrEmpty(query.UserId))
            baseQuery = baseQuery.Where(l => l.UserId == query.UserId);

        if (!string.IsNullOrEmpty(query.ClientId))
            baseQuery = baseQuery.Where(l => l.ClientId == query.ClientId);

        if (query.HasException == true)
            baseQuery = baseQuery.Where(l => l.Exception != null);
        else if (query.HasException == false)
            baseQuery = baseQuery.Where(l => l.Exception == null);

        if (query.StartDate.HasValue)
            baseQuery = baseQuery.Where(l => l.Timestamp >= query.StartDate.Value);

        if (query.EndDate.HasValue)
            baseQuery = baseQuery.Where(l => l.Timestamp <= query.EndDate.Value);

        if (!string.IsNullOrEmpty(query.SearchTerm))
        {
            var term = query.SearchTerm.ToLower();
            baseQuery = baseQuery.Where(l =>
                l.Message.ToLower().Contains(term) ||
                (l.Exception != null && l.Exception.ToLower().Contains(term)) ||
                l.Category.ToLower().Contains(term));
        }

        // Count total
        var totalCount = await baseQuery.CountAsync(cancellationToken);

        // Apply sorting
        baseQuery = query.SortBy.ToLowerInvariant() switch
        {
            "timestamp" => query.SortDescending
                ? baseQuery.OrderByDescending(l => l.Timestamp)
                : baseQuery.OrderBy(l => l.Timestamp),
            "level" => query.SortDescending
                ? baseQuery.OrderByDescending(l => l.Level)
                : baseQuery.OrderBy(l => l.Level),
            "category" => query.SortDescending
                ? baseQuery.OrderByDescending(l => l.Category)
                : baseQuery.OrderBy(l => l.Category),
            _ => baseQuery.OrderByDescending(l => l.Timestamp)
        };

        // Apply pagination
        var items = await baseQuery
            .Skip((query.Page - 1) * query.PageSize)
            .Take(query.PageSize)
            .ToListAsync(cancellationToken);

        return new TelemetryLogQueryResult
        {
            Items = items,
            TotalCount = totalCount,
            Page = query.Page,
            PageSize = query.PageSize
        };
    }

    public async Task<TelemetryLog?> GetByIdAsync(long id, CancellationToken cancellationToken = default)
    {
        return await _context.TelemetryLogs.FirstOrDefaultAsync(l => l.Id == id, cancellationToken);
    }

    public async Task<IEnumerable<string>> GetCategoriesAsync(string? tenantId, CancellationToken cancellationToken = default)
    {
        var query = _context.TelemetryLogs.AsQueryable();

        if (!string.IsNullOrEmpty(tenantId))
            query = query.Where(l => l.TenantId == tenantId);

        return await query
            .Select(l => l.Category)
            .Distinct()
            .OrderBy(c => c)
            .Take(100)
            .ToListAsync(cancellationToken);
    }

    public async Task<int> PurgeOldLogsAsync(string? tenantId, DateTime cutoffDate, CancellationToken cancellationToken = default)
    {
        var query = _context.TelemetryLogs
            .Where(l => l.Timestamp < cutoffDate);

        if (!string.IsNullOrEmpty(tenantId))
            query = query.Where(l => l.TenantId == tenantId);

        return await query.ExecuteDeleteAsync(cancellationToken);
    }
}
