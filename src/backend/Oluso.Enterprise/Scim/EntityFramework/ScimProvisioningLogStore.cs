using Microsoft.EntityFrameworkCore;
using Oluso.Enterprise.Scim.Entities;
using Oluso.Enterprise.Scim.Stores;

namespace Oluso.Enterprise.Scim.EntityFramework;

/// <summary>
/// Entity Framework implementation of IScimProvisioningLogStore
/// </summary>
public class ScimProvisioningLogStore : IScimProvisioningLogStore
{
    private readonly ScimDbContext _context;

    public ScimProvisioningLogStore(ScimDbContext context)
    {
        _context = context;
    }

    public async Task<ScimProvisioningLog> CreateAsync(ScimProvisioningLog log, CancellationToken cancellationToken = default)
    {
        _context.ScimProvisioningLogs.Add(log);
        await _context.SaveChangesAsync(cancellationToken);
        return log;
    }

    public async Task<IReadOnlyList<ScimProvisioningLog>> GetByClientAsync(
        string scimClientId,
        int skip = 0,
        int take = 50,
        CancellationToken cancellationToken = default)
    {
        return await _context.ScimProvisioningLogs
            .Where(x => x.ScimClientId == scimClientId)
            .OrderByDescending(x => x.Timestamp)
            .Skip(skip)
            .Take(take)
            .ToListAsync(cancellationToken);
    }

    public async Task<IReadOnlyList<ScimProvisioningLog>> GetByTenantAsync(
        string tenantId,
        int skip = 0,
        int take = 50,
        CancellationToken cancellationToken = default)
    {
        return await _context.ScimProvisioningLogs
            .Where(x => x.TenantId == tenantId)
            .OrderByDescending(x => x.Timestamp)
            .Skip(skip)
            .Take(take)
            .ToListAsync(cancellationToken);
    }

    public async Task<int> GetCountByClientAsync(string scimClientId, CancellationToken cancellationToken = default)
    {
        return await _context.ScimProvisioningLogs
            .CountAsync(x => x.ScimClientId == scimClientId, cancellationToken);
    }

    public async Task DeleteOlderThanAsync(DateTime cutoff, CancellationToken cancellationToken = default)
    {
        await _context.ScimProvisioningLogs
            .Where(x => x.Timestamp < cutoff)
            .ExecuteDeleteAsync(cancellationToken);
    }
}
