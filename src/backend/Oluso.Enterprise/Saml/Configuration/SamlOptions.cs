using System.Security.Cryptography.X509Certificates;

namespace Oluso.Enterprise.Saml.Configuration;

/// <summary>
/// Configuration options for SAML 2.0 Service Provider
/// </summary>
public class SamlSpOptions
{
    public const string SectionName = "Oluso:Saml:ServiceProvider";

    /// <summary>
    /// Entity ID (unique identifier) for this SP
    /// </summary>
    public string EntityId { get; set; } = string.Empty;

    /// <summary>
    /// Base URL for the SP (e.g., https://myapp.example.com)
    /// </summary>
    public string BaseUrl { get; set; } = string.Empty;

    /// <summary>
    /// Path for the Assertion Consumer Service (ACS) endpoint
    /// </summary>
    public string AssertionConsumerServicePath { get; set; } = "/saml/acs";

    /// <summary>
    /// Path for Single Logout Service endpoint
    /// </summary>
    public string SingleLogoutServicePath { get; set; } = "/saml/slo";

    /// <summary>
    /// Path for SP metadata endpoint
    /// </summary>
    public string MetadataPath { get; set; } = "/saml/metadata";

    /// <summary>
    /// Certificate for signing requests (optional for SP)
    /// </summary>
    public CertificateOptions? SigningCertificate { get; set; }

    /// <summary>
    /// Certificate for decrypting assertions
    /// </summary>
    public CertificateOptions? DecryptionCertificate { get; set; }

    /// <summary>
    /// Whether to sign authentication requests
    /// </summary>
    public bool SignAuthnRequests { get; set; } = false;

    /// <summary>
    /// Whether to require signed responses
    /// </summary>
    public bool RequireSignedResponses { get; set; } = true;

    /// <summary>
    /// Whether to require signed assertions
    /// </summary>
    public bool RequireSignedAssertions { get; set; } = true;

    /// <summary>
    /// Whether to require encrypted assertions
    /// </summary>
    public bool RequireEncryptedAssertions { get; set; } = false;

    /// <summary>
    /// Default name ID format
    /// </summary>
    public string NameIdFormat { get; set; } = "urn:oasis:names:tc:SAML:1.1:nameid-format:emailAddress";

    /// <summary>
    /// Allowed clock skew for token validation (in seconds)
    /// </summary>
    public int AllowedClockSkewSeconds { get; set; } = 300;

    /// <summary>
    /// Configured Identity Providers
    /// </summary>
    public List<SamlIdpConfig> IdentityProviders { get; set; } = new();
}

/// <summary>
/// Configuration stored in IdentityProvider.Properties JSON for SAML2 providers.
/// Used when loading dynamic SAML IdP configurations from the database.
/// </summary>
public class Saml2ProviderConfiguration
{
    public string EntityId { get; set; } = string.Empty;
    public string? MetadataUrl { get; set; }
    public string? SingleSignOnServiceUrl { get; set; }
    public string? SingleLogoutServiceUrl { get; set; }
    public string? SigningCertificate { get; set; }
    public Dictionary<string, string> ClaimMappings { get; set; } = new();
    public bool ProxyMode { get; set; }
    public bool StoreUserLocally { get; set; } = true;
    public bool AutoProvisionUsers { get; set; } = true;
    public List<string>? ProxyIncludeClaims { get; set; }
    public List<string>? ProxyExcludeClaims { get; set; }
}

/// <summary>
/// Configuration for a SAML Identity Provider (external IdP)
/// </summary>
public class SamlIdpConfig
{
    /// <summary>
    /// Unique name/scheme for this IdP
    /// </summary>
    public string Name { get; set; } = string.Empty;

    /// <summary>
    /// Display name shown in UI
    /// </summary>
    public string DisplayName { get; set; } = string.Empty;

    /// <summary>
    /// Entity ID of the IdP
    /// </summary>
    public string EntityId { get; set; } = string.Empty;

    /// <summary>
    /// URL to IdP metadata (recommended)
    /// </summary>
    public string? MetadataUrl { get; set; }

    /// <summary>
    /// Single Sign-On Service URL (if not using metadata)
    /// </summary>
    public string? SingleSignOnServiceUrl { get; set; }

    /// <summary>
    /// Single Logout Service URL
    /// </summary>
    public string? SingleLogoutServiceUrl { get; set; }

    /// <summary>
    /// IdP signing certificate (if not using metadata)
    /// </summary>
    public CertificateOptions? SigningCertificate { get; set; }

    /// <summary>
    /// Binding for SSO (Redirect or POST)
    /// </summary>
    public string SingleSignOnServiceBinding { get; set; } = "Redirect";

    /// <summary>
    /// Claim type mappings from SAML attributes to claims
    /// </summary>
    public Dictionary<string, string> ClaimMappings { get; set; } = new();

    /// <summary>
    /// Whether this IdP is enabled
    /// </summary>
    public bool Enabled { get; set; } = true;

    /// <summary>
    /// Proxy mode - pass through external claims without storing local user
    /// </summary>
    public bool ProxyMode { get; set; } = false;

    /// <summary>
    /// Whether to store users locally (default true, set false for proxy mode)
    /// </summary>
    public bool StoreUserLocally { get; set; } = true;

    /// <summary>
    /// Auto-provision users on first login
    /// </summary>
    public bool AutoProvisionUsers { get; set; } = true;
}

/// <summary>
/// Configuration options for SAML 2.0 Identity Provider
/// </summary>
public class SamlIdpOptions
{
    public const string SectionName = "Oluso:Saml:IdentityProvider";

    /// <summary>
    /// Whether to enable IdP functionality
    /// </summary>
    public bool Enabled { get; set; } = false;

    /// <summary>
    /// Entity ID for this IdP
    /// </summary>
    public string EntityId { get; set; } = string.Empty;

    /// <summary>
    /// Base URL for the IdP
    /// </summary>
    public string BaseUrl { get; set; } = string.Empty;

    /// <summary>
    /// Path for SSO endpoint
    /// </summary>
    public string SingleSignOnServicePath { get; set; } = "/saml/sso";

    /// <summary>
    /// Path for SLO endpoint
    /// </summary>
    public string SingleLogoutServicePath { get; set; } = "/saml/idp/slo";

    /// <summary>
    /// Path for IdP metadata
    /// </summary>
    public string MetadataPath { get; set; } = "/saml/idp/metadata";

    /// <summary>
    /// Certificate for signing assertions (required for IdP)
    /// </summary>
    public CertificateOptions SigningCertificate { get; set; } = new();

    /// <summary>
    /// Certificate for encrypting assertions (optional)
    /// </summary>
    public CertificateOptions? EncryptionCertificate { get; set; }

    /// <summary>
    /// Default assertion lifetime in minutes
    /// </summary>
    public int AssertionLifetimeMinutes { get; set; } = 5;

    /// <summary>
    /// Supported name ID formats
    /// </summary>
    public List<string> NameIdFormats { get; set; } = new()
    {
        "urn:oasis:names:tc:SAML:1.1:nameid-format:emailAddress",
        "urn:oasis:names:tc:SAML:2.0:nameid-format:persistent",
        "urn:oasis:names:tc:SAML:2.0:nameid-format:transient"
    };

    /// <summary>
    /// Configured Service Providers
    /// </summary>
    public List<SamlSpConfig> ServiceProviders { get; set; } = new();
}

/// <summary>
/// Configuration for a registered Service Provider
/// </summary>
public class SamlSpConfig
{
    /// <summary>
    /// Entity ID of the SP
    /// </summary>
    public string EntityId { get; set; } = string.Empty;

    /// <summary>
    /// Display name
    /// </summary>
    public string? DisplayName { get; set; }

    /// <summary>
    /// URL to SP metadata
    /// </summary>
    public string? MetadataUrl { get; set; }

    /// <summary>
    /// Assertion Consumer Service URL (if not using metadata)
    /// </summary>
    public string? AssertionConsumerServiceUrl { get; set; }

    /// <summary>
    /// Single Logout Service URL
    /// </summary>
    public string? SingleLogoutServiceUrl { get; set; }

    /// <summary>
    /// SP signing certificate for verifying requests
    /// </summary>
    public CertificateOptions? SigningCertificate { get; set; }

    /// <summary>
    /// SP encryption certificate for encrypting assertions
    /// </summary>
    public CertificateOptions? EncryptionCertificate { get; set; }

    /// <summary>
    /// Whether to encrypt assertions for this SP
    /// </summary>
    public bool EncryptAssertions { get; set; } = false;

    /// <summary>
    /// Name ID format for this SP
    /// </summary>
    public string? NameIdFormat { get; set; }

    /// <summary>
    /// SSO binding preference (Redirect or POST)
    /// </summary>
    public string SsoBinding { get; set; } = "POST";

    /// <summary>
    /// Whether to sign SAML responses
    /// </summary>
    public bool SignResponses { get; set; } = true;

    /// <summary>
    /// Whether to sign SAML assertions
    /// </summary>
    public bool SignAssertions { get; set; } = true;

    /// <summary>
    /// Whether to require signed AuthnRequests from this SP
    /// </summary>
    public bool RequireSignedAuthnRequests { get; set; } = false;

    /// <summary>
    /// Claims to include in assertion for this SP
    /// </summary>
    public List<string> AllowedClaims { get; set; } = new();

    /// <summary>
    /// Claim type mappings (SAML attribute name -> claim type)
    /// </summary>
    public Dictionary<string, string> ClaimMappings { get; set; } = new();

    /// <summary>
    /// Whether this SP is enabled
    /// </summary>
    public bool Enabled { get; set; } = true;
}

/// <summary>
/// Certificate configuration options.
/// Supports multiple sources: file, base64, Windows store, and managed (database/Key Vault).
/// </summary>
public class CertificateOptions
{
    /// <summary>
    /// Path to certificate file (.pfx/.p12)
    /// </summary>
    public string? FilePath { get; set; }

    /// <summary>
    /// Password for certificate file
    /// </summary>
    public string? Password { get; set; }

    /// <summary>
    /// Base64 encoded certificate (PFX format with private key)
    /// </summary>
    public string? Base64 { get; set; }

    /// <summary>
    /// Certificate thumbprint (for Windows certificate store lookup)
    /// </summary>
    public string? Thumbprint { get; set; }

    /// <summary>
    /// Store name for certificate lookup
    /// </summary>
    public StoreName StoreName { get; set; } = StoreName.My;

    /// <summary>
    /// Store location for certificate lookup
    /// </summary>
    public StoreLocation StoreLocation { get; set; } = StoreLocation.CurrentUser;

    /// <summary>
    /// Use managed certificate from ICertificateService.
    /// When true, certificate is loaded from database/Key Vault via ICertificateService.
    /// </summary>
    public bool UseManaged { get; set; } = false;

    /// <summary>
    /// Purpose identifier for managed certificate lookup.
    /// Used with ICertificateService.GetCertificateAsync().
    /// Example: "saml-signing", "saml-encryption"
    /// </summary>
    public string? ManagedPurpose { get; set; }

    /// <summary>
    /// Entity ID for managed certificate lookup (optional).
    /// Allows different certificates per SAML SP/IdP entity.
    /// </summary>
    public string? ManagedEntityId { get; set; }

    /// <summary>
    /// Whether to auto-generate a self-signed certificate if none exists.
    /// Only applies when UseManaged=true.
    /// </summary>
    public bool AutoGenerateSelfSigned { get; set; } = true;

    /// <summary>
    /// Loads the certificate based on configuration (synchronous).
    /// For managed certificates, use LoadCertificateAsync() with ICertificateService instead.
    /// </summary>
    public X509Certificate2? LoadCertificate()
    {
        // Managed certificates require async loading with ICertificateService
        if (UseManaged)
        {
            return null;
        }

        // Use platform-appropriate flags (MachineKeySet not well-supported on macOS)
        var keyStorageFlags = OperatingSystem.IsMacOS()
            ? X509KeyStorageFlags.Exportable
            : X509KeyStorageFlags.MachineKeySet;

        if (!string.IsNullOrEmpty(FilePath))
        {
            return new X509Certificate2(FilePath, Password, keyStorageFlags);
        }

        if (!string.IsNullOrEmpty(Base64))
        {
            var bytes = Convert.FromBase64String(Base64);
            return new X509Certificate2(bytes, Password, keyStorageFlags);
        }

        if (!string.IsNullOrEmpty(Thumbprint))
        {
            using var store = new X509Store(StoreName, StoreLocation);
            store.Open(OpenFlags.ReadOnly);
            var certs = store.Certificates.Find(
                X509FindType.FindByThumbprint,
                Thumbprint,
                validOnly: false);
            return certs.Count > 0 ? certs[0] : null;
        }

        return null;
    }

    /// <summary>
    /// Gets whether this configuration points to a managed certificate.
    /// </summary>
    public bool IsManaged => UseManaged && !string.IsNullOrEmpty(ManagedPurpose);

    /// <summary>
    /// Gets whether any certificate source is configured.
    /// </summary>
    public bool IsConfigured =>
        UseManaged ||
        !string.IsNullOrEmpty(FilePath) ||
        !string.IsNullOrEmpty(Base64) ||
        !string.IsNullOrEmpty(Thumbprint);
}

/// <summary>
/// Extension methods for CertificateOptions to support async loading from ICertificateService.
/// </summary>
public static class CertificateOptionsExtensions
{
    /// <summary>
    /// Loads the certificate, supporting both static and managed configurations.
    /// </summary>
    /// <param name="options">Certificate options</param>
    /// <param name="certificateService">Certificate service for managed certificates</param>
    /// <param name="tenantId">Optional tenant ID for managed certificates</param>
    /// <param name="cancellationToken">Cancellation token</param>
    /// <returns>The loaded certificate, or null if not found/configured</returns>
    public static async Task<X509Certificate2?> LoadCertificateAsync(
        this CertificateOptions? options,
        Oluso.Core.Services.ICertificateService? certificateService,
        string? tenantId = null,
        CancellationToken cancellationToken = default)
    {
        if (options == null)
        {
            return null;
        }

        // If managed, use certificate service
        if (options.IsManaged && certificateService != null)
        {
            if (options.AutoGenerateSelfSigned)
            {
                return await certificateService.EnsureCertificateAsync(
                    options.ManagedPurpose!,
                    tenantId,
                    options.ManagedEntityId,
                    cancellationToken: cancellationToken);
            }
            else
            {
                return await certificateService.GetCertificateAsync(
                    options.ManagedPurpose!,
                    tenantId,
                    options.ManagedEntityId,
                    cancellationToken);
            }
        }

        // Otherwise use synchronous loading
        return options.LoadCertificate();
    }
}
