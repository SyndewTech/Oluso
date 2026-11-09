using System.Collections.Concurrent;
using Microsoft.Extensions.Logging;
using Microsoft.IdentityModel.Tokens;
using Oluso.Core;
using Oluso.Core.Domain.Entities;
using Oluso.Core.Domain.Interfaces;
using Oluso.Core.Services;

namespace Oluso.Keys;

/// <summary>
/// Production signing credential store that bridges to ISigningKeyService.
/// Uses the full key management system with tenant support, persistence, and Key Vault.
/// Auto-provisions a default signing key if none exists.
///
/// Signing credentials and validation keys are cached per tenant for a short
/// TTL and invalidated explicitly on rotation, revocation, or auto-provisioning.
/// The TTL bounds how long another instance keeps signing with a key that was
/// rotated or expired elsewhere; a token carrying an unknown key id forces one
/// immediate refresh so cross-instance rotation never rejects valid tokens.
/// </summary>
public class SigningCredentialStore : ISigningCredentialStore
{
    /// <summary>How long a cached entry is trusted before it is re-read from the store.</summary>
    public static readonly TimeSpan CacheTtl = TimeSpan.FromMinutes(5);

    private readonly ISigningKeyService _keyService;
    private readonly ISigningKeyStore _keyStore;
    private readonly ITenantContext _tenantContext;
    private readonly IKeyMaterialProviderRegistry _providerRegistry;
    private readonly ILogger<SigningCredentialStore> _logger;

    private sealed record CacheEntry<T>(T Value, DateTime CachedAtUtc)
    {
        public bool IsFresh => DateTime.UtcNow - CachedAtUtc < CacheTtl;
    }

    /// <summary>
    /// Static caches shared across all scoped instances, keyed by tenant ID.
    /// </summary>
    private static readonly ConcurrentDictionary<string, CacheEntry<SigningCredentials>> _signingCache = new();
    private static readonly ConcurrentDictionary<string, CacheEntry<IReadOnlyList<SecurityKeyInfo>>> _validationCache = new();

    public SigningCredentialStore(
        ISigningKeyService keyService,
        ISigningKeyStore keyStore,
        ITenantContext tenantContext,
        IKeyMaterialProviderRegistry providerRegistry,
        ILogger<SigningCredentialStore> logger)
    {
        _keyService = keyService;
        _keyStore = keyStore;
        _tenantContext = tenantContext;
        _providerRegistry = providerRegistry;
        _logger = logger;
    }

    /// <summary>
    /// Invalidate cached signing credentials and validation keys for a tenant.
    /// Called by <see cref="SigningKeyService"/> on key rotation, expiry, or revocation.
    /// Pass null to clear all tenants.
    /// </summary>
    public static void InvalidateCache(string? tenantId = null)
    {
        if (tenantId == null)
        {
            _signingCache.Clear();
            _validationCache.Clear();
        }
        else
        {
            _signingCache.TryRemove(tenantId, out _);
            _validationCache.TryRemove(tenantId, out _);
        }
    }

    public Task<SigningCredentials?> GetSigningCredentialsAsync(CancellationToken cancellationToken = default) =>
        GetSigningCredentialsAsync(_tenantContext.TenantId ?? PlatformConstants.TenantId, cancellationToken);

    public async Task<SigningCredentials?> GetSigningCredentialsAsync(string tenantId, CancellationToken cancellationToken)
    {
        if (_signingCache.TryGetValue(tenantId, out var cached) && cached.IsFresh)
        {
            return cached.Value;
        }

        var credentials = await _keyService.GetSigningCredentialsForTenantAsync(tenantId, clientId: null, cancellationToken);
        if (credentials != null)
        {
            _signingCache[tenantId] = new CacheEntry<SigningCredentials>(credentials, DateTime.UtcNow);
            return credentials;
        }

        // No active key. Only the request's own tenant can be given one here: a request routed to a tenant
        // writes only that tenant's rows (the platform's key comes from EnsurePlatformSigningKeyAsync).
        if (tenantId != (_tenantContext.TenantId ?? PlatformConstants.TenantId))
        {
            _logger.LogError("No active signing key for tenant {TenantId}", tenantId);
            return null;
        }

        var effectiveTenantId = tenantId;
        _logger.LogInformation("No active signing key found for tenant {TenantId}, auto-provisioning",
            effectiveTenantId);

        var defaultProvider = _providerRegistry.GetDefaultProvider();
        var newKey = await _keyService.GenerateKeyAsync(new GenerateKeyRequest
        {
            TenantId = effectiveTenantId,
            Name = "Auto-provisioned signing key",
            KeyType = SigningKeyType.RSA,
            Algorithm = "RS256",
            KeySize = 2048,
            LifetimeDays = 90,
            ActivateImmediately = true,
            StorageProvider = defaultProvider.ProviderType
        }, cancellationToken);

        _logger.LogInformation("Auto-provisioned signing key {KeyId} for tenant {TenantId} using {Provider}",
            newKey.KeyId, effectiveTenantId, defaultProvider.ProviderType);

        InvalidateCache(tenantId);
        credentials = await _keyService.GetSigningCredentialsForTenantAsync(tenantId, clientId: null, cancellationToken);
        if (credentials != null)
        {
            _signingCache[tenantId] = new CacheEntry<SigningCredentials>(credentials, DateTime.UtcNow);
        }
        return credentials;
    }

    public Task<IEnumerable<SecurityKeyInfo>> GetValidationKeysAsync(CancellationToken cancellationToken = default)
        => GetValidationKeysAsync(tenantId: null, cancellationToken);

    public Task<IEnumerable<SecurityKeyInfo>> GetValidationKeysAsync(string? tenantId, CancellationToken cancellationToken = default)
        => GetValidationKeysAsync(tenantId, requiredKeyId: null, cancellationToken);

    public async Task<IEnumerable<SecurityKeyInfo>> GetValidationKeysAsync(
        string? tenantId,
        string? requiredKeyId,
        CancellationToken cancellationToken = default)
    {
        var effectiveTenantId = !string.IsNullOrEmpty(tenantId)
            ? tenantId
            : (_tenantContext.TenantId ?? PlatformConstants.TenantId);

        if (_validationCache.TryGetValue(effectiveTenantId, out var cached) && cached.IsFresh)
        {
            if (requiredKeyId == null || cached.Value.Any(k => string.Equals(k.Key.KeyId, requiredKeyId, StringComparison.Ordinal)))
            {
                return cached.Value;
            }

            _logger.LogInformation(
                "Token key id {KeyId} is not in the cached validation keys for tenant {TenantId}; refreshing",
                requiredKeyId, effectiveTenantId);
        }

        var result = await LoadValidationKeysAsync(effectiveTenantId, cancellationToken);
        _validationCache[effectiveTenantId] = new CacheEntry<IReadOnlyList<SecurityKeyInfo>>(result, DateTime.UtcNow);
        return result;
    }

    private async Task<IReadOnlyList<SecurityKeyInfo>> LoadValidationKeysAsync(string effectiveTenantId, CancellationToken cancellationToken)
    {
        // Validation must find keys for current tenant + platform — platform clients
        // sign tokens under _platform, but API requests resolve to the user's tenant.
        // Keys that expired inside the tenant's grace period stay valid for
        // verification so tokens signed shortly before expiry still validate.
        var config = await _keyStore.GetRotationConfigAsync(effectiveTenantId, null, cancellationToken);
        var graceDays = config?.GracePeriodDays ?? new KeyRotationConfig().GracePeriodDays;
        var grace = TimeSpan.FromDays(Math.Max(1, graceDays));

        var validationKeys = await _keyStore.GetValidationKeysForTenantAndPlatformAsync(effectiveTenantId, grace, cancellationToken);

        var allKeys = new List<SecurityKey>();
        foreach (var key in validationKeys)
        {
            var provider = _providerRegistry.GetProvider(key.StorageProvider);
            if (provider != null)
            {
                var publicKey = await provider.GetPublicKeyAsync(key, cancellationToken);
                if (publicKey != null)
                    allKeys.Add(publicKey);
            }
        }

        return allKeys.Select(k => new SecurityKeyInfo
        {
            Key = k,
            SigningAlgorithm = GetAlgorithmForKey(k)
        }).ToList().AsReadOnly();
    }

    private static string GetAlgorithmForKey(SecurityKey key) => key switch
    {
        RsaSecurityKey => SecurityAlgorithms.RsaSha256,
        ECDsaSecurityKey ec => GetEcAlgorithm(ec),
        SymmetricSecurityKey => SecurityAlgorithms.HmacSha256,
        _ => SecurityAlgorithms.RsaSha256
    };

    private static string GetEcAlgorithm(ECDsaSecurityKey key)
    {
        var keySize = key.KeySize;
        return keySize switch
        {
            256 => SecurityAlgorithms.EcdsaSha256,
            384 => SecurityAlgorithms.EcdsaSha384,
            521 => SecurityAlgorithms.EcdsaSha512,
            _ => SecurityAlgorithms.EcdsaSha256
        };
    }
}
