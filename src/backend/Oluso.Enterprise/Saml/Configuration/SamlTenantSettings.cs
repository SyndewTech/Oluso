namespace Oluso.Enterprise.Saml.Configuration;

/// <summary>
/// SAML IdP settings stored in the tenant's Configuration JSON.
/// This is the canonical storage format for SAML-specific tenant configuration.
/// </summary>
public class SamlTenantSettings
{
    /// <summary>
    /// Section key used in Tenant.Configuration JSON
    /// </summary>
    public const string SectionKey = "SamlIdp";

    /// <summary>
    /// Whether SAML IdP functionality is enabled for this tenant.
    /// </summary>
    public bool Enabled { get; set; }

    /// <summary>
    /// Journey name to use for authentication when Oluso acts as SAML IdP.
    /// If set, SAML SSO will redirect to this journey instead of /account/login.
    /// </summary>
    public string? LoginJourneyName { get; set; }

    /// <summary>
    /// Configuration for the tenant's SAML signing certificate.
    /// If not set, falls back to the global signing certificate.
    /// </summary>
    public SamlCertificateConfig? SigningCertificate { get; set; }

    /// <summary>
    /// Configuration for the tenant's SAML encryption certificate.
    /// If not set, falls back to the global encryption certificate.
    /// </summary>
    public SamlCertificateConfig? EncryptionCertificate { get; set; }

    /// <summary>
    /// Override the IdP Entity ID for this tenant.
    /// If not set, defaults to the issuer URL from IIssuerResolver.
    /// </summary>
    public string? EntityId { get; set; }

    /// <summary>
    /// Default SAML IdP settings
    /// </summary>
    public static SamlTenantSettings Default => new();
}

/// <summary>
/// Configuration for a SAML certificate (signing or encryption).
/// </summary>
public class SamlCertificateConfig
{
    /// <summary>
    /// The source of the certificate.
    /// </summary>
    public SamlCertificateSource Source { get; set; } = SamlCertificateSource.Global;

    /// <summary>
    /// Base64-encoded PFX for uploaded certificates.
    /// Only used when Source is Uploaded.
    /// </summary>
    public string? Base64Pfx { get; set; }

    /// <summary>
    /// Password for the uploaded PFX file.
    /// Only used when Source is Uploaded.
    /// </summary>
    public string? Password { get; set; }

    /// <summary>
    /// Reference to the SigningKey.Id for managed certificates.
    /// Populated after auto-generation or successful upload/import.
    /// </summary>
    public string? CertificateId { get; set; }
}

/// <summary>
/// Source of a SAML certificate.
/// </summary>
public enum SamlCertificateSource
{
    /// <summary>
    /// Use the global certificate from appsettings.json (default fallback).
    /// </summary>
    Global = 0,

    /// <summary>
    /// Auto-generate a tenant-specific certificate if not exists.
    /// </summary>
    Auto = 1,

    /// <summary>
    /// Use an uploaded PFX certificate.
    /// </summary>
    Uploaded = 2
}
