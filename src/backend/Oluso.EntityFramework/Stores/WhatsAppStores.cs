using Microsoft.EntityFrameworkCore;
using Oluso.Core.Domain.Entities;
using Oluso.Core.Domain.Interfaces;

namespace Oluso.EntityFramework.Stores;

public class WhatsAppContactStore : IWhatsAppContactStore
{
    private readonly IOlusoDbContext _context;

    public WhatsAppContactStore(IOlusoDbContext context) => _context = context;

    public Task<WhatsAppContact?> GetByPhoneAsync(string tenantId, string phoneNumber, CancellationToken cancellationToken = default) =>
        _context.WhatsAppContacts
            .IgnoreQueryFilters()
            .FirstOrDefaultAsync(c => c.TenantId == tenantId && c.PhoneNumber == phoneNumber, cancellationToken);

    public Task<WhatsAppContact?> GetByIdAsync(string tenantId, string id, CancellationToken cancellationToken = default) =>
        _context.WhatsAppContacts
            .IgnoreQueryFilters()
            .FirstOrDefaultAsync(c => c.TenantId == tenantId && c.Id == id, cancellationToken);

    public async Task<(IReadOnlyList<WhatsAppContact> Items, int TotalCount)> SearchAsync(
        string tenantId, string? search, WhatsAppConsentStatus? consent, int skip, int take, CancellationToken cancellationToken = default)
    {
        var query = _context.WhatsAppContacts.IgnoreQueryFilters().Where(c => c.TenantId == tenantId);
        if (!string.IsNullOrWhiteSpace(search))
        {
            var term = search.Trim();
            query = query.Where(c => c.PhoneNumber.Contains(term) || (c.DisplayName != null && c.DisplayName.Contains(term)));
        }
        if (consent.HasValue)
            query = query.Where(c => c.ConsentStatus == consent.Value);

        var total = await query.CountAsync(cancellationToken);
        var items = await query
            .OrderByDescending(c => c.LastInboundAt ?? c.LastOutboundAt ?? c.CreatedAt)
            .Skip(Math.Max(0, skip))
            .Take(Math.Clamp(take, 1, 200))
            .ToListAsync(cancellationToken);
        return (items, total);
    }

    public async Task<WhatsAppContact> UpsertAsync(WhatsAppContact contact, CancellationToken cancellationToken = default)
    {
        var existing = await _context.WhatsAppContacts
            .IgnoreQueryFilters()
            .FirstOrDefaultAsync(c => c.TenantId == contact.TenantId && c.PhoneNumber == contact.PhoneNumber, cancellationToken);

        if (existing == null)
        {
            contact.CreatedAt = DateTime.UtcNow;
            _context.WhatsAppContacts.Add(contact);
            await _context.SaveChangesAsync(cancellationToken);
            return contact;
        }

        existing.DisplayName = contact.DisplayName ?? existing.DisplayName;
        existing.ConsentStatus = contact.ConsentStatus;
        existing.ConsentSource = contact.ConsentSource;
        existing.ConsentAt = contact.ConsentAt;
        existing.OptedOutAt = contact.OptedOutAt;
        existing.LastInboundAt = contact.LastInboundAt;
        existing.LastInboundMessageId = contact.LastInboundMessageId;
        existing.LastOutboundAt = contact.LastOutboundAt;
        existing.UpdatedAt = DateTime.UtcNow;
        await _context.SaveChangesAsync(cancellationToken);
        return existing;
    }
}

public class WhatsAppTemplateStore : IWhatsAppTemplateStore
{
    private readonly IOlusoDbContext _context;

    public WhatsAppTemplateStore(IOlusoDbContext context) => _context = context;

    public async Task<IReadOnlyList<WhatsAppTemplate>> ListAsync(string tenantId, bool approvedOnly, CancellationToken cancellationToken = default)
    {
        var query = _context.WhatsAppTemplates.IgnoreQueryFilters().Where(t => t.TenantId == tenantId);
        if (approvedOnly)
            query = query.Where(t => t.Status == "APPROVED");
        return await query.OrderBy(t => t.Name).ThenBy(t => t.Language).ToListAsync(cancellationToken);
    }

    public Task<WhatsAppTemplate?> GetAsync(string tenantId, string name, string language, CancellationToken cancellationToken = default) =>
        _context.WhatsAppTemplates
            .IgnoreQueryFilters()
            .FirstOrDefaultAsync(t => t.TenantId == tenantId && t.Name == name && t.Language == language, cancellationToken);

    public async Task ReplaceAllAsync(string tenantId, IReadOnlyList<WhatsAppTemplate> templates, CancellationToken cancellationToken = default)
    {
        var existing = await _context.WhatsAppTemplates
            .IgnoreQueryFilters()
            .Where(t => t.TenantId == tenantId)
            .ToListAsync(cancellationToken);
        var byKey = existing.ToDictionary(t => (t.Name, t.Language));
        var seen = new HashSet<(string, string)>();

        foreach (var incoming in templates)
        {
            var key = (incoming.Name, incoming.Language);
            seen.Add(key);
            if (byKey.TryGetValue(key, out var row))
            {
                row.ProviderTemplateId = incoming.ProviderTemplateId;
                row.Category = incoming.Category;
                row.Status = incoming.Status;
                row.BodyText = incoming.BodyText;
                row.BodyParameterCount = incoming.BodyParameterCount;
                row.HeaderFormat = incoming.HeaderFormat;
                row.ComponentsJson = incoming.ComponentsJson;
                row.SyncedAt = DateTime.UtcNow;
            }
            else
            {
                incoming.TenantId = tenantId;
                incoming.SyncedAt = DateTime.UtcNow;
                _context.WhatsAppTemplates.Add(incoming);
            }
        }

        foreach (var stale in existing.Where(t => !seen.Contains((t.Name, t.Language))))
            _context.WhatsAppTemplates.Remove(stale);

        await _context.SaveChangesAsync(cancellationToken);
    }
}

public class WhatsAppInboundMessageStore : IWhatsAppInboundMessageStore
{
    private readonly IOlusoDbContext _context;

    public WhatsAppInboundMessageStore(IOlusoDbContext context) => _context = context;

    public Task<bool> ExistsAsync(string tenantId, string providerMessageId, CancellationToken cancellationToken = default) =>
        _context.WhatsAppInboundMessages
            .IgnoreQueryFilters()
            .AnyAsync(m => m.TenantId == tenantId && m.ProviderMessageId == providerMessageId, cancellationToken);

    public async Task AddAsync(WhatsAppInboundMessage message, CancellationToken cancellationToken = default)
    {
        _context.WhatsAppInboundMessages.Add(message);
        await _context.SaveChangesAsync(cancellationToken);
    }

    public async Task<IReadOnlyList<WhatsAppInboundMessage>> ListForContactAsync(string tenantId, string contactId, int take, CancellationToken cancellationToken = default) =>
        await _context.WhatsAppInboundMessages
            .IgnoreQueryFilters()
            .Where(m => m.TenantId == tenantId && m.ContactId == contactId)
            .OrderByDescending(m => m.ReceivedAt)
            .Take(Math.Clamp(take, 1, 500))
            .ToListAsync(cancellationToken);
}

public class WhatsAppAccountStore : IWhatsAppAccountStore
{
    private readonly IOlusoDbContext _context;

    public WhatsAppAccountStore(IOlusoDbContext context) => _context = context;

    public Task<WhatsAppAccount?> GetByPhoneNumberIdAsync(string phoneNumberId, CancellationToken cancellationToken = default) =>
        _context.WhatsAppAccounts
            .IgnoreQueryFilters()
            .FirstOrDefaultAsync(a => a.PhoneNumberId == phoneNumberId, cancellationToken);

    public Task<WhatsAppAccount?> GetForTenantAsync(string tenantId, CancellationToken cancellationToken = default) =>
        _context.WhatsAppAccounts
            .IgnoreQueryFilters()
            .FirstOrDefaultAsync(a => a.TenantId == tenantId, cancellationToken);

    public async Task<WhatsAppAccount> UpsertAsync(WhatsAppAccount account, CancellationToken cancellationToken = default)
    {
        var existing = await _context.WhatsAppAccounts
            .IgnoreQueryFilters()
            .FirstOrDefaultAsync(a => a.TenantId == account.TenantId, cancellationToken);

        // A phone number id belongs to exactly one tenant; refuse to steal it.
        var owner = await _context.WhatsAppAccounts
            .IgnoreQueryFilters()
            .FirstOrDefaultAsync(a => a.PhoneNumberId == account.PhoneNumberId && a.TenantId != account.TenantId, cancellationToken);
        if (owner != null)
            throw new InvalidOperationException($"WhatsApp phone number id '{account.PhoneNumberId}' is already registered to another tenant.");

        if (existing == null)
        {
            account.CreatedAt = DateTime.UtcNow;
            _context.WhatsAppAccounts.Add(account);
            await _context.SaveChangesAsync(cancellationToken);
            return account;
        }

        existing.PhoneNumberId = account.PhoneNumberId;
        existing.BusinessAccountId = account.BusinessAccountId ?? existing.BusinessAccountId;
        existing.DisplayPhoneNumber = account.DisplayPhoneNumber ?? existing.DisplayPhoneNumber;
        existing.VerifiedName = account.VerifiedName ?? existing.VerifiedName;
        existing.QualityRating = account.QualityRating ?? existing.QualityRating;
        existing.LastVerifiedAt = account.LastVerifiedAt ?? existing.LastVerifiedAt;
        existing.UpdatedAt = DateTime.UtcNow;
        await _context.SaveChangesAsync(cancellationToken);
        return existing;
    }

    public async Task RemoveForTenantAsync(string tenantId, CancellationToken cancellationToken = default)
    {
        var rows = await _context.WhatsAppAccounts.IgnoreQueryFilters().Where(a => a.TenantId == tenantId).ToListAsync(cancellationToken);
        if (rows.Count == 0) return;
        foreach (var row in rows) _context.WhatsAppAccounts.Remove(row);
        await _context.SaveChangesAsync(cancellationToken);
    }
}
