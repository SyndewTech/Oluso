using Microsoft.EntityFrameworkCore;
using Oluso.Core.Domain.Entities;
using Oluso.Core.Domain.Interfaces;

namespace Oluso.EntityFramework.Stores;

public class ConversationMessageStore : IConversationMessageStore
{
    private readonly IOlusoDbContext _context;

    public ConversationMessageStore(IOlusoDbContext context)
    {
        _context = context;
    }

    public async Task<IReadOnlyList<ConversationMessage>> ListAsync(
        string tenantId,
        string entityType,
        string entityId,
        CancellationToken cancellationToken = default)
    {
        return await _context.ConversationMessages
            .Where(m => m.TenantId == tenantId
                     && m.EntityType == entityType
                     && m.EntityId == entityId)
            .OrderBy(m => m.CreatedAt)
            .ToListAsync(cancellationToken);
    }

    public async Task<ConversationMessage> CreateAsync(
        ConversationMessage message, CancellationToken cancellationToken = default)
    {
        _context.ConversationMessages.Add(message);
        await _context.SaveChangesAsync(cancellationToken);
        return message;
    }

    public async Task<ConversationMessage?> GetByIdAsync(
        string tenantId, string id, CancellationToken cancellationToken = default)
    {
        return await _context.ConversationMessages
            .FirstOrDefaultAsync(m => m.TenantId == tenantId && m.Id == id, cancellationToken);
    }

    public async Task<ConversationMessage> UpdateAsync(
        ConversationMessage message, CancellationToken cancellationToken = default)
    {
        _context.ConversationMessages.Update(message);
        await _context.SaveChangesAsync(cancellationToken);
        return message;
    }

    public async Task<IReadOnlyList<ConversationMessage>> ListUnreadMentionsForUserAsync(
        string tenantId,
        string userId,
        int limit = 50,
        CancellationToken cancellationToken = default)
    {
        // The mention list is JSON in MentionedUserIdsJson — a LIKE on the
        // quoted user id matches both single-mention and multi-mention rows
        // and is portable across SQL Server/Postgres without per-provider
        // JSON operators. Anti-join with ConversationMentionReads gives us
        // "mentions of me that I haven't seen yet". Soft-deleted messages
        // are excluded — the mention isn't actionable once the body is gone.
        var quotedId = "\"" + userId + "\"";

        var readMessageIds = _context.ConversationMentionReads
            .Where(r => r.TenantId == tenantId && r.UserId == userId)
            .Select(r => r.MessageId);

        return await _context.ConversationMessages
            .Where(m => m.TenantId == tenantId
                     && m.DeletedAt == null
                     && m.MentionedUserIdsJson != null
                     && EF.Functions.Like(m.MentionedUserIdsJson, "%" + quotedId + "%")
                     && !readMessageIds.Contains(m.Id))
            .OrderByDescending(m => m.CreatedAt)
            .Take(limit)
            .ToListAsync(cancellationToken);
    }
}

public class ConversationMentionReadStore : IConversationMentionReadStore
{
    private readonly IOlusoDbContext _context;

    public ConversationMentionReadStore(IOlusoDbContext context)
    {
        _context = context;
    }

    public async Task MarkReadAsync(
        string tenantId,
        string userId,
        IReadOnlyCollection<string> messageIds,
        CancellationToken cancellationToken = default)
    {
        if (messageIds.Count == 0) return;

        // Skip rows that are already acked so the unique index doesn't fire
        // on a normal "re-open the thread" call. Single round-trip — the
        // count is bounded by the page's visible mention messages.
        var existing = await _context.ConversationMentionReads
            .Where(r => r.TenantId == tenantId
                     && r.UserId == userId
                     && messageIds.Contains(r.MessageId))
            .Select(r => r.MessageId)
            .ToListAsync(cancellationToken);
        var existingSet = new HashSet<string>(existing, StringComparer.Ordinal);

        var now = DateTime.UtcNow;
        foreach (var id in messageIds)
        {
            if (existingSet.Contains(id)) continue;
            _context.ConversationMentionReads.Add(new ConversationMentionRead
            {
                TenantId = tenantId,
                MessageId = id,
                UserId = userId,
                ReadAt = now,
            });
        }
        await _context.SaveChangesAsync(cancellationToken);
    }
}
