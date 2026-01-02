using Microsoft.EntityFrameworkCore;
using Oluso.Core.Domain.Entities;
using Oluso.Core.Domain.Interfaces;

namespace Oluso.EntityFramework.Stores;

/// <summary>
/// Entity Framework implementation of IClientStore with tenant isolation
/// </summary>
public class ClientStore : IClientStore
{
    private readonly IOlusoDbContext _context;
    private readonly ITenantContext _tenantContext;

    public ClientStore(IOlusoDbContext context, ITenantContext tenantContext)
    {
        _context = context;
        _tenantContext = tenantContext;
    }

    public async Task<Client?> FindClientByIdAsync(string clientId, CancellationToken cancellationToken = default)
    {
        return await _context.Clients
            .Include(c => c.ClientSecrets)
            .Include(c => c.AllowedGrantTypes)
            .Include(c => c.RedirectUris)
            .Include(c => c.PostLogoutRedirectUris)
            .Include(c => c.AllowedScopes)
            .Include(c => c.Claims)
            .Include(c => c.AllowedCorsOrigins)
            .Include(c => c.Properties)
            .Include(c => c.IdentityProviderRestrictions)
            .Include(c => c.AllowedRoles)
            .Include(c => c.AllowedUsers)
            .FirstOrDefaultAsync(c => c.ClientId == clientId, cancellationToken);
    }

    public async Task<IEnumerable<Client>> GetAllClientsAsync(CancellationToken cancellationToken = default)
    {
        return await _context.Clients
            .Include(c => c.AllowedGrantTypes)
            .Include(c => c.AllowedScopes)
            .ToListAsync(cancellationToken);
    }

    public async Task<Client> AddClientAsync(Client client, CancellationToken cancellationToken = default)
    {
        // Always set TenantId from context to prevent bypass attacks
        client.TenantId = _tenantContext.TenantId;
        _context.Clients.Add(client);
        await _context.SaveChangesAsync(cancellationToken);
        return client;
    }

    public async Task<Client> UpdateClientAsync(Client client, CancellationToken cancellationToken = default)
    {
        client.Updated = DateTime.UtcNow;
        // Note: For complex updates with collections, you may need to implement
        // the same EF Core tracking pattern as shown in the original code
        await _context.SaveChangesAsync(cancellationToken);
        return client;
    }

    public async Task DeleteClientAsync(string clientId, CancellationToken cancellationToken = default)
    {
        var client = await _context.Clients.FirstOrDefaultAsync(c => c.ClientId == clientId, cancellationToken);
        if (client != null)
        {
            _context.Clients.Remove(client);
            await _context.SaveChangesAsync(cancellationToken);
        }
    }
}
