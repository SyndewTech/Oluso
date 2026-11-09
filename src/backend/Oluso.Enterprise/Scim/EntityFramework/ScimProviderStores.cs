using Microsoft.EntityFrameworkCore;
using Oluso.Enterprise.Scim.Entities;
using Oluso.Enterprise.Scim.Stores;

namespace Oluso.Enterprise.Scim.EntityFramework;

public class ScimProviderStore : IScimProviderStore
{
    private readonly ScimDbContext _context;

    public ScimProviderStore(ScimDbContext context)
    {
        _context = context;
    }

    public Task<ScimProvider?> GetByIdAsync(string id, CancellationToken cancellationToken = default) =>
        _context.ScimProviders.FirstOrDefaultAsync(x => x.Id == id, cancellationToken);

    public async Task<IReadOnlyList<ScimProvider>> GetByTenantAsync(string tenantId, CancellationToken cancellationToken = default) =>
        await _context.ScimProviders
            .Where(x => x.TenantId == tenantId)
            .OrderBy(x => x.Name)
            .ToListAsync(cancellationToken);

    public async Task<IReadOnlyList<ScimProvider>> GetEnabledByTenantAsync(string tenantId, CancellationToken cancellationToken = default) =>
        await _context.ScimProviders
            .Where(x => x.TenantId == tenantId && x.IsEnabled)
            .OrderBy(x => x.Name)
            .ToListAsync(cancellationToken);

    public async Task<IReadOnlyList<ScimProvider>> GetReconcileDueAsync(DateTime now, CancellationToken cancellationToken = default)
    {
        var candidates = await _context.ScimProviders
            .Where(x => x.IsEnabled && x.ReconcileIntervalMinutes > 0 && x.SecretUpdatedAt != null)
            .ToListAsync(cancellationToken);

        return candidates
            .Where(p => p.LastReconcileAt == null || p.LastReconcileAt.Value.AddMinutes(p.ReconcileIntervalMinutes) <= now)
            .ToList();
    }

    public async Task<ScimProvider> CreateAsync(ScimProvider provider, CancellationToken cancellationToken = default)
    {
        _context.ScimProviders.Add(provider);
        await _context.SaveChangesAsync(cancellationToken);
        return provider;
    }

    public async Task UpdateAsync(ScimProvider provider, CancellationToken cancellationToken = default)
    {
        _context.ScimProviders.Update(provider);
        await _context.SaveChangesAsync(cancellationToken);
    }

    public async Task DeleteAsync(string id, CancellationToken cancellationToken = default)
    {
        var provider = await GetByIdAsync(id, cancellationToken);
        if (provider == null)
            return;

        await _context.ScimProviderResourceMappings.Where(x => x.ScimProviderId == id).ExecuteDeleteAsync(cancellationToken);
        await _context.ScimProviderAttributeMappings.Where(x => x.ScimProviderId == id).ExecuteDeleteAsync(cancellationToken);
        await _context.ScimProviderOutbox.Where(x => x.ScimProviderId == id).ExecuteDeleteAsync(cancellationToken);
        await _context.ScimProviderLogs.Where(x => x.ScimProviderId == id).ExecuteDeleteAsync(cancellationToken);

        _context.ScimProviders.Remove(provider);
        await _context.SaveChangesAsync(cancellationToken);
    }

    public async Task RecordOutcomeAsync(string id, bool success, string? error, CancellationToken cancellationToken = default)
    {
        var now = DateTime.UtcNow;
        if (success)
        {
            await _context.ScimProviders
                .Where(x => x.Id == id)
                .ExecuteUpdateAsync(s => s
                    .SetProperty(x => x.SuccessCount, x => x.SuccessCount + 1)
                    .SetProperty(x => x.LastSuccessAt, now), cancellationToken);
        }
        else
        {
            var message = error == null ? null : (error.Length > 2000 ? error[..2000] : error);
            await _context.ScimProviders
                .Where(x => x.Id == id)
                .ExecuteUpdateAsync(s => s
                    .SetProperty(x => x.ErrorCount, x => x.ErrorCount + 1)
                    .SetProperty(x => x.LastErrorAt, now)
                    .SetProperty(x => x.LastError, message), cancellationToken);
        }
    }
}

public class ScimProviderResourceMappingStore : IScimProviderResourceMappingStore
{
    private readonly ScimDbContext _context;

    public ScimProviderResourceMappingStore(ScimDbContext context)
    {
        _context = context;
    }

    public Task<ScimProviderResourceMapping?> GetByInternalIdAsync(string scimProviderId, string resourceType, string internalId, CancellationToken cancellationToken = default) =>
        _context.ScimProviderResourceMappings.FirstOrDefaultAsync(
            x => x.ScimProviderId == scimProviderId && x.ResourceType == resourceType && x.InternalId == internalId,
            cancellationToken);

    public async Task<IReadOnlyDictionary<string, ScimProviderResourceMapping>> GetByInternalIdsAsync(string scimProviderId, string resourceType, IReadOnlyCollection<string> internalIds, CancellationToken cancellationToken = default)
    {
        if (internalIds.Count == 0)
            return new Dictionary<string, ScimProviderResourceMapping>();

        var rows = await _context.ScimProviderResourceMappings
            .Where(x => x.ScimProviderId == scimProviderId && x.ResourceType == resourceType && internalIds.Contains(x.InternalId))
            .ToListAsync(cancellationToken);

        return rows.ToDictionary(x => x.InternalId);
    }

    public Task<ScimProviderResourceMapping?> GetByRemoteIdAsync(string scimProviderId, string resourceType, string remoteId, CancellationToken cancellationToken = default) =>
        _context.ScimProviderResourceMappings.FirstOrDefaultAsync(
            x => x.ScimProviderId == scimProviderId && x.ResourceType == resourceType && x.RemoteId == remoteId,
            cancellationToken);

    public async Task<IReadOnlyList<ScimProviderResourceMapping>> GetByProviderAsync(string scimProviderId, string? resourceType, int skip, int take, CancellationToken cancellationToken = default)
    {
        var query = _context.ScimProviderResourceMappings.Where(x => x.ScimProviderId == scimProviderId);
        if (!string.IsNullOrEmpty(resourceType))
            query = query.Where(x => x.ResourceType == resourceType);

        return await query
            .OrderByDescending(x => x.LastSyncedAt ?? x.CreatedAt)
            .Skip(skip)
            .Take(take)
            .ToListAsync(cancellationToken);
    }

    public Task<int> GetCountByProviderAsync(string scimProviderId, string? resourceType, CancellationToken cancellationToken = default)
    {
        var query = _context.ScimProviderResourceMappings.Where(x => x.ScimProviderId == scimProviderId);
        if (!string.IsNullOrEmpty(resourceType))
            query = query.Where(x => x.ResourceType == resourceType);
        return query.CountAsync(cancellationToken);
    }

    public async Task<IReadOnlyList<ScimProviderResourceMapping>> GetAllByProviderAsync(string scimProviderId, string resourceType, CancellationToken cancellationToken = default) =>
        await _context.ScimProviderResourceMappings
            .Where(x => x.ScimProviderId == scimProviderId && x.ResourceType == resourceType)
            .ToListAsync(cancellationToken);

    public async Task<ScimProviderResourceMapping> CreateAsync(ScimProviderResourceMapping mapping, CancellationToken cancellationToken = default)
    {
        _context.ScimProviderResourceMappings.Add(mapping);
        await _context.SaveChangesAsync(cancellationToken);
        return mapping;
    }

    public async Task UpdateAsync(ScimProviderResourceMapping mapping, CancellationToken cancellationToken = default)
    {
        _context.ScimProviderResourceMappings.Update(mapping);
        await _context.SaveChangesAsync(cancellationToken);
    }

    public async Task DeleteAsync(string id, CancellationToken cancellationToken = default)
    {
        await _context.ScimProviderResourceMappings.Where(x => x.Id == id).ExecuteDeleteAsync(cancellationToken);
    }
}

public class ScimProviderAttributeMappingStore : IScimProviderAttributeMappingStore
{
    private readonly ScimDbContext _context;

    public ScimProviderAttributeMappingStore(ScimDbContext context)
    {
        _context = context;
    }

    public Task<ScimProviderAttributeMapping?> GetByIdAsync(string id, CancellationToken cancellationToken = default) =>
        _context.ScimProviderAttributeMappings.FirstOrDefaultAsync(x => x.Id == id, cancellationToken);

    public async Task<IReadOnlyList<ScimProviderAttributeMapping>> GetByProviderAsync(string scimProviderId, CancellationToken cancellationToken = default) =>
        await _context.ScimProviderAttributeMappings
            .Where(x => x.ScimProviderId == scimProviderId)
            .OrderBy(x => x.ScimAttribute)
            .ToListAsync(cancellationToken);

    public async Task<ScimProviderAttributeMapping> CreateAsync(ScimProviderAttributeMapping mapping, CancellationToken cancellationToken = default)
    {
        _context.ScimProviderAttributeMappings.Add(mapping);
        await _context.SaveChangesAsync(cancellationToken);
        return mapping;
    }

    public async Task UpdateAsync(ScimProviderAttributeMapping mapping, CancellationToken cancellationToken = default)
    {
        _context.ScimProviderAttributeMappings.Update(mapping);
        await _context.SaveChangesAsync(cancellationToken);
    }

    public async Task DeleteAsync(string id, CancellationToken cancellationToken = default)
    {
        await _context.ScimProviderAttributeMappings.Where(x => x.Id == id).ExecuteDeleteAsync(cancellationToken);
    }
}

public class ScimProviderOutboxStore : IScimProviderOutboxStore
{
    private readonly ScimDbContext _context;

    public ScimProviderOutboxStore(ScimDbContext context)
    {
        _context = context;
    }

    public async Task<ScimProviderOutboxItem> EnqueueAsync(ScimProviderOutboxItem item, CancellationToken cancellationToken = default)
    {
        var open = await _context.ScimProviderOutbox
            .Where(x => x.ScimProviderId == item.ScimProviderId
                        && x.ResourceType == item.ResourceType
                        && x.InternalId == item.InternalId
                        && (x.Status == ScimOutboxStatuses.Pending || x.Status == ScimOutboxStatuses.Failed))
            .ToListAsync(cancellationToken);

        var same = open.FirstOrDefault(x => x.Operation == item.Operation);
        if (same != null)
        {
            // Already queued; pull a failed item forward so the new trigger is not delayed by old backoff.
            if (same.Status == ScimOutboxStatuses.Failed || same.NextAttemptAt > item.NextAttemptAt)
            {
                same.Status = ScimOutboxStatuses.Pending;
                same.NextAttemptAt = item.NextAttemptAt;
                same.UpdatedAt = DateTime.UtcNow;
                await _context.SaveChangesAsync(cancellationToken);
            }
            return same;
        }

        foreach (var other in open)
        {
            other.Status = ScimOutboxStatuses.Superseded;
            other.UpdatedAt = DateTime.UtcNow;
            other.CompletedAt = DateTime.UtcNow;
        }

        _context.ScimProviderOutbox.Add(item);
        await _context.SaveChangesAsync(cancellationToken);
        return item;
    }

    public async Task<IReadOnlyList<string>> GetDueProviderIdsAsync(DateTime now, int limit, CancellationToken cancellationToken = default) =>
        await _context.ScimProviderOutbox
            .Where(x => (x.Status == ScimOutboxStatuses.Pending || x.Status == ScimOutboxStatuses.Failed) && x.NextAttemptAt <= now)
            .Select(x => x.ScimProviderId)
            .Distinct()
            .Take(limit)
            .ToListAsync(cancellationToken);

    public async Task<ScimProviderOutboxItem?> ClaimNextAsync(string scimProviderId, string workerId, DateTime now, TimeSpan lockDuration, CancellationToken cancellationToken = default)
    {
        // Loop: a competing worker may take the candidate between select and claim.
        for (var attempt = 0; attempt < 5; attempt++)
        {
            var candidate = await _context.ScimProviderOutbox
                .Where(x => x.ScimProviderId == scimProviderId
                            && (x.Status == ScimOutboxStatuses.Pending || x.Status == ScimOutboxStatuses.Failed)
                            && x.NextAttemptAt <= now)
                .OrderBy(x => x.Priority)
                .ThenBy(x => x.CreatedAt)
                .AsNoTracking()
                .FirstOrDefaultAsync(cancellationToken);

            if (candidate == null)
                return null;

            var previousStatus = candidate.Status;
            var lockedUntil = now.Add(lockDuration);
            var claimed = await _context.ScimProviderOutbox
                .Where(x => x.Id == candidate.Id && x.Status == previousStatus)
                .ExecuteUpdateAsync(s => s
                    .SetProperty(x => x.Status, ScimOutboxStatuses.Processing)
                    .SetProperty(x => x.LockedUntil, lockedUntil)
                    .SetProperty(x => x.LockedBy, workerId)
                    .SetProperty(x => x.Attempts, x => x.Attempts + 1)
                    .SetProperty(x => x.UpdatedAt, now), cancellationToken);

            if (claimed == 1)
            {
                candidate.Status = ScimOutboxStatuses.Processing;
                candidate.LockedUntil = lockedUntil;
                candidate.LockedBy = workerId;
                candidate.Attempts += 1;
                return candidate;
            }
        }

        return null;
    }

    public Task MarkSucceededAsync(string id, int? statusCode, CancellationToken cancellationToken = default)
    {
        var now = DateTime.UtcNow;
        return _context.ScimProviderOutbox
            .Where(x => x.Id == id)
            .ExecuteUpdateAsync(s => s
                .SetProperty(x => x.Status, ScimOutboxStatuses.Succeeded)
                .SetProperty(x => x.LockedUntil, (DateTime?)null)
                .SetProperty(x => x.LockedBy, (string?)null)
                .SetProperty(x => x.LastStatusCode, statusCode)
                .SetProperty(x => x.LastError, (string?)null)
                .SetProperty(x => x.UpdatedAt, now)
                .SetProperty(x => x.CompletedAt, now), cancellationToken);
    }

    public Task MarkFailedAsync(string id, int? statusCode, string error, DateTime nextAttemptAt, CancellationToken cancellationToken = default)
    {
        var now = DateTime.UtcNow;
        var message = Truncate(error);
        return _context.ScimProviderOutbox
            .Where(x => x.Id == id)
            .ExecuteUpdateAsync(s => s
                .SetProperty(x => x.Status, ScimOutboxStatuses.Failed)
                .SetProperty(x => x.LockedUntil, (DateTime?)null)
                .SetProperty(x => x.LockedBy, (string?)null)
                .SetProperty(x => x.LastStatusCode, statusCode)
                .SetProperty(x => x.LastError, message)
                .SetProperty(x => x.NextAttemptAt, nextAttemptAt)
                .SetProperty(x => x.UpdatedAt, now), cancellationToken);
    }

    public Task MarkDeadAsync(string id, int? statusCode, string error, CancellationToken cancellationToken = default)
    {
        var now = DateTime.UtcNow;
        var message = Truncate(error);
        return _context.ScimProviderOutbox
            .Where(x => x.Id == id)
            .ExecuteUpdateAsync(s => s
                .SetProperty(x => x.Status, ScimOutboxStatuses.Dead)
                .SetProperty(x => x.LockedUntil, (DateTime?)null)
                .SetProperty(x => x.LockedBy, (string?)null)
                .SetProperty(x => x.LastStatusCode, statusCode)
                .SetProperty(x => x.LastError, message)
                .SetProperty(x => x.UpdatedAt, now)
                .SetProperty(x => x.CompletedAt, now), cancellationToken);
    }

    public Task<int> ReleaseExpiredLocksAsync(DateTime now, CancellationToken cancellationToken = default) =>
        _context.ScimProviderOutbox
            .Where(x => x.Status == ScimOutboxStatuses.Processing && x.LockedUntil != null && x.LockedUntil < now)
            .ExecuteUpdateAsync(s => s
                .SetProperty(x => x.Status, ScimOutboxStatuses.Pending)
                .SetProperty(x => x.LockedUntil, (DateTime?)null)
                .SetProperty(x => x.LockedBy, (string?)null)
                .SetProperty(x => x.LastError, "Lock expired; worker did not finish")
                .SetProperty(x => x.UpdatedAt, now), cancellationToken);

    public async Task<ScimOutboxSummary> GetSummaryAsync(string scimProviderId, CancellationToken cancellationToken = default)
    {
        var since = DateTime.UtcNow.AddHours(-24);
        var counts = await _context.ScimProviderOutbox
            .Where(x => x.ScimProviderId == scimProviderId)
            .GroupBy(x => x.Status)
            .Select(g => new { Status = g.Key, Count = g.Count() })
            .ToListAsync(cancellationToken);

        int Count(string status) => counts.FirstOrDefault(c => c.Status == status)?.Count ?? 0;

        var succeededRecent = await _context.ScimProviderOutbox
            .CountAsync(x => x.ScimProviderId == scimProviderId && x.Status == ScimOutboxStatuses.Succeeded && x.CompletedAt >= since, cancellationToken);

        var oldestPending = await _context.ScimProviderOutbox
            .Where(x => x.ScimProviderId == scimProviderId && (x.Status == ScimOutboxStatuses.Pending || x.Status == ScimOutboxStatuses.Failed))
            .OrderBy(x => x.CreatedAt)
            .Select(x => (DateTime?)x.CreatedAt)
            .FirstOrDefaultAsync(cancellationToken);

        return new ScimOutboxSummary
        {
            Pending = Count(ScimOutboxStatuses.Pending),
            Processing = Count(ScimOutboxStatuses.Processing),
            Failed = Count(ScimOutboxStatuses.Failed),
            Dead = Count(ScimOutboxStatuses.Dead),
            SucceededLast24h = succeededRecent,
            OldestPendingAt = oldestPending
        };
    }

    public async Task<IReadOnlyList<ScimProviderOutboxItem>> GetByProviderAsync(string scimProviderId, string[]? statuses, int skip, int take, CancellationToken cancellationToken = default)
    {
        var query = _context.ScimProviderOutbox.Where(x => x.ScimProviderId == scimProviderId);
        if (statuses is { Length: > 0 })
            query = query.Where(x => statuses.Contains(x.Status));

        return await query
            .OrderByDescending(x => x.UpdatedAt ?? x.CreatedAt)
            .Skip(skip)
            .Take(take)
            .ToListAsync(cancellationToken);
    }

    public Task<int> GetCountByProviderAsync(string scimProviderId, string[]? statuses, CancellationToken cancellationToken = default)
    {
        var query = _context.ScimProviderOutbox.Where(x => x.ScimProviderId == scimProviderId);
        if (statuses is { Length: > 0 })
            query = query.Where(x => statuses.Contains(x.Status));
        return query.CountAsync(cancellationToken);
    }

    public Task<int> RetryFailedAsync(string scimProviderId, CancellationToken cancellationToken = default)
    {
        var now = DateTime.UtcNow;
        return _context.ScimProviderOutbox
            .Where(x => x.ScimProviderId == scimProviderId && (x.Status == ScimOutboxStatuses.Failed || x.Status == ScimOutboxStatuses.Dead))
            .ExecuteUpdateAsync(s => s
                .SetProperty(x => x.Status, ScimOutboxStatuses.Pending)
                .SetProperty(x => x.Attempts, 0)
                .SetProperty(x => x.NextAttemptAt, now)
                .SetProperty(x => x.CompletedAt, (DateTime?)null)
                .SetProperty(x => x.UpdatedAt, now), cancellationToken);
    }

    public Task<int> DeleteCompletedOlderThanAsync(DateTime cutoff, CancellationToken cancellationToken = default) =>
        _context.ScimProviderOutbox
            .Where(x => (x.Status == ScimOutboxStatuses.Succeeded || x.Status == ScimOutboxStatuses.Superseded) && x.CompletedAt < cutoff)
            .ExecuteDeleteAsync(cancellationToken);

    private static string Truncate(string value) => value.Length > 2000 ? value[..2000] : value;
}

public class ScimProviderLogStore : IScimProviderLogStore
{
    private readonly ScimDbContext _context;

    public ScimProviderLogStore(ScimDbContext context)
    {
        _context = context;
    }

    public async Task<ScimProviderLog> CreateAsync(ScimProviderLog log, CancellationToken cancellationToken = default)
    {
        _context.ScimProviderLogs.Add(log);
        await _context.SaveChangesAsync(cancellationToken);
        return log;
    }

    public async Task<IReadOnlyList<ScimProviderLog>> GetByProviderAsync(string scimProviderId, bool? success, int skip, int take, CancellationToken cancellationToken = default)
    {
        var query = _context.ScimProviderLogs.Where(x => x.ScimProviderId == scimProviderId);
        if (success.HasValue)
            query = query.Where(x => x.Success == success.Value);

        return await query
            .OrderByDescending(x => x.Timestamp)
            .Skip(skip)
            .Take(take)
            .ToListAsync(cancellationToken);
    }

    public Task<int> GetCountByProviderAsync(string scimProviderId, bool? success, CancellationToken cancellationToken = default)
    {
        var query = _context.ScimProviderLogs.Where(x => x.ScimProviderId == scimProviderId);
        if (success.HasValue)
            query = query.Where(x => x.Success == success.Value);
        return query.CountAsync(cancellationToken);
    }

    public Task DeleteOlderThanAsync(DateTime cutoff, CancellationToken cancellationToken = default) =>
        _context.ScimProviderLogs.Where(x => x.Timestamp < cutoff).ExecuteDeleteAsync(cancellationToken);
}
