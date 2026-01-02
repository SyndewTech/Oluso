using System.Collections.Concurrent;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Oluso.Core.Domain.Entities;
using Oluso.Core.Domain.Interfaces;

namespace Oluso.EntityFramework.Stores;

/// <summary>
/// Authorization code store using PersistedGrant for storage
/// </summary>
public class AuthorizationCodeStore : IAuthorizationCodeStore
{
    private readonly IOlusoDbContext _context;
    private readonly ITenantContext _tenantContext;

    // In-memory cache for fast lookups (codes are short-lived)
    private static readonly ConcurrentDictionary<string, AuthorizationCode> _cache = new();

    public AuthorizationCodeStore(
        IOlusoDbContext context,
        ITenantContext tenantContext)
    {
        _context = context;
        _tenantContext = tenantContext;
    }

    public async Task StoreAsync(AuthorizationCode code, CancellationToken cancellationToken = default)
    {
        code.TenantId = _tenantContext.TenantId;

        // Store as persisted grant
        var grant = new PersistedGrant
        {
            Key = code.Code,
            Type = "authorization_code",
            SubjectId = code.SubjectId,
            SessionId = code.SessionId,
            ClientId = code.ClientId,
            CreationTime = code.CreationTime,
            Expiration = code.Expiration,
            Data = JsonSerializer.Serialize(code),
            TenantId = _tenantContext.TenantId
        };

        _context.PersistedGrants.Add(grant);
        await _context.SaveChangesAsync(cancellationToken);

        // Cache it
        _cache[code.Code] = code;
    }

    public async Task<AuthorizationCode?> GetAsync(string code, CancellationToken cancellationToken = default)
    {
        // Check cache first
        if (_cache.TryGetValue(code, out var cached))
        {
            if (cached.Expiration > DateTime.UtcNow && !cached.IsConsumed)
            {
                return cached;
            }
            _cache.TryRemove(code, out _);
        }

        // Check database
        var grant = await _context.PersistedGrants
            .FirstOrDefaultAsync(g => g.Key == code && g.Type == "authorization_code", cancellationToken);

        if (grant == null)
        {
            return null;
        }

        var authCode = JsonSerializer.Deserialize<AuthorizationCode>(grant.Data);
        if (authCode != null)
        {
            _cache[code] = authCode;
        }

        return authCode;
    }

    public async Task RemoveAsync(string code, CancellationToken cancellationToken = default)
    {
        _cache.TryRemove(code, out _);

        var grant = await _context.PersistedGrants
            .FirstOrDefaultAsync(g => g.Key == code && g.Type == "authorization_code", cancellationToken);

        if (grant != null)
        {
            _context.PersistedGrants.Remove(grant);
            await _context.SaveChangesAsync(cancellationToken);
        }
    }
}
