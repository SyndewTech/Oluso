namespace Oluso.Enterprise.Fido2.Configuration;

/// <summary>
/// Configuration options for FIDO2/WebAuthn
/// </summary>
public class Fido2Options
{
    public const string SectionName = "Oluso:Fido2";

    /// <summary>
    /// The relying party ID (typically the domain name, e.g., "example.com")
    /// </summary>
    public string RelyingPartyId { get; set; } = null!;

    /// <summary>
    /// The relying party name displayed to users
    /// </summary>
    public string RelyingPartyName { get; set; } = null!;

    /// <summary>
    /// The relying party icon URL (optional)
    /// </summary>
    public string? RelyingPartyIcon { get; set; }

    /// <summary>
    /// Allowed origins for WebAuthn requests (e.g., "https://example.com")
    /// </summary>
    public HashSet<string> Origins { get; set; } = new();

    /// <summary>
    /// Timeout for WebAuthn ceremonies in milliseconds
    /// </summary>
    public uint Timeout { get; set; } = 60000;

    /// <summary>
    /// Attestation conveyance preference: none, indirect, direct, enterprise
    /// </summary>
    public string AttestationConveyancePreference { get; set; } = "none";

    /// <summary>
    /// User verification requirement: required, preferred, discouraged
    /// </summary>
    public string UserVerificationRequirement { get; set; } = "preferred";

    /// <summary>
    /// Authenticator attachment preference: platform, cross-platform, or null for any
    /// </summary>
    public string? AuthenticatorAttachment { get; set; }

    /// <summary>
    /// Resident key (discoverable credential) requirement: required, preferred, discouraged
    /// </summary>
    public string ResidentKeyRequirement { get; set; } = "preferred";

    /// <summary>
    /// Whether to store attestation data for enterprise scenarios
    /// </summary>
    public bool StoreAttestationData { get; set; } = false;

    /// <summary>
    /// Maximum number of credentials per user
    /// </summary>
    public int MaxCredentialsPerUser { get; set; } = 10;

    /// <summary>
    /// Metadata service configuration for attestation verification
    /// </summary>
    public MetadataServiceOptions? MetadataService { get; set; }
}

/// <summary>
/// Options for FIDO Alliance Metadata Service (MDS)
/// </summary>
public class MetadataServiceOptions
{
    /// <summary>
    /// Whether to enable metadata service for attestation verification
    /// </summary>
    public bool Enabled { get; set; } = false;

    /// <summary>
    /// Path to cached metadata blob (optional)
    /// </summary>
    public string? CachePath { get; set; }

    /// <summary>
    /// How often to refresh metadata (in hours)
    /// </summary>
    public int RefreshIntervalHours { get; set; } = 24;
}
