using Microsoft.EntityFrameworkCore;
using Oluso.Core.Domain.Entities;
using Oluso.Core.Domain.Interfaces;

namespace Oluso.EntityFramework.Stores;

/// <summary>
/// Entity Framework implementation of ISigningKeyStore
/// </summary>
public class SigningKeyStore : ISigningKeyStore
{
    private readonly IOlusoDbContext _context;

    public SigningKeyStore(IOlusoDbContext context)
    {
        _context = context;
    }

    public async Task<SigningKey?> GetByIdAsync(string id, CancellationToken cancellationToken = default)
    {
        return await _context.SigningKeys
            .FirstOrDefaultAsync(k => k.Id == id, cancellationToken);
    }

    public async Task StoreAsync(SigningKey key, CancellationToken cancellationToken = default)
    {
        _context.SigningKeys.Add(key);
        await _context.SaveChangesAsync(cancellationToken);
    }

    public async Task UpdateAsync(SigningKey key, CancellationToken cancellationToken = default)
    {
        await _context.SaveChangesAsync(cancellationToken);
    }

    public async Task DeleteAsync(string id, CancellationToken cancellationToken = default)
    {
        var key = await _context.SigningKeys
            .FirstOrDefaultAsync(k => k.Id == id, cancellationToken);

        if (key != null)
        {
            _context.SigningKeys.Remove(key);
            await _context.SaveChangesAsync(cancellationToken);
        }
    }

    public async Task<SigningKey?> GetActiveSigningKeyAsync(
        string? tenantId,
        string? clientId = null,
        CancellationToken cancellationToken = default)
    {
        var query = _context.SigningKeys
            .Where(k => k.Status == SigningKeyStatus.Active &&
                        k.CanSign);

        // Prefer client-specific, then tenant-specific, then global
        if (!string.IsNullOrEmpty(clientId))
        {
            var clientKey = await query
                .FirstOrDefaultAsync(k => k.ClientId == clientId && k.TenantId == tenantId, cancellationToken);
            if (clientKey != null)
                return clientKey;
        }

        if (!string.IsNullOrEmpty(tenantId))
        {
            var tenantKey = await query
                .FirstOrDefaultAsync(k => k.TenantId == tenantId && k.ClientId == null, cancellationToken);
            if (tenantKey != null)
                return tenantKey;
        }

        // Global key
        return await query
            .FirstOrDefaultAsync(k => k.TenantId == null && k.ClientId == null, cancellationToken);
    }

    public async Task<IReadOnlyList<SigningKey>> GetByTenantAsync(
        string? tenantId,
        CancellationToken cancellationToken = default)
    {
        return await _context.SigningKeys
            .Where(k => k.TenantId == tenantId)
            .ToListAsync(cancellationToken);
    }

    public async Task<IReadOnlyList<SigningKey>> GetByClientAsync(
        string? tenantId,
        string clientId,
        CancellationToken cancellationToken = default)
    {
        return await _context.SigningKeys
            .Where(k => k.TenantId == tenantId && k.ClientId == clientId)
            .ToListAsync(cancellationToken);
    }

    public async Task<IReadOnlyList<SigningKey>> GetJwksKeysAsync(
        string? tenantId,
        CancellationToken cancellationToken = default)
    {
        return await _context.SigningKeys
            .Where(k => k.IncludeInJwks &&
                        (k.TenantId == tenantId || k.TenantId == null) &&
                        k.CanVerify)
            .OrderByDescending(k => k.CreatedAt)
            .ToListAsync(cancellationToken);
    }

    public async Task<IReadOnlyList<SigningKey>> GetExpiringKeysAsync(
        int daysUntilExpiration,
        CancellationToken cancellationToken = default)
    {
        var cutoff = DateTime.UtcNow.AddDays(daysUntilExpiration);
        return await _context.SigningKeys
            .Where(k => k.Status == SigningKeyStatus.Active &&
                        k.ExpiresAt != null &&
                        k.ExpiresAt <= cutoff)
            .ToListAsync(cancellationToken);
    }

    public async Task IncrementUsageAsync(string id, CancellationToken cancellationToken = default)
    {
        var key = await _context.SigningKeys
            .FirstOrDefaultAsync(k => k.Id == id, cancellationToken);

        if (key != null)
        {
            key.SignatureCount++;
            key.LastUsedAt = DateTime.UtcNow;
            await _context.SaveChangesAsync(cancellationToken);
        }
    }

    public async Task<KeyRotationConfig?> GetRotationConfigAsync(
        string? tenantId,
        string? clientId = null,
        CancellationToken cancellationToken = default)
    {
        // For now, return a default config - could be stored in database
        await Task.CompletedTask;
        return new KeyRotationConfig
        {
            TenantId = tenantId,
            ClientId = clientId,
            Algorithm = "RS256",
            KeySize = 2048,
            KeyLifetimeDays = 90,
            RotationLeadDays = 14
        };
    }

    public async Task SaveRotationConfigAsync(
        KeyRotationConfig config,
        CancellationToken cancellationToken = default)
    {
        // Could store in a separate table or as metadata
        await Task.CompletedTask;
    }
}
