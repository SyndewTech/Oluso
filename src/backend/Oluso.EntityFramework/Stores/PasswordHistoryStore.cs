using Microsoft.EntityFrameworkCore;
using Oluso.Core.Domain.Entities;
using Oluso.Core.Domain.Interfaces;

namespace Oluso.EntityFramework.Stores;

public class PasswordHistoryStore : IPasswordHistoryStore
{
    private readonly IOlusoDbContext _context;

    public PasswordHistoryStore(IOlusoDbContext context)
    {
        _context = context;
    }

    public async Task<IReadOnlyList<string>> GetRecentHashesAsync(string userId, int count, CancellationToken cancellationToken = default)
    {
        if (count <= 0) return Array.Empty<string>();

        return await _context.PasswordHistory
            .Where(e => e.UserId == userId)
            .OrderByDescending(e => e.CreatedAt)
            .Take(count)
            .Select(e => e.PasswordHash)
            .ToListAsync(cancellationToken);
    }

    public async Task RecordAsync(string userId, string passwordHash, int retain, CancellationToken cancellationToken = default)
    {
        var existing = await _context.PasswordHistory
            .Where(e => e.UserId == userId)
            .OrderByDescending(e => e.CreatedAt)
            .ToListAsync(cancellationToken);

        if (retain > 0)
        {
            _context.PasswordHistory.Add(new PasswordHistoryEntry
            {
                UserId = userId,
                PasswordHash = passwordHash,
                CreatedAt = DateTime.UtcNow
            });
        }

        var keepExisting = Math.Max(retain - 1, 0);
        _context.PasswordHistory.RemoveRange(existing.Skip(keepExisting));

        await _context.SaveChangesAsync(cancellationToken);
    }
}
