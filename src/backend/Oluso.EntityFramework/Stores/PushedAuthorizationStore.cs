using Microsoft.EntityFrameworkCore;
using Oluso.Core.Domain.Entities;
using Oluso.Core.Domain.Interfaces;

namespace Oluso.EntityFramework.Stores;

/// <summary>
/// Entity Framework implementation of IPushedAuthorizationStore (RFC 9126)
/// </summary>
public class PushedAuthorizationStore : IPushedAuthorizationStore
{
    private readonly IOlusoDbContext _context;

    public PushedAuthorizationStore(IOlusoDbContext context)
    {
        _context = context;
    }

    public async Task StoreAsync(
        PushedAuthorizationRequest request,
        CancellationToken cancellationToken = default)
    {
        _context.PushedAuthorizationRequests.Add(request);
        await _context.SaveChangesAsync(cancellationToken);
    }

    public async Task<PushedAuthorizationRequest?> GetAsync(
        string requestUri,
        CancellationToken cancellationToken = default)
    {
        var request = await _context.PushedAuthorizationRequests
            .FirstOrDefaultAsync(p => p.RequestUri == requestUri, cancellationToken);

        // Return null if expired
        if (request != null && request.ExpiresAtUtc < DateTime.UtcNow)
        {
            return null;
        }

        return request;
    }

    public async Task RemoveAsync(string requestUri, CancellationToken cancellationToken = default)
    {
        var request = await _context.PushedAuthorizationRequests
            .FirstOrDefaultAsync(p => p.RequestUri == requestUri, cancellationToken);

        if (request != null)
        {
            _context.PushedAuthorizationRequests.Remove(request);
            await _context.SaveChangesAsync(cancellationToken);
        }
    }

    public async Task RemoveExpiredAsync(CancellationToken cancellationToken = default)
    {
        var expired = await _context.PushedAuthorizationRequests
            .Where(p => p.ExpiresAtUtc < DateTime.UtcNow)
            .ToListAsync(cancellationToken);

        if (expired.Any())
        {
            _context.PushedAuthorizationRequests.RemoveRange(expired);
            await _context.SaveChangesAsync(cancellationToken);
        }
    }
}
