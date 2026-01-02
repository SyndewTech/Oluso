using Microsoft.EntityFrameworkCore;
using Oluso.Core.Domain.Entities;
using Oluso.Core.Domain.Interfaces;

namespace Oluso.EntityFramework.Stores;

/// <summary>
/// Entity Framework implementation of IFido2CredentialStore
/// </summary>
public class Fido2CredentialStore : IFido2CredentialStore
{
    private readonly IOlusoDbContext _context;

    public Fido2CredentialStore(IOlusoDbContext context)
    {
        _context = context;
    }

    public async Task<Fido2CredentialEntity?> GetByIdAsync(string id, CancellationToken cancellationToken = default)
    {
        return await _context.Fido2Credentials
            .FirstOrDefaultAsync(c => c.Id == id, cancellationToken);
    }

    public async Task<Fido2CredentialEntity?> GetByCredentialIdAsync(string credentialId, CancellationToken cancellationToken = default)
    {
        return await _context.Fido2Credentials
            .FirstOrDefaultAsync(c => c.CredentialId == credentialId, cancellationToken);
    }

    public async Task<IReadOnlyList<Fido2CredentialEntity>> GetByUserIdAsync(string userId, CancellationToken cancellationToken = default)
    {
        return await _context.Fido2Credentials
            .Where(c => c.UserId == userId)
            .OrderByDescending(c => c.CreatedAt)
            .ToListAsync(cancellationToken);
    }

    public async Task<IReadOnlyList<Fido2CredentialEntity>> GetActiveByUserIdAsync(string userId, CancellationToken cancellationToken = default)
    {
        return await _context.Fido2Credentials
            .Where(c => c.UserId == userId && c.IsActive)
            .OrderByDescending(c => c.LastUsedAt ?? c.CreatedAt)
            .ToListAsync(cancellationToken);
    }

    public async Task<bool> ExistsAsync(string credentialId, CancellationToken cancellationToken = default)
    {
        return await _context.Fido2Credentials
            .AnyAsync(c => c.CredentialId == credentialId, cancellationToken);
    }

    public async Task AddAsync(Fido2CredentialEntity credential, CancellationToken cancellationToken = default)
    {
        _context.Fido2Credentials.Add(credential);
        await _context.SaveChangesAsync(cancellationToken);
    }

    public async Task UpdateAsync(Fido2CredentialEntity credential, CancellationToken cancellationToken = default)
    {
        await _context.SaveChangesAsync(cancellationToken);
    }

    public async Task RemoveAsync(string id, CancellationToken cancellationToken = default)
    {
        var credential = await _context.Fido2Credentials
            .FirstOrDefaultAsync(c => c.Id == id, cancellationToken);

        if (credential != null)
        {
            _context.Fido2Credentials.Remove(credential);
            await _context.SaveChangesAsync(cancellationToken);
        }
    }

    public async Task UpdateCounterAsync(string credentialId, uint newCounter, CancellationToken cancellationToken = default)
    {
        var credential = await _context.Fido2Credentials
            .FirstOrDefaultAsync(c => c.CredentialId == credentialId, cancellationToken);

        if (credential != null)
        {
            credential.SignatureCounter = newCounter;
            credential.LastUsedAt = DateTime.UtcNow;
            await _context.SaveChangesAsync(cancellationToken);
        }
    }
}
