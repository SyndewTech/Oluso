using Microsoft.EntityFrameworkCore;
using Oluso.Core;
using Oluso.Core.Domain.Entities;
using Oluso.Core.Domain.Interfaces;
using Oluso.Core.Storage;

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
        var now = DateTime.UtcNow;
        var query = Active(_context.SigningKeys, now);

        // The named tenant's own keys, whatever tenant the request was routed to: the caller chose the tenant.
        var namedTenant = Active(_context.SigningKeys.IgnoreQueryFilters(), now);

        // Prefer client-specific, then tenant-specific, then global
        if (!string.IsNullOrEmpty(clientId))
        {
            var clientKey = await namedTenant
                .FirstOrDefaultAsync(k => k.ClientId == clientId && k.TenantId == tenantId, cancellationToken);
            if (clientKey != null)
                return clientKey;
        }

        if (!string.IsNullOrEmpty(tenantId))
        {
            var tenantKey = await namedTenant
                .FirstOrDefaultAsync(k => k.TenantId == tenantId && k.ClientId == null, cancellationToken);
            if (tenantKey != null)
                return tenantKey;
        }

        // Fall back to _platform key (shared platform signing key)
        if (tenantId != PlatformConstants.TenantId)
        {
            var platformKey = await query
                .FirstOrDefaultAsync(k => k.TenantId == PlatformConstants.TenantId && k.ClientId == null, cancellationToken);
            if (platformKey != null)
                return platformKey;
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

    public Task<IReadOnlyList<SigningKey>> GetJwksKeysAsync(
        string? tenantId,
        CancellationToken cancellationToken = default)
        => GetJwksKeysAsync(tenantId, TimeSpan.Zero, cancellationToken);

    public async Task<IReadOnlyList<SigningKey>> GetJwksKeysAsync(
        string? tenantId,
        TimeSpan verificationGrace,
        CancellationToken cancellationToken = default)
    {
        var cutoff = DateTime.UtcNow - verificationGrace;
        return await _context.SigningKeys
            .Where(k => k.IncludeInJwks &&
                        // Data-protection keys protect stored values; they never sign or verify tokens.
                        (k.ClientId == null || !k.ClientId.StartsWith(DataProtectionKeys.ClientIdPrefix)) &&
                        (k.TenantId == tenantId || k.TenantId == null) &&
                        k.Status != SigningKeyStatus.Revoked &&
                        (k.ExpiresAt == null || k.ExpiresAt > cutoff))
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
        var config = await _context.KeyRotationConfigs
            .FirstOrDefaultAsync(k => k.TenantId == tenantId && k.ClientId == clientId, cancellationToken);

        if (config != null)
            return config;

        // Return defaults if no config exists yet
        return new KeyRotationConfig
        {
            TenantId = tenantId,
            ClientId = clientId
        };
    }

    public async Task SaveRotationConfigAsync(
        KeyRotationConfig config,
        CancellationToken cancellationToken = default)
    {
        var existing = await _context.KeyRotationConfigs
            .FirstOrDefaultAsync(k => k.TenantId == config.TenantId && k.ClientId == config.ClientId, cancellationToken);

        if (existing != null)
        {
            existing.Enabled = config.Enabled;
            existing.KeyType = config.KeyType;
            existing.Algorithm = config.Algorithm;
            existing.KeySize = config.KeySize;
            existing.KeyLifetimeDays = config.KeyLifetimeDays;
            existing.RotationLeadDays = config.RotationLeadDays;
            existing.GracePeriodDays = config.GracePeriodDays;
            existing.MaxKeys = config.MaxKeys;
            existing.LastRotationAt = config.LastRotationAt;
            existing.NextRotationAt = config.NextRotationAt;
            existing.PreferredStorageProvider = config.PreferredStorageProvider;
        }
        else
        {
            _context.KeyRotationConfigs.Add(config);
        }

        await _context.SaveChangesAsync(cancellationToken);
    }

    private static IOrderedQueryable<SigningKey> Active(IQueryable<SigningKey> keys, DateTime now) =>
        keys.Where(k => k.Status == SigningKeyStatus.Active &&
                        (k.ActivatedAt == null || k.ActivatedAt <= now) &&
                        (k.ExpiresAt == null || k.ExpiresAt > now))
            .OrderByDescending(k => k.Priority)
            .ThenByDescending(k => k.CreatedAt);

    public Task<IReadOnlyList<SigningKey>> GetValidationKeysForTenantAndPlatformAsync(
        string? tenantId,
        CancellationToken cancellationToken = default)
        => GetValidationKeysForTenantAndPlatformAsync(tenantId, TimeSpan.Zero, cancellationToken);

    public async Task<IReadOnlyList<SigningKey>> GetValidationKeysForTenantAndPlatformAsync(
        string? tenantId,
        TimeSpan verificationGrace,
        CancellationToken cancellationToken = default)
    {
        var cutoff = DateTime.UtcNow - verificationGrace;
        return await _context.SigningKeys
            .IgnoreQueryFilters()
            .Where(k => (k.TenantId == tenantId || k.TenantId == PlatformConstants.TenantId)
                && (k.ClientId == null || !k.ClientId.StartsWith(DataProtectionKeys.ClientIdPrefix))
                && (k.Status == SigningKeyStatus.Active || k.Status == SigningKeyStatus.Expired)
                && (k.ExpiresAt == null || k.ExpiresAt > cutoff))
            .ToListAsync(cancellationToken);
    }
}
