using Microsoft.Extensions.Logging;
using Microsoft.IdentityModel.Tokens;
using Oluso.Core.Domain.Entities;
using Oluso.Core.Domain.Interfaces;
using Oluso.Core.Services;

namespace Oluso.Keys;

/// <summary>
/// Service for managing signing keys including generation, rotation, and JWKS.
/// Uses pluggable key material providers (local, Azure Key Vault, AWS KMS, etc.)
/// Private keys never leave the provider - they are NEVER exposed in API responses.
/// </summary>
public class SigningKeyService : ISigningKeyService
{
    private readonly ISigningKeyStore _keyStore;
    private readonly ITenantContext _tenantContext;
    private readonly IKeyMaterialProviderRegistry _providerRegistry;
    private readonly ILogger<SigningKeyService> _logger;

    public SigningKeyService(
        ISigningKeyStore keyStore,
        ITenantContext tenantContext,
        IKeyMaterialProviderRegistry providerRegistry,
        ILogger<SigningKeyService> logger)
    {
        _keyStore = keyStore;
        _tenantContext = tenantContext;
        _providerRegistry = providerRegistry;
        _logger = logger;
    }

    /// <summary>
    /// Generate a new signing key using the configured provider
    /// </summary>
    public async Task<SigningKey> GenerateKeyAsync(
        GenerateKeyRequest request,
        CancellationToken cancellationToken = default)
    {
        var tenantId = request.TenantId ?? _tenantContext.TenantId;

        // Get the appropriate provider
        var storageProvider = request.StorageProvider ?? KeyStorageProvider.Local;
        var provider = _providerRegistry.GetProvider(storageProvider)
            ?? _providerRegistry.GetDefaultProvider();

        _logger.LogInformation(
            "Generating new {KeyType} key for tenant {TenantId}, client {ClientId} using {Provider}",
            request.KeyType, tenantId, request.ClientId, provider.ProviderType);

        // Generate key using the provider (private key stays in provider)
        var keyResult = await provider.GenerateKeyAsync(new KeyGenerationParams
        {
            TenantId = tenantId,
            ClientId = request.ClientId,
            Name = request.Name,
            KeyType = request.KeyType,
            Algorithm = request.Algorithm ?? GetDefaultAlgorithm(request.KeyType),
            KeySize = request.KeySize ?? GetDefaultKeySize(request.KeyType),
            Use = request.Use ?? SigningKeyUse.Signing,
            LifetimeDays = request.LifetimeDays,
            ExpiresAt = request.ExpiresAt
        }, cancellationToken);

        // Create the signing key metadata (no private key material!)
        var signingKey = new SigningKey
        {
            Id = Guid.NewGuid().ToString("N"),
            Name = request.Name ?? $"{request.KeyType}-{DateTime.UtcNow:yyyyMMdd}",
            KeyId = keyResult.KeyId,
            KeyType = request.KeyType,
            Algorithm = request.Algorithm ?? GetDefaultAlgorithm(request.KeyType),
            Use = request.Use ?? SigningKeyUse.Signing,
            KeySize = request.KeySize ?? GetDefaultKeySize(request.KeyType),
            // For Key Vault: PrivateKeyData is empty, KeyVaultUri contains the reference
            // For Local: PrivateKeyData contains encrypted key
            PrivateKeyData = keyResult.EncryptedPrivateKey ?? string.Empty,
            PublicKeyData = keyResult.PublicKeyData,
            KeyVaultUri = keyResult.KeyVaultUri,
            StorageProvider = provider.ProviderType,
            TenantId = tenantId,
            ClientId = request.ClientId,
            Status = request.ActivateImmediately ? SigningKeyStatus.Active : SigningKeyStatus.Pending,
            CreatedAt = DateTime.UtcNow,
            ActivatedAt = request.ActivateImmediately ? DateTime.UtcNow : request.ActivateAt,
            ExpiresAt = request.ExpiresAt ?? DateTime.UtcNow.AddDays(request.LifetimeDays ?? 90),
            Priority = request.Priority ?? 100,
            IncludeInJwks = true,
            X5t = keyResult.X5t,
            X5c = keyResult.X5c
        };

        await _keyStore.StoreAsync(signingKey, cancellationToken);

        _logger.LogInformation(
            "Generated key {KeyId} for tenant {TenantId} using {Provider}",
            signingKey.KeyId, tenantId, provider.ProviderType);

        return signingKey;
    }

    /// <summary>
    /// Get signing credentials for token generation (private key stays in provider)
    /// </summary>
    public async Task<SigningCredentials?> GetSigningCredentialsAsync(
        string? clientId = null,
        CancellationToken cancellationToken = default)
    {
        var tenantId = _tenantContext.TenantId;
        var key = await _keyStore.GetActiveSigningKeyAsync(tenantId, clientId, cancellationToken);

        if (key == null)
        {
            _logger.LogWarning("No active signing key found for tenant {TenantId}, client {ClientId}", tenantId, clientId);
            return null;
        }

        // Get the appropriate provider for this key
        var provider = _providerRegistry.GetProvider(key.StorageProvider)
            ?? _providerRegistry.GetDefaultProvider();

        // Update usage statistics
        await _keyStore.IncrementUsageAsync(key.Id, cancellationToken);

        // Get signing credentials from the provider (private key never leaves provider)
        return await provider.GetSigningCredentialsAsync(key, cancellationToken);
    }

    /// <summary>
    /// Get validation keys (public only) for token verification
    /// </summary>
    public async Task<IEnumerable<SecurityKey>> GetValidationKeysAsync(
        string? clientId = null,
        CancellationToken cancellationToken = default)
    {
        var tenantId = _tenantContext.TenantId;
        var keys = clientId != null
            ? await _keyStore.GetByClientAsync(tenantId, clientId, cancellationToken)
            : await _keyStore.GetByTenantAsync(tenantId, cancellationToken);

        var validationKeys = new List<SecurityKey>();

        foreach (var key in keys.Where(k => k.CanVerify))
        {
            var provider = _providerRegistry.GetProvider(key.StorageProvider)
                ?? _providerRegistry.GetDefaultProvider();

            var publicKey = await provider.GetPublicKeyAsync(key, cancellationToken);
            if (publicKey != null)
            {
                validationKeys.Add(publicKey);
            }
        }

        return validationKeys;
    }

    /// <summary>
    /// Get JWKS for discovery endpoint (public keys only)
    /// </summary>
    public async Task<JsonWebKeySet> GetJwksAsync(CancellationToken cancellationToken = default)
    {
        var tenantId = _tenantContext.TenantId;
        var keys = await _keyStore.GetJwksKeysAsync(tenantId, cancellationToken);

        var jwks = new JsonWebKeySet();

        foreach (var key in keys)
        {
            var provider = _providerRegistry.GetProvider(key.StorageProvider)
                ?? _providerRegistry.GetDefaultProvider();

            var jwk = await provider.GetJsonWebKeyAsync(key, cancellationToken);
            if (jwk != null)
            {
                jwks.Keys.Add(jwk);
            }
        }

        return jwks;
    }

    /// <summary>
    /// Rotate keys using configured provider
    /// </summary>
    public async Task RotateKeysAsync(
        string? clientId = null,
        CancellationToken cancellationToken = default)
    {
        var tenantId = _tenantContext.TenantId;

        _logger.LogInformation("Starting key rotation for tenant {TenantId}, client {ClientId}", tenantId, clientId);

        // Get rotation config
        var config = await _keyStore.GetRotationConfigAsync(tenantId, clientId, cancellationToken);
        if (config == null)
        {
            config = new KeyRotationConfig
            {
                TenantId = tenantId,
                ClientId = clientId
            };
        }

        // Generate new key using configured provider
        var newKey = await GenerateKeyAsync(new GenerateKeyRequest
        {
            TenantId = tenantId,
            ClientId = clientId,
            KeyType = config.KeyType,
            Algorithm = config.Algorithm,
            KeySize = config.KeySize,
            LifetimeDays = config.KeyLifetimeDays,
            ActivateImmediately = true,
            Priority = 200, // Higher priority for new keys
            StorageProvider = config.PreferredStorageProvider
        }, cancellationToken);

        // Demote existing active keys
        var existingKeys = clientId != null
            ? await _keyStore.GetByClientAsync(tenantId, clientId, cancellationToken)
            : await _keyStore.GetByTenantAsync(tenantId, cancellationToken);

        foreach (var key in existingKeys.Where(k => k.Id != newKey.Id && k.Status == SigningKeyStatus.Active))
        {
            key.Priority = 50; // Lower priority - used for verification only
            await _keyStore.UpdateAsync(key, cancellationToken);
        }

        // Update rotation config
        config.LastRotationAt = DateTime.UtcNow;
        config.NextRotationAt = DateTime.UtcNow.AddDays(config.KeyLifetimeDays - config.RotationLeadDays);
        await _keyStore.SaveRotationConfigAsync(config, cancellationToken);

        // Cleanup old keys
        await CleanupOldKeysAsync(tenantId, clientId, config.MaxKeys, config.GracePeriodDays, cancellationToken);

        _logger.LogInformation("Key rotation completed. New key: {KeyId}", newKey.KeyId);
    }

    /// <summary>
    /// Revoke a key (also revokes in provider if applicable)
    /// </summary>
    public async Task RevokeKeyAsync(
        string keyId,
        string reason,
        CancellationToken cancellationToken = default)
    {
        var key = await _keyStore.GetByIdAsync(keyId, cancellationToken);
        if (key == null)
        {
            throw new InvalidOperationException($"Key {keyId} not found");
        }

        key.Status = SigningKeyStatus.Revoked;
        key.RevokedAt = DateTime.UtcNow;
        key.RevocationReason = reason;
        key.IncludeInJwks = false;

        await _keyStore.UpdateAsync(key, cancellationToken);

        _logger.LogWarning("Key {KeyId} revoked: {Reason}", keyId, reason);
    }

    /// <summary>
    /// Process automatic key rotation for expiring keys
    /// </summary>
    public async Task ProcessScheduledRotationsAsync(CancellationToken cancellationToken = default)
    {
        var expiringKeys = await _keyStore.GetExpiringKeysAsync(14, cancellationToken);

        foreach (var keyGroup in expiringKeys.GroupBy(k => new { k.TenantId, k.ClientId }))
        {
            try
            {
                var keys = keyGroup.Key.ClientId != null
                    ? await _keyStore.GetByClientAsync(keyGroup.Key.TenantId, keyGroup.Key.ClientId, cancellationToken)
                    : await _keyStore.GetByTenantAsync(keyGroup.Key.TenantId, cancellationToken);

                var hasNewerKey = keys.Any(k =>
                    k.Status == SigningKeyStatus.Active &&
                    !k.IsExpiringSoon &&
                    k.Priority >= 100);

                if (!hasNewerKey)
                {
                    _logger.LogInformation(
                        "Auto-rotating keys for tenant {TenantId}, client {ClientId}",
                        keyGroup.Key.TenantId, keyGroup.Key.ClientId);

                    await RotateKeysAsync(keyGroup.Key.ClientId, cancellationToken);
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex,
                    "Failed to auto-rotate keys for tenant {TenantId}, client {ClientId}",
                    keyGroup.Key.TenantId, keyGroup.Key.ClientId);
            }
        }
    }

    #region Private Methods

    private static string GetDefaultAlgorithm(SigningKeyType keyType) => keyType switch
    {
        SigningKeyType.RSA => "RS256",
        SigningKeyType.EC => "ES256",
        SigningKeyType.Symmetric => "HS256",
        _ => "RS256"
    };

    private static int GetDefaultKeySize(SigningKeyType keyType) => keyType switch
    {
        SigningKeyType.RSA => 2048,
        SigningKeyType.EC => 256,
        SigningKeyType.Symmetric => 256,
        _ => 2048
    };

    private async Task CleanupOldKeysAsync(
        string? tenantId,
        string? clientId,
        int maxKeys,
        int gracePeriodDays,
        CancellationToken cancellationToken)
    {
        var keys = clientId != null
            ? await _keyStore.GetByClientAsync(tenantId, clientId, cancellationToken)
            : await _keyStore.GetByTenantAsync(tenantId, cancellationToken);

        var orderedKeys = keys
            .OrderByDescending(k => k.Priority)
            .ThenByDescending(k => k.CreatedAt)
            .ToList();

        var keysToRemove = orderedKeys.Skip(maxKeys).ToList();

        foreach (var key in keysToRemove)
        {
            if (key.ExpiresAt.HasValue &&
                key.ExpiresAt.Value.AddDays(gracePeriodDays) < DateTime.UtcNow)
            {
                // Delete from provider first
                var provider = _providerRegistry.GetProvider(key.StorageProvider);
                if (provider != null)
                {
                    try
                    {
                        await provider.DeleteKeyAsync(key, cancellationToken);
                    }
                    catch (Exception ex)
                    {
                        _logger.LogWarning(ex, "Failed to delete key from provider: {KeyId}", key.KeyId);
                    }
                }

                await _keyStore.DeleteAsync(key.Id, cancellationToken);
                _logger.LogInformation("Deleted old key {KeyId}", key.KeyId);
            }
            else if (key.Status != SigningKeyStatus.Archived)
            {
                key.Status = SigningKeyStatus.Archived;
                key.IncludeInJwks = false;
                await _keyStore.UpdateAsync(key, cancellationToken);
                _logger.LogInformation("Archived old key {KeyId}", key.KeyId);
            }
        }
    }

    #endregion
}
