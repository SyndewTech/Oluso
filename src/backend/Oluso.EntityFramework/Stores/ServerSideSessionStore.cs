using Microsoft.EntityFrameworkCore;
using Oluso.Core.Domain.Entities;
using Oluso.Core.Domain.Interfaces;

namespace Oluso.EntityFramework.Stores;

/// <summary>
/// Entity Framework implementation of IServerSideSessionStore
/// </summary>
public class ServerSideSessionStore : IServerSideSessionStore
{
    private readonly IOlusoDbContext _context;

    public ServerSideSessionStore(IOlusoDbContext context)
    {
        _context = context;
    }

    public async Task<ServerSideSession> CreateSessionAsync(
        ServerSideSession session,
        CancellationToken cancellationToken = default)
    {
        _context.ServerSideSessions.Add(session);
        await _context.SaveChangesAsync(cancellationToken);
        return session;
    }

    public async Task<ServerSideSession?> GetSessionAsync(
        string key,
        CancellationToken cancellationToken = default)
    {
        return await _context.ServerSideSessions
            .FirstOrDefaultAsync(s => s.Key == key, cancellationToken);
    }

    public async Task<ServerSideSession?> GetBySessionIdAsync(
        string sessionId,
        CancellationToken cancellationToken = default)
    {
        return await _context.ServerSideSessions
            .FirstOrDefaultAsync(s => s.SessionId == sessionId, cancellationToken);
    }

    public async Task<IReadOnlyList<ServerSideSession>> GetSessionsBySubjectAsync(
        string subjectId,
        CancellationToken cancellationToken = default)
    {
        return await _context.ServerSideSessions
            .Where(s => s.SubjectId == subjectId)
            .OrderByDescending(s => s.Renewed)
            .ToListAsync(cancellationToken);
    }

    public async Task<IReadOnlyList<UserSession>> GetUserSessionsAsync(
        string subjectId,
        CancellationToken cancellationToken = default)
    {
        var sessions = await _context.ServerSideSessions
            .Where(s => s.SubjectId == subjectId)
            .OrderByDescending(s => s.Renewed)
            .ToListAsync(cancellationToken);

        return sessions.Select(s => new UserSession
        {
            SessionId = s.SessionId ?? s.Key,
            SubjectId = s.SubjectId,
            DisplayName = s.DisplayName,
            Created = s.Created,
            Renewed = s.Renewed,
            Expires = s.Expires
        }).ToList();
    }

    public async Task UpdateSessionAsync(
        ServerSideSession session,
        CancellationToken cancellationToken = default)
    {
        var existing = await _context.ServerSideSessions
            .FirstOrDefaultAsync(s => s.Key == session.Key, cancellationToken);

        if (existing != null)
        {
            existing.Renewed = session.Renewed;
            existing.Expires = session.Expires;
            existing.Data = session.Data;
            existing.DisplayName = session.DisplayName;
            await _context.SaveChangesAsync(cancellationToken);
        }
    }

    public async Task DeleteSessionAsync(
        string key,
        CancellationToken cancellationToken = default)
    {
        var session = await _context.ServerSideSessions
            .FirstOrDefaultAsync(s => s.Key == key, cancellationToken);

        if (session != null)
        {
            _context.ServerSideSessions.Remove(session);
            await _context.SaveChangesAsync(cancellationToken);
        }
    }

    public async Task DeleteBySessionIdAsync(
        string sessionId,
        CancellationToken cancellationToken = default)
    {
        var session = await _context.ServerSideSessions
            .FirstOrDefaultAsync(s => s.SessionId == sessionId, cancellationToken);

        if (session != null)
        {
            _context.ServerSideSessions.Remove(session);
            await _context.SaveChangesAsync(cancellationToken);
        }
    }

    public async Task DeleteSessionsBySubjectAsync(
        string subjectId,
        CancellationToken cancellationToken = default)
    {
        var sessions = await _context.ServerSideSessions
            .Where(s => s.SubjectId == subjectId)
            .ToListAsync(cancellationToken);

        if (sessions.Count > 0)
        {
            _context.ServerSideSessions.RemoveRange(sessions);
            await _context.SaveChangesAsync(cancellationToken);
        }
    }

    public async Task<IReadOnlyList<ServerSideSession>> GetSessionsBySubjectAndClientAsync(
        string subjectId,
        string clientId,
        CancellationToken cancellationToken = default)
    {
        // Note: This requires parsing the Data field which contains serialized session info
        // For now, we'll return all sessions for the subject
        // A proper implementation would store ClientId as a column or parse Data
        return await _context.ServerSideSessions
            .Where(s => s.SubjectId == subjectId)
            .ToListAsync(cancellationToken);
    }

    public async Task<IReadOnlyList<string>> GetClientIdsBySubjectAsync(
        string subjectId,
        CancellationToken cancellationToken = default)
    {
        // This would require parsing session data to extract client IDs
        // For a proper implementation, consider adding a ClientId column to ServerSideSession
        // or querying PersistedGrants instead
        var grants = await _context.PersistedGrants
            .Where(g => g.SubjectId == subjectId)
            .Select(g => g.ClientId)
            .Distinct()
            .ToListAsync(cancellationToken);

        return grants;
    }

    public async Task<int> RemoveExpiredSessionsAsync(CancellationToken cancellationToken = default)
    {
        var now = DateTime.UtcNow;
        var expiredSessions = await _context.ServerSideSessions
            .Where(s => s.Expires != null && s.Expires < now)
            .ToListAsync(cancellationToken);

        if (expiredSessions.Count > 0)
        {
            _context.ServerSideSessions.RemoveRange(expiredSessions);
            await _context.SaveChangesAsync(cancellationToken);
        }

        return expiredSessions.Count;
    }

    public async Task<int> GetSessionCountAsync(
        string subjectId,
        CancellationToken cancellationToken = default)
    {
        return await _context.ServerSideSessions
            .CountAsync(s => s.SubjectId == subjectId, cancellationToken);
    }

    public async Task<(IReadOnlyList<ServerSideSession> Sessions, int TotalCount)> GetAllSessionsAsync(
        int skip = 0,
        int take = 20,
        CancellationToken cancellationToken = default)
    {
        var totalCount = await _context.ServerSideSessions.CountAsync(cancellationToken);

        var sessions = await _context.ServerSideSessions
            .OrderByDescending(s => s.Renewed)
            .Skip(skip)
            .Take(take)
            .ToListAsync(cancellationToken);

        return (sessions, totalCount);
    }
}
