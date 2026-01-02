namespace Oluso.Enterprise.Ldap.Configuration;

/// <summary>
/// LDAP Server settings for a tenant.
/// Controls behavior when Oluso exposes tenant users via LDAP protocol.
/// Stored in tenant Configuration JSON under "ldapServer" key.
/// </summary>
public class TenantLdapSettings
{
    /// <summary>
    /// Whether LDAP Server functionality is enabled for this tenant.
    /// </summary>
    public bool Enabled { get; set; }

    /// <summary>
    /// Custom Base DN for this tenant (overrides default).
    /// If not set, uses global BaseDn with tenant isolation.
    /// </summary>
    public string? BaseDn { get; set; }

    /// <summary>
    /// Custom organization name for this tenant.
    /// </summary>
    public string? Organization { get; set; }

    /// <summary>
    /// Whether to allow anonymous binds for this tenant.
    /// </summary>
    public bool AllowAnonymousBind { get; set; }

    /// <summary>
    /// Maximum search results for this tenant.
    /// </summary>
    public int? MaxSearchResults { get; set; }

    /// <summary>
    /// Admin DN for this tenant (for service accounts binding).
    /// </summary>
    public string? AdminDn { get; set; }

    /// <summary>
    /// Admin password hash for this tenant.
    /// </summary>
    public string? AdminPasswordHash { get; set; }

    /// <summary>
    /// TLS certificate configuration for this tenant.
    /// If not set, uses the global certificate.
    /// </summary>
    public LdapCertificateConfig? TlsCertificate { get; set; }

    public static TenantLdapSettings Default => new();
}

/// <summary>
/// Configuration for an LDAP TLS certificate.
/// </summary>
public class LdapCertificateConfig
{
    /// <summary>
    /// The source of the certificate.
    /// </summary>
    public LdapCertificateSource Source { get; set; } = LdapCertificateSource.Global;

    /// <summary>
    /// Reference to the certificate ID in ICertificateService.
    /// Used when Source is Auto or Uploaded.
    /// </summary>
    public string? CertificateId { get; set; }
}

/// <summary>
/// Source of an LDAP TLS certificate.
/// </summary>
public enum LdapCertificateSource
{
    /// <summary>
    /// Use the global certificate from appsettings or ICertificateService (default).
    /// </summary>
    Global = 0,

    /// <summary>
    /// Auto-generate a tenant-specific certificate.
    /// </summary>
    Auto = 1,

    /// <summary>
    /// Use an uploaded tenant-specific certificate.
    /// </summary>
    Uploaded = 2
}
