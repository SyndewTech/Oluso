using Microsoft.EntityFrameworkCore;
using Oluso.Enterprise.Scim.Entities;
using Oluso.Enterprise.Scim.Stores;

namespace Oluso.Enterprise.Scim.EntityFramework;

/// <summary>
/// Entity Framework implementation of IScimClientStore
/// </summary>
public class ScimClientStore : IScimClientStore
{
    private readonly ScimDbContext _context;

    public ScimClientStore(ScimDbContext context)
    {
        _context = context;
    }

    public async Task<ScimClient?> GetByIdAsync(string id, CancellationToken cancellationToken = default)
    {
        return await _context.ScimClients
            .FirstOrDefaultAsync(x => x.Id == id, cancellationToken);
    }

    public async Task<ScimClient?> GetByTokenHashAsync(string tokenHash, CancellationToken cancellationToken = default)
    {
        return await _context.ScimClients
            .FirstOrDefaultAsync(x => x.TokenHash == tokenHash, cancellationToken);
    }

    public async Task<IReadOnlyList<ScimClient>> GetByTenantAsync(string tenantId, CancellationToken cancellationToken = default)
    {
        return await _context.ScimClients
            .Where(x => x.TenantId == tenantId)
            .OrderBy(x => x.Name)
            .ToListAsync(cancellationToken);
    }

    public async Task<ScimClient> CreateAsync(ScimClient client, CancellationToken cancellationToken = default)
    {
        _context.ScimClients.Add(client);
        await _context.SaveChangesAsync(cancellationToken);
        return client;
    }

    public async Task UpdateAsync(ScimClient client, CancellationToken cancellationToken = default)
    {
        _context.ScimClients.Update(client);
        await _context.SaveChangesAsync(cancellationToken);
    }

    public async Task DeleteAsync(string id, CancellationToken cancellationToken = default)
    {
        var client = await GetByIdAsync(id, cancellationToken);
        if (client != null)
        {
            _context.ScimClients.Remove(client);
            await _context.SaveChangesAsync(cancellationToken);
        }
    }

    public async Task UpdateActivityAsync(string id, bool success, CancellationToken cancellationToken = default)
    {
        var client = await _context.ScimClients
            .FirstOrDefaultAsync(x => x.Id == id, cancellationToken);

        if (client != null)
        {
            client.LastActivityAt = DateTime.UtcNow;
            if (success)
                client.SuccessCount++;
            else
                client.ErrorCount++;

            await _context.SaveChangesAsync(cancellationToken);
        }
    }
}
