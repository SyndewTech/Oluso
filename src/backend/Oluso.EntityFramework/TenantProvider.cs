using Microsoft.EntityFrameworkCore;
using Oluso.Core.Domain.Entities;

namespace Oluso.EntityFramework;

public class TenantProvider : ITenantProvider
{
    private readonly IOlusoDbContext _context;

    public TenantProvider(IOlusoDbContext context)
    {
        _context = context;
    }

    public async Task<IReadOnlyList<string>> GetActiveTenantIdsAsync(CancellationToken cancellationToken = default)
    {
        var tenantsDbSet = _context.Tenants;
        if (tenantsDbSet == null)
            return Array.Empty<string>();

        return await tenantsDbSet
            .Where(t => t.Enabled && t.Environment == TenantEnvironment.Production)
            .Select(t => t.Id)
            .ToListAsync(cancellationToken);
    }
}
