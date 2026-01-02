using System.Security.Cryptography.X509Certificates;
using System.Text.Json;
using Microsoft.Extensions.Logging;
using Oluso.Core.Domain.Interfaces;
using Oluso.Core.Services;
using Oluso.Enterprise.Saml.Configuration;

namespace Oluso.Enterprise.Saml.Services;

/// <summary>
/// Default implementation of ISamlTenantSettingsService.
/// Stores SAML settings in the Tenant.Configuration JSON field.
/// </summary>
public class SamlTenantSettingsService : ISamlTenantSettingsService
{
    private readonly ITenantStore _tenantStore;
    private readonly ICertificateService _certificateService;
    private readonly ILogger<SamlTenantSettingsService> _logger;

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        WriteIndented = false
    };

    public SamlTenantSettingsService(
        ITenantStore tenantStore,
        ICertificateService certificateService,
        ILogger<SamlTenantSettingsService> logger)
    {
        _tenantStore = tenantStore;
        _certificateService = certificateService;
        _logger = logger;
    }

    public async Task<SamlTenantSettings> GetSettingsAsync(string tenantId, CancellationToken cancellationToken = default)
    {
        var tenant = await _tenantStore.GetByIdAsync(tenantId, cancellationToken);
        if (tenant == null)
        {
            _logger.LogWarning("Tenant {TenantId} not found when getting SAML settings", tenantId);
            return SamlTenantSettings.Default;
        }

        return ExtractSettings(tenant.Configuration);
    }

    public async Task<SamlTenantSettings> UpdateSettingsAsync(
        string tenantId,
        Action<SamlTenantSettings> updateAction,
        CancellationToken cancellationToken = default)
    {
        var tenant = await _tenantStore.GetByIdAsync(tenantId, cancellationToken);
        if (tenant == null)
        {
            throw new InvalidOperationException($"Tenant {tenantId} not found");
        }

        // Get current settings
        var settings = ExtractSettings(tenant.Configuration);

        // Apply updates
        updateAction(settings);

        // Merge back into configuration
        tenant.Configuration = MergeSettings(tenant.Configuration, settings);
        tenant.Updated = DateTime.UtcNow;

        await _tenantStore.UpdateAsync(tenant, cancellationToken);

        _logger.LogInformation("Updated SAML IdP settings for tenant {TenantId}", tenantId);

        return settings;
    }

    private static SamlTenantSettings ExtractSettings(string? configurationJson)
    {
        if (string.IsNullOrEmpty(configurationJson))
        {
            return SamlTenantSettings.Default;
        }

        try
        {
            var config = JsonSerializer.Deserialize<Dictionary<string, JsonElement>>(configurationJson, JsonOptions);
            if (config != null && config.TryGetValue(SamlTenantSettings.SectionKey, out var section))
            {
                var settings = JsonSerializer.Deserialize<SamlTenantSettings>(section.GetRawText(), JsonOptions);
                return settings ?? SamlTenantSettings.Default;
            }
        }
        catch (JsonException)
        {
            // Invalid JSON, return default
        }

        return SamlTenantSettings.Default;
    }

    private static string MergeSettings(string? existingConfiguration, SamlTenantSettings settings)
    {
        Dictionary<string, object> config;

        if (!string.IsNullOrEmpty(existingConfiguration))
        {
            try
            {
                config = JsonSerializer.Deserialize<Dictionary<string, object>>(existingConfiguration, JsonOptions)
                         ?? new Dictionary<string, object>();
            }
            catch (JsonException)
            {
                config = new Dictionary<string, object>();
            }
        }
        else
        {
            config = new Dictionary<string, object>();
        }

        // Update the SAML section
        config[SamlTenantSettings.SectionKey] = settings;

        return JsonSerializer.Serialize(config, JsonOptions);
    }

    public async Task<X509Certificate2> GetSigningCertificateAsync(string tenantId, CancellationToken cancellationToken = default)
    {
        var settings = await GetSettingsAsync(tenantId, cancellationToken);
        return await GetCertificateByConfigAsync(
            settings.SigningCertificate,
            CertificatePurpose.SamlSigning,
            tenantId,
            cancellationToken);
    }

    public async Task<X509Certificate2?> GetEncryptionCertificateAsync(string tenantId, CancellationToken cancellationToken = default)
    {
        var settings = await GetSettingsAsync(tenantId, cancellationToken);

        // Encryption certificate is optional
        if (settings.EncryptionCertificate == null || settings.EncryptionCertificate.Source == SamlCertificateSource.Global)
        {
            return await _certificateService.GetCertificateAsync(CertificatePurpose.SamlEncryption, cancellationToken: cancellationToken);
        }

        return await GetCertificateByConfigAsync(
            settings.EncryptionCertificate,
            CertificatePurpose.SamlEncryption,
            tenantId,
            cancellationToken);
    }

    public async Task<SamlCertificateInfo> GenerateSigningCertificateAsync(string tenantId, CancellationToken cancellationToken = default)
    {
        var certInfo = await _certificateService.GenerateSelfSignedCertificateAsync(
            new GenerateCertificateRequest
            {
                Name = $"SAML Signing Certificate - Tenant {tenantId}",
                Purpose = CertificatePurpose.SamlSigning,
                TenantId = tenantId,
                Subject = $"CN=SAML Signing Certificate, O=Tenant {tenantId}",
                KeyUsage = CertificateKeyUsage.DigitalSignature | CertificateKeyUsage.NonRepudiation,
                ValidityDays = 365 * 2 // 2 years
            },
            cancellationToken);

        await UpdateSettingsAsync(tenantId, settings =>
        {
            settings.SigningCertificate = new SamlCertificateConfig
            {
                Source = SamlCertificateSource.Auto,
                CertificateId = certInfo.Id
            };
        }, cancellationToken);

        _logger.LogInformation("Generated signing certificate {CertificateId} for tenant {TenantId}", certInfo.Id, tenantId);

        return ToSamlCertificateInfo(certInfo, SamlCertificateSource.Auto);
    }

    public async Task<SamlCertificateInfo> GenerateEncryptionCertificateAsync(string tenantId, CancellationToken cancellationToken = default)
    {
        var certInfo = await _certificateService.GenerateSelfSignedCertificateAsync(
            new GenerateCertificateRequest
            {
                Name = $"SAML Encryption Certificate - Tenant {tenantId}",
                Purpose = CertificatePurpose.SamlEncryption,
                TenantId = tenantId,
                Subject = $"CN=SAML Encryption Certificate, O=Tenant {tenantId}",
                KeyUsage = CertificateKeyUsage.KeyEncipherment | CertificateKeyUsage.DataEncipherment,
                ValidityDays = 365 * 2 // 2 years
            },
            cancellationToken);

        await UpdateSettingsAsync(tenantId, settings =>
        {
            settings.EncryptionCertificate = new SamlCertificateConfig
            {
                Source = SamlCertificateSource.Auto,
                CertificateId = certInfo.Id
            };
        }, cancellationToken);

        _logger.LogInformation("Generated encryption certificate {CertificateId} for tenant {TenantId}", certInfo.Id, tenantId);

        return ToSamlCertificateInfo(certInfo, SamlCertificateSource.Auto);
    }

    public async Task<SamlCertificateInfo> UploadSigningCertificateAsync(
        string tenantId,
        string base64Pfx,
        string? password,
        CancellationToken cancellationToken = default)
    {
        var certInfo = await _certificateService.ImportCertificateAsync(
            new ImportCertificateRequest
            {
                Name = $"Uploaded SAML Signing Certificate - Tenant {tenantId}",
                Purpose = CertificatePurpose.SamlSigning,
                TenantId = tenantId,
                PfxData = base64Pfx,
                Password = password
            },
            cancellationToken);

        await UpdateSettingsAsync(tenantId, settings =>
        {
            settings.SigningCertificate = new SamlCertificateConfig
            {
                Source = SamlCertificateSource.Uploaded,
                CertificateId = certInfo.Id
            };
        }, cancellationToken);

        _logger.LogInformation("Uploaded signing certificate {CertificateId} for tenant {TenantId}", certInfo.Id, tenantId);

        return ToSamlCertificateInfo(certInfo, SamlCertificateSource.Uploaded);
    }

    public async Task<SamlCertificateInfo> UploadEncryptionCertificateAsync(
        string tenantId,
        string base64Pfx,
        string? password,
        CancellationToken cancellationToken = default)
    {
        var certInfo = await _certificateService.ImportCertificateAsync(
            new ImportCertificateRequest
            {
                Name = $"Uploaded SAML Encryption Certificate - Tenant {tenantId}",
                Purpose = CertificatePurpose.SamlEncryption,
                TenantId = tenantId,
                PfxData = base64Pfx,
                Password = password
            },
            cancellationToken);

        await UpdateSettingsAsync(tenantId, settings =>
        {
            settings.EncryptionCertificate = new SamlCertificateConfig
            {
                Source = SamlCertificateSource.Uploaded,
                CertificateId = certInfo.Id
            };
        }, cancellationToken);

        _logger.LogInformation("Uploaded encryption certificate {CertificateId} for tenant {TenantId}", certInfo.Id, tenantId);

        return ToSamlCertificateInfo(certInfo, SamlCertificateSource.Uploaded);
    }

    public async Task<SamlCertificateInfo?> GetSigningCertificateInfoAsync(string tenantId, CancellationToken cancellationToken = default)
    {
        var settings = await GetSettingsAsync(tenantId, cancellationToken);
        return await GetCertificateInfoByConfigAsync(settings.SigningCertificate, CertificatePurpose.SamlSigning, tenantId, cancellationToken);
    }

    public async Task<SamlCertificateInfo?> GetEncryptionCertificateInfoAsync(string tenantId, CancellationToken cancellationToken = default)
    {
        var settings = await GetSettingsAsync(tenantId, cancellationToken);
        return await GetCertificateInfoByConfigAsync(settings.EncryptionCertificate, CertificatePurpose.SamlEncryption, tenantId, cancellationToken);
    }

    public async Task ResetSigningCertificateToGlobalAsync(string tenantId, CancellationToken cancellationToken = default)
    {
        await UpdateSettingsAsync(tenantId, settings =>
        {
            settings.SigningCertificate = null;
        }, cancellationToken);

        _logger.LogInformation("Reset signing certificate to global for tenant {TenantId}", tenantId);
    }

    public async Task ResetEncryptionCertificateToGlobalAsync(string tenantId, CancellationToken cancellationToken = default)
    {
        await UpdateSettingsAsync(tenantId, settings =>
        {
            settings.EncryptionCertificate = null;
        }, cancellationToken);

        _logger.LogInformation("Reset encryption certificate to global for tenant {TenantId}", tenantId);
    }

    private async Task<X509Certificate2> GetCertificateByConfigAsync(
        SamlCertificateConfig? config,
        string purpose,
        string tenantId,
        CancellationToken cancellationToken)
    {
        // If no config or Global, use the global certificate
        if (config == null || config.Source == SamlCertificateSource.Global)
        {
            var globalCert = await _certificateService.GetCertificateAsync(purpose, cancellationToken: cancellationToken);
            if (globalCert == null)
            {
                throw new InvalidOperationException($"No global {purpose} certificate configured");
            }
            return globalCert;
        }

        // For Auto or Uploaded, we need a certificate ID
        if (string.IsNullOrEmpty(config.CertificateId))
        {
            // Auto-generate if needed
            if (config.Source == SamlCertificateSource.Auto)
            {
                var cert = await _certificateService.EnsureCertificateAsync(purpose, tenantId, cancellationToken: cancellationToken);
                return cert;
            }
            throw new InvalidOperationException($"No certificate ID configured for tenant {tenantId}");
        }

        // Load by certificate ID via purpose and tenantId
        var tenantCert = await _certificateService.GetCertificateAsync(purpose, tenantId, cancellationToken: cancellationToken);
        if (tenantCert == null)
        {
            throw new InvalidOperationException($"Certificate {config.CertificateId} not found for tenant {tenantId}");
        }

        return tenantCert;
    }

    private async Task<SamlCertificateInfo?> GetCertificateInfoByConfigAsync(
        SamlCertificateConfig? config,
        string purpose,
        string tenantId,
        CancellationToken cancellationToken)
    {
        var source = config?.Source ?? SamlCertificateSource.Global;

        // For Global, get global cert info
        if (source == SamlCertificateSource.Global)
        {
            var globalCert = await _certificateService.GetCertificateAsync(purpose, cancellationToken: cancellationToken);
            if (globalCert == null)
            {
                return null;
            }
            return new SamlCertificateInfo
            {
                Source = SamlCertificateSource.Global,
                Subject = globalCert.Subject,
                Issuer = globalCert.Issuer,
                NotBefore = globalCert.NotBefore,
                NotAfter = globalCert.NotAfter,
                Thumbprint = globalCert.Thumbprint
            };
        }

        // For Auto or Uploaded, get tenant-specific cert info
        if (!string.IsNullOrEmpty(config?.CertificateId))
        {
            var certInfo = await _certificateService.GetCertificateInfoAsync(config.CertificateId, cancellationToken);
            if (certInfo != null)
            {
                return ToSamlCertificateInfo(certInfo, source);
            }
        }

        // Try to get by purpose/tenant
        var tenantCert = await _certificateService.GetCertificateAsync(purpose, tenantId, cancellationToken: cancellationToken);
        if (tenantCert != null)
        {
            return new SamlCertificateInfo
            {
                Source = source,
                Subject = tenantCert.Subject,
                Issuer = tenantCert.Issuer,
                NotBefore = tenantCert.NotBefore,
                NotAfter = tenantCert.NotAfter,
                Thumbprint = tenantCert.Thumbprint
            };
        }

        return null;
    }

    private static SamlCertificateInfo ToSamlCertificateInfo(CertificateInfo certInfo, SamlCertificateSource source)
    {
        return new SamlCertificateInfo
        {
            Source = source,
            CertificateId = certInfo.Id,
            Subject = certInfo.Subject,
            Issuer = certInfo.Issuer,
            NotBefore = certInfo.NotBefore,
            NotAfter = certInfo.NotAfter,
            Thumbprint = certInfo.Thumbprint
        };
    }
}
