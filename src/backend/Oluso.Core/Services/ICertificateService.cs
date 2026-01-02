using System.Security.Cryptography.X509Certificates;
using System.Text.Json.Serialization;
using Oluso.Core.Domain.Entities;

namespace Oluso.Core.Services;

/// <summary>
/// Service for managing X.509 certificates used for SAML, mTLS, and other certificate-based operations.
/// Certificates can be stored locally (encrypted), in Azure Key Vault, or loaded from configuration.
/// </summary>
public interface ICertificateService
{
    /// <summary>
    /// Gets a signing certificate for the specified purpose and optional scope.
    /// </summary>
    /// <param name="purpose">Certificate purpose (e.g., "saml-signing", "saml-encryption")</param>
    /// <param name="tenantId">Optional tenant ID for tenant-scoped certificates</param>
    /// <param name="entityId">Optional entity ID (e.g., SP entity ID for SAML)</param>
    /// <param name="cancellationToken">Cancellation token</param>
    /// <returns>The certificate or null if not found</returns>
    Task<X509Certificate2?> GetCertificateAsync(
        string purpose,
        string? tenantId = null,
        string? entityId = null,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Gets all certificates for a purpose (e.g., for metadata with multiple signing certs).
    /// </summary>
    Task<IReadOnlyList<X509Certificate2>> GetCertificatesAsync(
        string purpose,
        string? tenantId = null,
        string? entityId = null,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Generates a new self-signed certificate and stores it.
    /// </summary>
    Task<CertificateInfo> GenerateSelfSignedCertificateAsync(
        GenerateCertificateRequest request,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Imports a certificate from PFX/P12 data.
    /// </summary>
    Task<CertificateInfo> ImportCertificateAsync(
        ImportCertificateRequest request,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Gets certificate info without loading the private key.
    /// </summary>
    Task<CertificateInfo?> GetCertificateInfoAsync(
        string certificateId,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Revokes a certificate.
    /// </summary>
    Task RevokeCertificateAsync(
        string certificateId,
        string reason,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Checks if a certificate exists for the given purpose.
    /// </summary>
    Task<bool> ExistsAsync(
        string purpose,
        string? tenantId = null,
        string? entityId = null,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Ensures a certificate exists, generating a self-signed one if not.
    /// </summary>
    Task<X509Certificate2> EnsureCertificateAsync(
        string purpose,
        string? tenantId = null,
        string? entityId = null,
        SelfSignedCertificateDefaults? defaults = null,
        CancellationToken cancellationToken = default);
}

/// <summary>
/// Provider for certificate material. Implementations handle different storage backends.
/// </summary>
public interface ICertificateMaterialProvider
{
    /// <summary>
    /// The storage provider type this handles.
    /// </summary>
    KeyStorageProvider ProviderType { get; }

    /// <summary>
    /// Generates a new certificate with key pair.
    /// </summary>
    Task<CertificateMaterialResult> GenerateCertificateAsync(
        CertificateGenerationParams request,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Loads a certificate from storage.
    /// </summary>
    Task<X509Certificate2?> LoadCertificateAsync(
        SigningKey key,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Deletes a certificate from storage.
    /// </summary>
    Task DeleteCertificateAsync(
        SigningKey key,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Checks if this provider is available/configured.
    /// </summary>
    Task<bool> IsAvailableAsync(CancellationToken cancellationToken = default);
}

/// <summary>
/// Request to generate a self-signed certificate.
/// </summary>
public class GenerateCertificateRequest
{
    /// <summary>
    /// Human-friendly name for the certificate.
    /// </summary>
    public string Name { get; set; } = "Signing Certificate";

    /// <summary>
    /// Certificate purpose (e.g., "saml-signing", "saml-encryption").
    /// </summary>
    public required string Purpose { get; set; }

    /// <summary>
    /// Optional tenant ID.
    /// </summary>
    public string? TenantId { get; set; }

    /// <summary>
    /// Optional entity ID (e.g., SAML SP entity ID).
    /// </summary>
    public string? EntityId { get; set; }

    /// <summary>
    /// Certificate subject (CN=..., O=..., etc.).
    /// </summary>
    public string Subject { get; set; } = "CN=Oluso Signing Certificate";

    /// <summary>
    /// Subject Alternative Names (DNS names).
    /// </summary>
    public List<string>? SubjectAlternativeNames { get; set; }

    /// <summary>
    /// Key type: RSA or EC.
    /// </summary>
    public SigningKeyType KeyType { get; set; } = SigningKeyType.RSA;

    /// <summary>
    /// Key size (2048, 4096 for RSA; 256, 384, 521 for EC).
    /// </summary>
    public int KeySize { get; set; } = 2048;

    /// <summary>
    /// Certificate validity in days.
    /// </summary>
    public int ValidityDays { get; set; } = 365;

    /// <summary>
    /// Key usage: signing, encryption, or both.
    /// </summary>
    public CertificateKeyUsage KeyUsage { get; set; } = CertificateKeyUsage.DigitalSignature;

    /// <summary>
    /// Preferred storage provider.
    /// </summary>
    public KeyStorageProvider StorageProvider { get; set; } = KeyStorageProvider.Local;
}

/// <summary>
/// Request to import an existing certificate.
/// </summary>
public class ImportCertificateRequest
{
    /// <summary>
    /// Human-friendly name.
    /// </summary>
    public string Name { get; set; } = "Imported Certificate";

    /// <summary>
    /// Certificate purpose.
    /// </summary>
    public required string Purpose { get; set; }

    /// <summary>
    /// Optional tenant ID.
    /// </summary>
    public string? TenantId { get; set; }

    /// <summary>
    /// Optional entity ID.
    /// </summary>
    public string? EntityId { get; set; }

    /// <summary>
    /// PFX/P12 data (base64 encoded).
    /// </summary>
    public required string PfxData { get; set; }

    /// <summary>
    /// Password for the PFX file.
    /// </summary>
    public string? Password { get; set; }

    /// <summary>
    /// Storage provider for the imported certificate.
    /// </summary>
    public KeyStorageProvider StorageProvider { get; set; } = KeyStorageProvider.Local;
}

/// <summary>
/// Parameters for certificate generation by a provider.
/// </summary>
public class CertificateGenerationParams
{
    public required string Subject { get; set; }
    public List<string>? SubjectAlternativeNames { get; set; }
    public SigningKeyType KeyType { get; set; } = SigningKeyType.RSA;
    public int KeySize { get; set; } = 2048;
    public int ValidityDays { get; set; } = 365;
    public CertificateKeyUsage KeyUsage { get; set; } = CertificateKeyUsage.DigitalSignature;
    public string? TenantId { get; set; }
    public string? EntityId { get; set; }
    public string? Name { get; set; }
    public Dictionary<string, string>? Tags { get; set; }
}

/// <summary>
/// Result from certificate material provider.
/// </summary>
public class CertificateMaterialResult
{
    /// <summary>
    /// The generated certificate (with private key for local storage).
    /// </summary>
    public X509Certificate2? Certificate { get; set; }

    /// <summary>
    /// Certificate thumbprint (SHA-1).
    /// </summary>
    public required string Thumbprint { get; set; }

    /// <summary>
    /// Certificate thumbprint (SHA-256).
    /// </summary>
    public required string ThumbprintSha256 { get; set; }

    /// <summary>
    /// Base64-encoded certificate (public only, for X5c).
    /// </summary>
    public required string CertificateData { get; set; }

    /// <summary>
    /// Encrypted private key data (for local storage).
    /// SECURITY: Never expose in API responses.
    /// </summary>
    [JsonIgnore]
    public string? EncryptedPrivateKey { get; set; }

    /// <summary>
    /// Key Vault or external URI (for cloud storage).
    /// </summary>
    public string? KeyVaultUri { get; set; }

    /// <summary>
    /// Certificate subject.
    /// </summary>
    public required string Subject { get; set; }

    /// <summary>
    /// Certificate issuer.
    /// </summary>
    public required string Issuer { get; set; }

    /// <summary>
    /// Serial number.
    /// </summary>
    public required string SerialNumber { get; set; }

    /// <summary>
    /// Not valid before.
    /// </summary>
    public DateTime NotBefore { get; set; }

    /// <summary>
    /// Not valid after.
    /// </summary>
    public DateTime NotAfter { get; set; }
}

/// <summary>
/// Information about a stored certificate.
/// </summary>
public class CertificateInfo
{
    public required string Id { get; set; }
    public required string Name { get; set; }
    public required string Purpose { get; set; }
    public string? TenantId { get; set; }
    public string? EntityId { get; set; }
    public required string Subject { get; set; }
    public required string Issuer { get; set; }
    public required string Thumbprint { get; set; }
    public required string SerialNumber { get; set; }
    public DateTime NotBefore { get; set; }
    public DateTime NotAfter { get; set; }
    public SigningKeyStatus Status { get; set; }
    public KeyStorageProvider StorageProvider { get; set; }
    public bool IsExpired => NotAfter <= DateTime.UtcNow;
    public bool IsExpiringSoon => NotAfter > DateTime.UtcNow && NotAfter <= DateTime.UtcNow.AddDays(30);
}

/// <summary>
/// Default settings for auto-generated self-signed certificates.
/// </summary>
public class SelfSignedCertificateDefaults
{
    public string SubjectFormat { get; set; } = "CN=Oluso {0} Certificate, O=Oluso";
    public SigningKeyType KeyType { get; set; } = SigningKeyType.RSA;
    public int KeySize { get; set; } = 2048;
    public int ValidityDays { get; set; } = 365;
    public CertificateKeyUsage KeyUsage { get; set; } = CertificateKeyUsage.DigitalSignature;
    public KeyStorageProvider StorageProvider { get; set; } = KeyStorageProvider.Local;
}

/// <summary>
/// Certificate key usage flags.
/// </summary>
[Flags]
public enum CertificateKeyUsage
{
    None = 0,
    DigitalSignature = 1,
    KeyEncipherment = 2,
    DataEncipherment = 4,
    NonRepudiation = 8,
    All = DigitalSignature | KeyEncipherment | DataEncipherment | NonRepudiation
}

/// <summary>
/// Well-known certificate purposes.
/// </summary>
public static class CertificatePurpose
{
    public const string SamlSigning = "saml-signing";
    public const string SamlEncryption = "saml-encryption";
    public const string OidcSigning = "oidc-signing";
    public const string MtlsClient = "mtls-client";
    public const string MtlsServer = "mtls-server";
    public const string LdapTls = "ldap-tls";
}
