using System.Security.Cryptography.X509Certificates;
using Oluso.Enterprise.Saml.Configuration;

namespace Oluso.Enterprise.Saml.Services;

/// <summary>
/// Service for reading and updating SAML-specific tenant settings.
/// Settings are stored in the Tenant.Configuration JSON field.
/// </summary>
public interface ISamlTenantSettingsService
{
    /// <summary>
    /// Gets SAML IdP settings for the specified tenant.
    /// </summary>
    Task<SamlTenantSettings> GetSettingsAsync(string tenantId, CancellationToken cancellationToken = default);

    /// <summary>
    /// Updates SAML IdP settings for the specified tenant.
    /// </summary>
    Task<SamlTenantSettings> UpdateSettingsAsync(
        string tenantId,
        Action<SamlTenantSettings> updateAction,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Gets the signing certificate for the specified tenant.
    /// Falls back to global certificate if tenant has no specific config.
    /// </summary>
    Task<X509Certificate2> GetSigningCertificateAsync(string tenantId, CancellationToken cancellationToken = default);

    /// <summary>
    /// Gets the encryption certificate for the specified tenant.
    /// Falls back to global certificate if tenant has no specific config.
    /// </summary>
    Task<X509Certificate2?> GetEncryptionCertificateAsync(string tenantId, CancellationToken cancellationToken = default);

    /// <summary>
    /// Generates a new auto-generated signing certificate for the tenant.
    /// </summary>
    Task<SamlCertificateInfo> GenerateSigningCertificateAsync(string tenantId, CancellationToken cancellationToken = default);

    /// <summary>
    /// Generates a new auto-generated encryption certificate for the tenant.
    /// </summary>
    Task<SamlCertificateInfo> GenerateEncryptionCertificateAsync(string tenantId, CancellationToken cancellationToken = default);

    /// <summary>
    /// Uploads and sets a signing certificate for the tenant.
    /// </summary>
    Task<SamlCertificateInfo> UploadSigningCertificateAsync(
        string tenantId,
        string base64Pfx,
        string? password,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Uploads and sets an encryption certificate for the tenant.
    /// </summary>
    Task<SamlCertificateInfo> UploadEncryptionCertificateAsync(
        string tenantId,
        string base64Pfx,
        string? password,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Gets certificate information for display (without private key).
    /// </summary>
    Task<SamlCertificateInfo?> GetSigningCertificateInfoAsync(string tenantId, CancellationToken cancellationToken = default);

    /// <summary>
    /// Gets encryption certificate information for display (without private key).
    /// </summary>
    Task<SamlCertificateInfo?> GetEncryptionCertificateInfoAsync(string tenantId, CancellationToken cancellationToken = default);

    /// <summary>
    /// Resets the signing certificate to use the global certificate.
    /// </summary>
    Task ResetSigningCertificateToGlobalAsync(string tenantId, CancellationToken cancellationToken = default);

    /// <summary>
    /// Resets the encryption certificate to use the global certificate.
    /// </summary>
    Task ResetEncryptionCertificateToGlobalAsync(string tenantId, CancellationToken cancellationToken = default);
}

/// <summary>
/// Information about a SAML certificate for display purposes.
/// </summary>
public class SamlCertificateInfo
{
    public required SamlCertificateSource Source { get; init; }
    public string? CertificateId { get; init; }
    public string? Subject { get; init; }
    public string? Issuer { get; init; }
    public DateTime? NotBefore { get; init; }
    public DateTime? NotAfter { get; init; }
    public string? Thumbprint { get; init; }
    public bool IsExpired => NotAfter.HasValue && NotAfter.Value < DateTime.UtcNow;
    public bool IsExpiringSoon => NotAfter.HasValue && NotAfter.Value < DateTime.UtcNow.AddDays(30);
}
