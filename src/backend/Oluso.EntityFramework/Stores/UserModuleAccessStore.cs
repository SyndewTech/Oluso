using Microsoft.EntityFrameworkCore;
using Oluso.Core.Domain.Entities;
using Oluso.Core.Domain.Interfaces;

namespace Oluso.EntityFramework.Stores;

public class UserModuleAccessStore : IUserModuleAccessStore
{
    private readonly OlusoDbContext _context;

    public UserModuleAccessStore(OlusoDbContext context)
    {
        _context = context;
    }

    public async Task<IReadOnlyList<UserModuleAccess>> GetAccessAsync(string tenantId, string featureKey, CancellationToken cancellationToken = default)
    {
        return await _context.UserModuleAccess
            .Where(a => a.TenantId == tenantId && a.FeatureKey == featureKey)
            .OrderBy(a => a.GrantedAt)
            .ToListAsync(cancellationToken);
    }

    public async Task<bool> HasAccessAsync(string tenantId, string userId, string featureKey, CancellationToken cancellationToken = default)
    {
        return await _context.UserModuleAccess
            .AnyAsync(a => a.TenantId == tenantId && a.UserId == userId && a.FeatureKey == featureKey, cancellationToken);
    }

    public async Task<int> GetSeatCountAsync(string tenantId, string featureKey, CancellationToken cancellationToken = default)
    {
        return await _context.UserModuleAccess
            .CountAsync(a => a.TenantId == tenantId && a.FeatureKey == featureKey, cancellationToken);
    }

    public async Task GrantAccessAsync(string tenantId, string userId, string featureKey, string? grantedByUserId, CancellationToken cancellationToken = default)
    {
        var exists = await HasAccessAsync(tenantId, userId, featureKey, cancellationToken);
        if (exists)
            return;

        _context.UserModuleAccess.Add(new UserModuleAccess
        {
            TenantId = tenantId,
            UserId = userId,
            FeatureKey = featureKey,
            GrantedByUserId = grantedByUserId,
            GrantedAt = DateTime.UtcNow,
        });

        await _context.SaveChangesAsync(cancellationToken);
    }

    public async Task RevokeAccessAsync(string tenantId, string userId, string featureKey, CancellationToken cancellationToken = default)
    {
        var access = await _context.UserModuleAccess
            .FirstOrDefaultAsync(a => a.TenantId == tenantId && a.UserId == userId && a.FeatureKey == featureKey, cancellationToken);

        if (access != null)
        {
            _context.UserModuleAccess.Remove(access);
            await _context.SaveChangesAsync(cancellationToken);
        }
    }

    public async Task<IReadOnlyList<UserModuleAccess>> GetUserModulesAsync(string tenantId, string userId, CancellationToken cancellationToken = default)
    {
        return await _context.UserModuleAccess
            .Where(a => a.TenantId == tenantId && a.UserId == userId)
            .OrderBy(a => a.FeatureKey)
            .ToListAsync(cancellationToken);
    }
}
