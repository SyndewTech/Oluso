using Microsoft.EntityFrameworkCore;
using Oluso.Core.Domain.Entities;
using Oluso.Core.Domain.Interfaces;

namespace Oluso.EntityFramework.Stores;

/// <summary>
/// Entity Framework implementation of IPersistedGrantStore
/// </summary>
public class PersistedGrantStore : IPersistedGrantStore
{
    private readonly IOlusoDbContext _context;

    public PersistedGrantStore(IOlusoDbContext context)
    {
        _context = context;
    }

    public async Task StoreAsync(PersistedGrant grant, CancellationToken cancellationToken = default)
    {
        var existing = await _context.PersistedGrants
            .FirstOrDefaultAsync(g => g.Key == grant.Key, cancellationToken);

        if (existing != null)
        {
            existing.Type = grant.Type;
            existing.SubjectId = grant.SubjectId;
            existing.SessionId = grant.SessionId;
            existing.ClientId = grant.ClientId;
            existing.Description = grant.Description;
            existing.CreationTime = grant.CreationTime;
            existing.Expiration = grant.Expiration;
            existing.ConsumedTime = grant.ConsumedTime;
            existing.Data = grant.Data;
        }
        else
        {
            _context.PersistedGrants.Add(grant);
        }

        await _context.SaveChangesAsync(cancellationToken);
    }

    public async Task<PersistedGrant?> GetAsync(string key, CancellationToken cancellationToken = default)
    {
        return await _context.PersistedGrants
            .FirstOrDefaultAsync(g => g.Key == key, cancellationToken);
    }

    public async Task<IEnumerable<PersistedGrant>> GetAllAsync(
        PersistedGrantFilter filter,
        CancellationToken cancellationToken = default)
    {
        var query = _context.PersistedGrants.AsQueryable();

        if (!string.IsNullOrEmpty(filter.SubjectId))
            query = query.Where(g => g.SubjectId == filter.SubjectId);

        if (!string.IsNullOrEmpty(filter.SessionId))
            query = query.Where(g => g.SessionId == filter.SessionId);

        if (!string.IsNullOrEmpty(filter.ClientId))
            query = query.Where(g => g.ClientId == filter.ClientId);

        if (!string.IsNullOrEmpty(filter.Type))
            query = query.Where(g => g.Type == filter.Type);

        return await query.ToListAsync(cancellationToken);
    }

    public async Task RemoveAsync(string key, CancellationToken cancellationToken = default)
    {
        var grant = await _context.PersistedGrants
            .FirstOrDefaultAsync(g => g.Key == key, cancellationToken);

        if (grant != null)
        {
            _context.PersistedGrants.Remove(grant);
            await _context.SaveChangesAsync(cancellationToken);
        }
    }

    public async Task RemoveAllAsync(PersistedGrantFilter filter, CancellationToken cancellationToken = default)
    {
        var query = _context.PersistedGrants.AsQueryable();

        if (!string.IsNullOrEmpty(filter.SubjectId))
            query = query.Where(g => g.SubjectId == filter.SubjectId);

        if (!string.IsNullOrEmpty(filter.SessionId))
            query = query.Where(g => g.SessionId == filter.SessionId);

        if (!string.IsNullOrEmpty(filter.ClientId))
            query = query.Where(g => g.ClientId == filter.ClientId);

        if (!string.IsNullOrEmpty(filter.Type))
            query = query.Where(g => g.Type == filter.Type);

        var grants = await query.ToListAsync(cancellationToken);
        _context.PersistedGrants.RemoveRange(grants);
        await _context.SaveChangesAsync(cancellationToken);
    }

    public async Task RemoveAllBySubjectAndClientAsync(
        string subjectId,
        string clientId,
        CancellationToken cancellationToken = default)
    {
        var grants = await _context.PersistedGrants
            .Where(g => g.SubjectId == subjectId && g.ClientId == clientId)
            .ToListAsync(cancellationToken);

        _context.PersistedGrants.RemoveRange(grants);
        await _context.SaveChangesAsync(cancellationToken);
    }
}
