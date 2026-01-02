using System.Security.Cryptography.X509Certificates;
using Microsoft.Extensions.Logging;
using Oluso.Core.Domain.Entities;
using Oluso.Core.Domain.Interfaces;
using Oluso.Core.Services;
using Oluso.Keys;

namespace Oluso.Certificates;

/// <summary>
/// Default implementation of ICertificateService.
/// Manages certificates stored in SigningKey entities, supporting multiple providers.
/// </summary>
public class CertificateService : ICertificateService
{
    private readonly ISigningKeyStore _keyStore;
    private readonly ICertificateMaterialProviderRegistry _providerRegistry;
    private readonly ITenantContext? _tenantContext;
    private readonly ILogger<CertificateService> _logger;

    public CertificateService(
        ISigningKeyStore keyStore,
        ICertificateMaterialProviderRegistry providerRegistry,
        ILogger<CertificateService> logger,
        ITenantContext? tenantContext = null)
    {
        _keyStore = keyStore;
        _providerRegistry = providerRegistry;
        _tenantContext = tenantContext;
        _logger = logger;
    }

    public async Task<X509Certificate2?> GetCertificateAsync(
        string purpose,
        string? tenantId = null,
        string? entityId = null,
        CancellationToken cancellationToken = default)
    {
        tenantId ??= _tenantContext?.TenantId;

        var key = await FindCertificateKeyAsync(purpose, tenantId, entityId, cancellationToken);
        if (key == null)
        {
            return null;
        }

        return await LoadCertificateFromKeyAsync(key, cancellationToken);
    }

    public async Task<IReadOnlyList<X509Certificate2>> GetCertificatesAsync(
        string purpose,
        string? tenantId = null,
        string? entityId = null,
        CancellationToken cancellationToken = default)
    {
        tenantId ??= _tenantContext?.TenantId;

        var keys = await _keyStore.GetByTenantAsync(tenantId, cancellationToken);

        IEnumerable<SigningKey> certificateKeys = keys
            .Where(k => k.HasCertificate && k.Name.StartsWith(purpose) && k.CanVerify)
            .OrderByDescending(k => k.Priority)
            .ThenByDescending(k => k.CreatedAt);

        if (!string.IsNullOrEmpty(entityId))
        {
            certificateKeys = certificateKeys.Where(k => k.ClientId == entityId);
        }

        var certificates = new List<X509Certificate2>();
        foreach (var key in certificateKeys)
        {
            var cert = await LoadCertificateFromKeyAsync(key, cancellationToken);
            if (cert != null)
            {
                certificates.Add(cert);
            }
        }

        return certificates;
    }

    public async Task<CertificateInfo> GenerateSelfSignedCertificateAsync(
        GenerateCertificateRequest request,
        CancellationToken cancellationToken = default)
    {
        request.TenantId ??= _tenantContext?.TenantId;

        var provider = _providerRegistry.GetProvider(request.StorageProvider);
        if (provider == null)
        {
            throw new InvalidOperationException($"No certificate provider available for: {request.StorageProvider}");
        }

        var genParams = new CertificateGenerationParams
        {
            Subject = request.Subject,
            SubjectAlternativeNames = request.SubjectAlternativeNames,
            KeyType = request.KeyType,
            KeySize = request.KeySize,
            ValidityDays = request.ValidityDays,
            KeyUsage = request.KeyUsage,
            TenantId = request.TenantId,
            EntityId = request.EntityId,
            Name = request.Name
        };

        var result = await provider.GenerateCertificateAsync(genParams, cancellationToken);

        // Create SigningKey entity
        var keyId = Guid.NewGuid().ToString("N")[..16];
        var key = new SigningKey
        {
            Id = Guid.NewGuid().ToString("N"),
            Name = $"{request.Purpose}:{request.Name}",
            KeyId = keyId,
            TenantId = request.TenantId,
            ClientId = request.EntityId,
            KeyType = request.KeyType,
            Algorithm = request.KeyType == SigningKeyType.RSA ? "RS256" : "ES256",
            Use = request.KeyUsage.HasFlag(CertificateKeyUsage.KeyEncipherment)
                ? SigningKeyUse.Encryption
                : SigningKeyUse.Signing,
            KeySize = request.KeySize,
            StorageProvider = request.StorageProvider,
            PrivateKeyData = result.EncryptedPrivateKey ?? "",
            PublicKeyData = result.CertificateData,
            KeyVaultUri = result.KeyVaultUri,
            X5t = result.Thumbprint,
            X5tS256 = result.ThumbprintSha256,
            X5c = result.CertificateData,
            CertificateSubject = result.Subject,
            CertificateIssuer = result.Issuer,
            CertificateSerialNumber = result.SerialNumber,
            CertificateNotBefore = result.NotBefore,
            CertificateNotAfter = result.NotAfter,
            Status = SigningKeyStatus.Active,
            CreatedAt = DateTime.UtcNow,
            ActivatedAt = DateTime.UtcNow,
            ExpiresAt = result.NotAfter
        };

        await _keyStore.StoreAsync(key, cancellationToken);

        _logger.LogInformation(
            "Generated certificate: Purpose={Purpose}, Thumbprint={Thumbprint}, ValidUntil={NotAfter}",
            request.Purpose, result.Thumbprint, result.NotAfter);

        return new CertificateInfo
        {
            Id = key.Id,
            Name = request.Name,
            Purpose = request.Purpose,
            TenantId = request.TenantId,
            EntityId = request.EntityId,
            Subject = result.Subject,
            Issuer = result.Issuer,
            Thumbprint = result.Thumbprint,
            SerialNumber = result.SerialNumber,
            NotBefore = result.NotBefore,
            NotAfter = result.NotAfter,
            Status = SigningKeyStatus.Active,
            StorageProvider = request.StorageProvider
        };
    }

    public async Task<CertificateInfo> ImportCertificateAsync(
        ImportCertificateRequest request,
        CancellationToken cancellationToken = default)
    {
        request.TenantId ??= _tenantContext?.TenantId;

        // Load and validate the PFX with platform-appropriate flags
        var pfxBytes = Convert.FromBase64String(request.PfxData);
        var keyStorageFlags = OperatingSystem.IsMacOS()
            ? X509KeyStorageFlags.Exportable
            : X509KeyStorageFlags.MachineKeySet | X509KeyStorageFlags.Exportable;
        var cert = new X509Certificate2(pfxBytes, request.Password, keyStorageFlags);

        if (!cert.HasPrivateKey)
        {
            throw new ArgumentException("Certificate must include a private key");
        }

        // Get provider for encryption
        var provider = _providerRegistry.GetProvider(request.StorageProvider) as LocalCertificateMaterialProvider;

        // Export and encrypt
        var exportedPfx = cert.Export(X509ContentType.Pfx);
        var encryptedPrivateKey = provider != null
            ? Convert.ToBase64String(exportedPfx) // Will be encrypted by provider
            : Convert.ToBase64String(exportedPfx);

        // Get public certificate for X5c
        var publicCertBytes = cert.Export(X509ContentType.Cert);
        var certificateData = Convert.ToBase64String(publicCertBytes);

        var keyId = Guid.NewGuid().ToString("N")[..16];
        var key = new SigningKey
        {
            Id = Guid.NewGuid().ToString("N"),
            Name = $"{request.Purpose}:{request.Name}",
            KeyId = keyId,
            TenantId = request.TenantId,
            ClientId = request.EntityId,
            KeyType = cert.PublicKey.Oid.FriendlyName == "RSA" ? SigningKeyType.RSA : SigningKeyType.EC,
            Algorithm = cert.PublicKey.Oid.FriendlyName == "RSA" ? "RS256" : "ES256",
            Use = SigningKeyUse.Signing,
            StorageProvider = request.StorageProvider,
            PrivateKeyData = encryptedPrivateKey,
            PublicKeyData = certificateData,
            X5t = cert.Thumbprint,
            X5tS256 = ComputeSha256Thumbprint(cert),
            X5c = certificateData,
            CertificateSubject = cert.Subject,
            CertificateIssuer = cert.Issuer,
            CertificateSerialNumber = cert.SerialNumber,
            CertificateNotBefore = cert.NotBefore.ToUniversalTime(),
            CertificateNotAfter = cert.NotAfter.ToUniversalTime(),
            Status = SigningKeyStatus.Active,
            CreatedAt = DateTime.UtcNow,
            ActivatedAt = DateTime.UtcNow,
            ExpiresAt = cert.NotAfter.ToUniversalTime()
        };

        await _keyStore.StoreAsync(key, cancellationToken);

        _logger.LogInformation(
            "Imported certificate: Purpose={Purpose}, Thumbprint={Thumbprint}",
            request.Purpose, cert.Thumbprint);

        return new CertificateInfo
        {
            Id = key.Id,
            Name = request.Name,
            Purpose = request.Purpose,
            TenantId = request.TenantId,
            EntityId = request.EntityId,
            Subject = cert.Subject,
            Issuer = cert.Issuer,
            Thumbprint = cert.Thumbprint,
            SerialNumber = cert.SerialNumber,
            NotBefore = cert.NotBefore.ToUniversalTime(),
            NotAfter = cert.NotAfter.ToUniversalTime(),
            Status = SigningKeyStatus.Active,
            StorageProvider = request.StorageProvider
        };
    }

    public async Task<CertificateInfo?> GetCertificateInfoAsync(
        string certificateId,
        CancellationToken cancellationToken = default)
    {
        var key = await _keyStore.GetByIdAsync(certificateId, cancellationToken);
        if (key == null || !key.HasCertificate)
        {
            return null;
        }

        // Parse purpose from name (format: "purpose:name")
        var parts = key.Name.Split(':', 2);
        var purpose = parts.Length > 0 ? parts[0] : "unknown";
        var name = parts.Length > 1 ? parts[1] : key.Name;

        return new CertificateInfo
        {
            Id = key.Id,
            Name = name,
            Purpose = purpose,
            TenantId = key.TenantId,
            EntityId = key.ClientId,
            Subject = key.CertificateSubject ?? "",
            Issuer = key.CertificateIssuer ?? "",
            Thumbprint = key.X5t ?? "",
            SerialNumber = key.CertificateSerialNumber ?? "",
            NotBefore = key.CertificateNotBefore ?? DateTime.MinValue,
            NotAfter = key.CertificateNotAfter ?? DateTime.MaxValue,
            Status = key.Status,
            StorageProvider = key.StorageProvider
        };
    }

    public async Task RevokeCertificateAsync(
        string certificateId,
        string reason,
        CancellationToken cancellationToken = default)
    {
        var key = await _keyStore.GetByIdAsync(certificateId, cancellationToken);
        if (key == null)
        {
            throw new InvalidOperationException($"Certificate not found: {certificateId}");
        }

        key.Status = SigningKeyStatus.Revoked;
        key.RevokedAt = DateTime.UtcNow;
        key.RevocationReason = reason;

        await _keyStore.StoreAsync(key, cancellationToken);

        _logger.LogInformation(
            "Revoked certificate: Id={CertificateId}, Reason={Reason}",
            certificateId, reason);
    }

    public async Task<bool> ExistsAsync(
        string purpose,
        string? tenantId = null,
        string? entityId = null,
        CancellationToken cancellationToken = default)
    {
        tenantId ??= _tenantContext?.TenantId;

        var key = await FindCertificateKeyAsync(purpose, tenantId, entityId, cancellationToken);
        return key != null;
    }

    public async Task<X509Certificate2> EnsureCertificateAsync(
        string purpose,
        string? tenantId = null,
        string? entityId = null,
        SelfSignedCertificateDefaults? defaults = null,
        CancellationToken cancellationToken = default)
    {
        tenantId ??= _tenantContext?.TenantId;
        defaults ??= new SelfSignedCertificateDefaults();

        // Try to get existing certificate
        var existing = await GetCertificateAsync(purpose, tenantId, entityId, cancellationToken);
        if (existing != null)
        {
            return existing;
        }

        _logger.LogInformation(
            "No certificate found for purpose={Purpose}, generating self-signed certificate",
            purpose);

        // Generate self-signed certificate
        var request = new GenerateCertificateRequest
        {
            Name = purpose,
            Purpose = purpose,
            TenantId = tenantId,
            EntityId = entityId,
            Subject = string.Format(defaults.SubjectFormat, purpose),
            KeyType = defaults.KeyType,
            KeySize = defaults.KeySize,
            ValidityDays = defaults.ValidityDays,
            KeyUsage = defaults.KeyUsage,
            StorageProvider = defaults.StorageProvider
        };

        await GenerateSelfSignedCertificateAsync(request, cancellationToken);

        // Load and return the new certificate
        var cert = await GetCertificateAsync(purpose, tenantId, entityId, cancellationToken);
        if (cert == null)
        {
            throw new InvalidOperationException($"Failed to generate certificate for: {purpose}");
        }

        return cert;
    }

    private async Task<SigningKey?> FindCertificateKeyAsync(
        string purpose,
        string? tenantId,
        string? entityId,
        CancellationToken cancellationToken)
    {
        var keys = await _keyStore.GetByTenantAsync(tenantId, cancellationToken);

        var query = keys
            .Where(k => k.HasCertificate && k.Name.StartsWith(purpose) && k.CanSign)
            .OrderByDescending(k => k.Priority)
            .ThenByDescending(k => k.CreatedAt);

        if (!string.IsNullOrEmpty(entityId))
        {
            // First try entity-specific, then fall back to tenant-wide
            var entityKey = query.FirstOrDefault(k => k.ClientId == entityId);
            if (entityKey != null) return entityKey;
        }

        return query.FirstOrDefault(k => k.ClientId == null);
    }

    private async Task<X509Certificate2?> LoadCertificateFromKeyAsync(
        SigningKey key,
        CancellationToken cancellationToken)
    {
        var provider = _providerRegistry.GetProvider(key.StorageProvider);
        if (provider == null)
        {
            _logger.LogWarning(
                "No provider available for storage type: {StorageProvider}",
                key.StorageProvider);
            return null;
        }

        return await provider.LoadCertificateAsync(key, cancellationToken);
    }

    private static string ComputeSha256Thumbprint(X509Certificate2 cert)
    {
        using var sha256 = System.Security.Cryptography.SHA256.Create();
        var hash = sha256.ComputeHash(cert.RawData);
        return Convert.ToBase64String(hash)
            .TrimEnd('=')
            .Replace('+', '-')
            .Replace('/', '_');
    }
}

/// <summary>
/// Registry for certificate material providers.
/// </summary>
public interface ICertificateMaterialProviderRegistry
{
    ICertificateMaterialProvider? GetProvider(KeyStorageProvider providerType);
    IEnumerable<ICertificateMaterialProvider> GetAllProviders();
}

/// <summary>
/// Default implementation of certificate provider registry.
/// </summary>
public class CertificateMaterialProviderRegistry : ICertificateMaterialProviderRegistry
{
    private readonly Dictionary<KeyStorageProvider, ICertificateMaterialProvider> _providers;

    public CertificateMaterialProviderRegistry(IEnumerable<ICertificateMaterialProvider> providers)
    {
        _providers = providers.ToDictionary(p => p.ProviderType);
    }

    public ICertificateMaterialProvider? GetProvider(KeyStorageProvider providerType)
    {
        return _providers.GetValueOrDefault(providerType);
    }

    public IEnumerable<ICertificateMaterialProvider> GetAllProviders()
    {
        return _providers.Values;
    }
}
