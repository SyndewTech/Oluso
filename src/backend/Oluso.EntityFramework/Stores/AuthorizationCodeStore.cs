using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Caching.Distributed;
using Microsoft.Extensions.Logging;
using Oluso.Core.Domain.Entities;
using Oluso.Core.Domain.Interfaces;

namespace Oluso.EntityFramework.Stores;

/// <summary>
/// Authorization code store using PersistedGrant for storage, fronted by the
/// distributed cache. The database is authoritative; the cache only spares the
/// redemption lookup and, being shared, keeps one-time use intact across
/// instances (a code consumed on one node disappears for all of them). Swapping
/// the cache for Redis needs no change here.
/// </summary>
public class AuthorizationCodeStore : IAuthorizationCodeStore
{
    private const string CacheKeyPrefix = "oluso:authcode:";

    private readonly IOlusoDbContext _context;
    private readonly ITenantContext _tenantContext;
    private readonly IDistributedCache? _cache;
    private readonly ILogger<AuthorizationCodeStore>? _logger;

    public AuthorizationCodeStore(
        IOlusoDbContext context,
        ITenantContext tenantContext,
        IDistributedCache? cache = null,
        ILogger<AuthorizationCodeStore>? logger = null)
    {
        _context = context;
        _tenantContext = tenantContext;
        _cache = cache;
        _logger = logger;
    }

    public async Task StoreAsync(AuthorizationCode code, CancellationToken cancellationToken = default)
    {
        // Derive tenant from the authorization code's claims (set during the authorize flow
        // by the profile service), not from _tenantContext (URL-based routing context).
        // The authorize endpoint is an OIDC protocol path, not an API path, so the middleware
        // resolves tenant from URL/header without JWT-based access validation.
        var codeTenantId = code.TenantId
            ?? (code.Claims?.TryGetValue("tenant_id", out var tid) == true ? tid : null);
        code.TenantId = codeTenantId!;

        var json = JsonSerializer.Serialize(code);
        var grant = new PersistedGrant
        {
            Key = code.Code,
            Type = "authorization_code",
            SubjectId = code.SubjectId,
            SessionId = code.SessionId,
            ClientId = code.ClientId,
            CreationTime = code.CreationTime,
            Expiration = code.Expiration,
            Data = json,
            TenantId = codeTenantId
        };

        _context.PersistedGrants.Add(grant);
        await _context.SaveChangesAsync(cancellationToken);

        await CacheSetAsync(code.Code, json, code.Expiration, cancellationToken);
    }

    public async Task<AuthorizationCode?> GetAsync(string code, CancellationToken cancellationToken = default)
    {
        var cached = await CacheGetAsync(code, cancellationToken);
        if (cached != null)
        {
            if (cached.Expiration > DateTime.UtcNow && !cached.IsConsumed)
            {
                return cached;
            }
            await CacheRemoveAsync(code, cancellationToken);
        }

        var grant = await _context.PersistedGrants
            .FirstOrDefaultAsync(g => g.Key == code && g.Type == "authorization_code", cancellationToken);

        if (grant == null)
        {
            return null;
        }

        var authCode = JsonSerializer.Deserialize<AuthorizationCode>(grant.Data);
        if (authCode != null)
        {
            await CacheSetAsync(code, grant.Data, authCode.Expiration, cancellationToken);
        }

        return authCode;
    }

    public async Task RemoveAsync(string code, CancellationToken cancellationToken = default)
    {
        // Cache first: once removed here, no instance can redeem the code again.
        await CacheRemoveAsync(code, cancellationToken);

        var grant = await _context.PersistedGrants
            .FirstOrDefaultAsync(g => g.Key == code && g.Type == "authorization_code", cancellationToken);

        if (grant != null)
        {
            _context.PersistedGrants.Remove(grant);
            await _context.SaveChangesAsync(cancellationToken);
        }
    }

    // The cache is an accelerator: a cache outage degrades to database lookups and
    // is logged, it never fails a sign-in.

    private async Task<AuthorizationCode?> CacheGetAsync(string code, CancellationToken cancellationToken)
    {
        if (_cache == null) return null;
        try
        {
            var json = await _cache.GetStringAsync(CacheKeyPrefix + code, cancellationToken);
            return json == null ? null : JsonSerializer.Deserialize<AuthorizationCode>(json);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            _logger?.LogWarning(ex, "Authorization code cache read failed; falling back to the database");
            return null;
        }
    }

    private async Task CacheSetAsync(string code, string json, DateTime expiration, CancellationToken cancellationToken)
    {
        if (_cache == null) return;
        var ttl = expiration - DateTime.UtcNow;
        if (ttl <= TimeSpan.Zero) return;
        try
        {
            await _cache.SetStringAsync(CacheKeyPrefix + code, json,
                new DistributedCacheEntryOptions { AbsoluteExpirationRelativeToNow = ttl }, cancellationToken);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            _logger?.LogWarning(ex, "Authorization code cache write failed; the database copy remains authoritative");
        }
    }

    private async Task CacheRemoveAsync(string code, CancellationToken cancellationToken)
    {
        if (_cache == null) return;
        try
        {
            await _cache.RemoveAsync(CacheKeyPrefix + code, cancellationToken);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            _logger?.LogWarning(ex, "Authorization code cache remove failed; the code expires from the cache on its own");
        }
    }
}
