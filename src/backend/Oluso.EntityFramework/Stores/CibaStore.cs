using Microsoft.EntityFrameworkCore;
using Oluso.Core.Domain.Interfaces;

namespace Oluso.EntityFramework.Stores;

/// <summary>
/// Entity Framework implementation of ICibaStore
/// </summary>
public class CibaStore : ICibaStore
{
    private readonly IOlusoDbContext _context;

    public CibaStore(IOlusoDbContext context)
    {
        _context = context;
    }

    public async Task StoreRequestAsync(
        CibaRequest request,
        CancellationToken cancellationToken = default)
    {
        _context.CibaRequests.Add(request);
        await _context.SaveChangesAsync(cancellationToken);
    }

    public async Task<CibaRequest?> GetByAuthReqIdAsync(
        string authReqId,
        CancellationToken cancellationToken = default)
    {
        return await _context.CibaRequests
            .FirstOrDefaultAsync(r => r.AuthReqId == authReqId, cancellationToken);
    }

    public async Task<IReadOnlyList<CibaRequest>> GetPendingBySubjectAsync(
        string subjectId,
        CancellationToken cancellationToken = default)
    {
        var now = DateTime.UtcNow;
        return await _context.CibaRequests
            .Where(r => r.SubjectId == subjectId
                && r.Status == CibaRequestStatus.Pending
                && r.ExpiresAt > now)
            .OrderByDescending(r => r.CreatedAt)
            .ToListAsync(cancellationToken);
    }

    public async Task UpdateRequestAsync(
        CibaRequest request,
        CancellationToken cancellationToken = default)
    {
        var existing = await _context.CibaRequests
            .FirstOrDefaultAsync(r => r.AuthReqId == request.AuthReqId, cancellationToken);

        if (existing != null)
        {
            existing.Status = request.Status;
            existing.CompletedAt = request.CompletedAt;
            existing.SessionId = request.SessionId;
            existing.Error = request.Error;
            existing.ErrorDescription = request.ErrorDescription;
            await _context.SaveChangesAsync(cancellationToken);
        }
    }

    public async Task RemoveRequestAsync(
        string authReqId,
        CancellationToken cancellationToken = default)
    {
        var request = await _context.CibaRequests
            .FirstOrDefaultAsync(r => r.AuthReqId == authReqId, cancellationToken);

        if (request != null)
        {
            _context.CibaRequests.Remove(request);
            await _context.SaveChangesAsync(cancellationToken);
        }
    }

    public async Task<int> RemoveExpiredRequestsAsync(CancellationToken cancellationToken = default)
    {
        var now = DateTime.UtcNow;
        var expiredRequests = await _context.CibaRequests
            .Where(r => r.ExpiresAt < now)
            .ToListAsync(cancellationToken);

        if (expiredRequests.Count > 0)
        {
            _context.CibaRequests.RemoveRange(expiredRequests);
            await _context.SaveChangesAsync(cancellationToken);
        }

        return expiredRequests.Count;
    }
}
