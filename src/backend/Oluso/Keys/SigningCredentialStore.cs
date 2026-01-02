using Microsoft.Extensions.Logging;
using Microsoft.IdentityModel.Tokens;
using Oluso.Core.Domain.Entities;
using Oluso.Core.Domain.Interfaces;
using Oluso.Core.Services;

namespace Oluso.Keys;

/// <summary>
/// Production signing credential store that bridges to ISigningKeyService.
/// Uses the full key management system with tenant support, persistence, and Key Vault.
/// Auto-provisions a default signing key if none exists.
/// </summary>
public class SigningCredentialStore : ISigningCredentialStore
{
    private readonly ISigningKeyService _keyService;
    private readonly ISigningKeyStore _keyStore;
    private readonly ITenantContext _tenantContext;
    private readonly IKeyMaterialProviderRegistry _providerRegistry;
    private readonly ILogger<SigningCredentialStore> _logger;

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

    public async Task<SigningCredentials?> GetSigningCredentialsAsync(CancellationToken cancellationToken = default)
    {
        // Get credentials from the key service (tenant-aware)
        var credentials = await _keyService.GetSigningCredentialsAsync(clientId: null, cancellationToken);

        if (credentials != null)
        {
            return credentials;
        }

        // No active key - auto-provision one
        _logger.LogInformation("No active signing key found for tenant {TenantId}, auto-provisioning",
            _tenantContext.TenantId);

        // Use the default provider (Key Vault if configured, otherwise local)
        var defaultProvider = _providerRegistry.GetDefaultProvider();

        var newKey = await _keyService.GenerateKeyAsync(new GenerateKeyRequest
        {
            TenantId = _tenantContext.TenantId,
            Name = "Auto-provisioned signing key",
            KeyType = SigningKeyType.RSA,
            Algorithm = "RS256",
            KeySize = 2048,
            LifetimeDays = 90,
            ActivateImmediately = true,
            StorageProvider = defaultProvider.ProviderType
        }, cancellationToken);

        _logger.LogInformation("Auto-provisioned signing key {KeyId} for tenant {TenantId} using {Provider}",
            newKey.KeyId, _tenantContext.TenantId, defaultProvider.ProviderType);

        return await _keyService.GetSigningCredentialsAsync(clientId: null, cancellationToken);
    }

    public async Task<IEnumerable<SecurityKeyInfo>> GetValidationKeysAsync(CancellationToken cancellationToken = default)
    {
        var validationKeys = await _keyService.GetValidationKeysAsync(clientId: null, cancellationToken);

        return validationKeys.Select(k => new SecurityKeyInfo
        {
            Key = k,
            SigningAlgorithm = GetAlgorithmForKey(k)
        });
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
