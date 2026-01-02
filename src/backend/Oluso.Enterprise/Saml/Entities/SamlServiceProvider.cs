using Oluso.Core.Domain.Entities;

namespace Oluso.Enterprise.Saml.Entities;

/// <summary>
/// SAML Service Provider - represents an application that uses this system as its SAML Identity Provider.
/// These are applications (like Salesforce, ServiceNow) that redirect users here for authentication.
/// Counterpart to IdentityProvider: IdentityProvider = external IdPs we authenticate FROM,
/// SamlServiceProvider = external SPs that authenticate TO us.
/// </summary>
public class SamlServiceProvider : TenantEntity
{
    public int Id { get; set; }

    /// <summary>
    /// SAML Entity ID - unique identifier for this SP (e.g., "https://salesforce.com/saml")
    /// </summary>
    public string EntityId { get; set; } = string.Empty;

    /// <summary>
    /// Display name shown in admin UI
    /// </summary>
    public string? DisplayName { get; set; }

    /// <summary>
    /// Description of this SP
    /// </summary>
    public string? Description { get; set; }

    /// <summary>
    /// Whether this SP is enabled
    /// </summary>
    public bool Enabled { get; set; } = true;

    /// <summary>
    /// URL to SP metadata for auto-configuration
    /// </summary>
    public string? MetadataUrl { get; set; }

    /// <summary>
    /// Assertion Consumer Service URL (where to POST SAML responses)
    /// </summary>
    public string? AssertionConsumerServiceUrl { get; set; }

    /// <summary>
    /// Single Logout Service URL
    /// </summary>
    public string? SingleLogoutServiceUrl { get; set; }

    /// <summary>
    /// SP's signing certificate (Base64 encoded, public key only)
    /// Used to verify signed AuthnRequests from SP
    /// </summary>
    public string? SigningCertificate { get; set; }

    /// <summary>
    /// SP's encryption certificate (Base64 encoded, public key only)
    /// Used to encrypt assertions sent to SP
    /// </summary>
    public string? EncryptionCertificate { get; set; }

    /// <summary>
    /// Whether to encrypt assertions for this SP
    /// </summary>
    public bool EncryptAssertions { get; set; }

    /// <summary>
    /// Name ID format (e.g., emailAddress, persistent, transient)
    /// </summary>
    public string? NameIdFormat { get; set; }

    /// <summary>
    /// JSON array of allowed claim types to include in assertions
    /// </summary>
    public string? AllowedClaimsJson { get; set; }

    /// <summary>
    /// JSON object for claim type mappings
    /// </summary>
    public string? ClaimMappingsJson { get; set; }

    /// <summary>
    /// Binding preference for SSO (Redirect, POST)
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
    public bool RequireSignedAuthnRequests { get; set; }

    /// <summary>
    /// Default RelayState to use if SP doesn't provide one
    /// </summary>
    public string? DefaultRelayState { get; set; }

    /// <summary>
    /// Additional properties (JSON)
    /// </summary>
    public string? PropertiesJson { get; set; }

    /// <summary>
    /// Whether this record is non-editable (system-managed)
    /// </summary>
    public bool NonEditable { get; set; }

    public DateTime Created { get; set; } = DateTime.UtcNow;
    public DateTime? Updated { get; set; }
    public DateTime? LastAccessed { get; set; }
}
