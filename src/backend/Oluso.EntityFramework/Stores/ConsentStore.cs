using Microsoft.EntityFrameworkCore;
using Oluso.Core.Domain.Entities;
using Oluso.Core.Domain.Interfaces;

namespace Oluso.EntityFramework.Stores;

/// <summary>
/// Entity Framework implementation of IConsentStore
/// </summary>
public class ConsentStore : IConsentStore
{
    private readonly IOlusoDbContext _context;

    public ConsentStore(IOlusoDbContext context)
    {
        _context = context;
    }

    public async Task StoreConsentAsync(Consent consent, CancellationToken cancellationToken = default)
    {
        var existing = await _context.Consents
            .FirstOrDefaultAsync(c => c.SubjectId == consent.SubjectId && c.ClientId == consent.ClientId,
                cancellationToken);

        if (existing != null)
        {
            existing.Scopes = consent.Scopes;
            existing.CreatedAt = consent.CreatedAt;
            existing.ExpiresAt = consent.ExpiresAt;
        }
        else
        {
            _context.Consents.Add(consent);
        }

        await _context.SaveChangesAsync(cancellationToken);
    }

    public async Task<Consent?> GetConsentAsync(
        string subjectId,
        string clientId,
        CancellationToken cancellationToken = default)
    {
        return await _context.Consents
            .FirstOrDefaultAsync(c => c.SubjectId == subjectId && c.ClientId == clientId,
                cancellationToken);
    }

    public async Task<IEnumerable<Consent>> GetConsentsBySubjectAsync(
        string subjectId,
        CancellationToken cancellationToken = default)
    {
        return await _context.Consents
            .Where(c => c.SubjectId == subjectId)
            .ToListAsync(cancellationToken);
    }

    public async Task RevokeConsentAsync(
        string subjectId,
        string clientId,
        CancellationToken cancellationToken = default)
    {
        var consent = await _context.Consents
            .FirstOrDefaultAsync(c => c.SubjectId == subjectId && c.ClientId == clientId,
                cancellationToken);

        if (consent != null)
        {
            _context.Consents.Remove(consent);
            await _context.SaveChangesAsync(cancellationToken);
        }
    }

    public async Task RevokeAllConsentsAsync(string subjectId, CancellationToken cancellationToken = default)
    {
        var consents = await _context.Consents
            .Where(c => c.SubjectId == subjectId)
            .ToListAsync(cancellationToken);

        _context.Consents.RemoveRange(consents);
        await _context.SaveChangesAsync(cancellationToken);
    }

    public async Task<bool> HasConsentAsync(
        string subjectId,
        string clientId,
        IEnumerable<string> requestedScopes,
        CancellationToken cancellationToken = default)
    {
        var consent = await _context.Consents
            .FirstOrDefaultAsync(c => c.SubjectId == subjectId && c.ClientId == clientId,
                cancellationToken);

        if (consent == null)
            return false;

        // Check if consent has expired
        if (consent.ExpiresAt.HasValue && consent.ExpiresAt.Value < DateTime.UtcNow)
            return false;

        // Check if all requested scopes are in the consented scopes
        var consentedScopes = consent.GetScopes();
        var requestedScopesList = requestedScopes.ToList();

        return requestedScopesList.All(s => consentedScopes.Contains(s));
    }
}
