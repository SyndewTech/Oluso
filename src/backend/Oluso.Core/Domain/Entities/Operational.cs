using System.Text.Json.Serialization;

namespace Oluso.Core.Domain.Entities;

/// <summary>
/// Persisted grant for tokens and codes
/// </summary>
public class PersistedGrant : TenantEntity
{
    public string Key { get; set; } = default!;
    public string Type { get; set; } = default!;
    public string? SubjectId { get; set; }
    public string? SessionId { get; set; }
    public string ClientId { get; set; } = default!;
    public string? Description { get; set; }
    public DateTime CreationTime { get; set; }
    public DateTime? Expiration { get; set; }
    public DateTime? ConsumedTime { get; set; }

    /// <summary>
    /// Serialized token/grant data. Contains sensitive information.
    /// SECURITY: Never expose this in API responses.
    /// </summary>
    [JsonIgnore]
    public string Data { get; set; } = default!;
}

/// <summary>
/// Device flow authorization code
/// </summary>
public class DeviceFlowCode : TenantEntity
{
    public string DeviceCode { get; set; } = default!;
    public string UserCode { get; set; } = default!;
    public string ClientId { get; set; } = default!;
    public string? SubjectId { get; set; }
    public string? SessionId { get; set; }
    public string? Description { get; set; }
    public DateTime CreationTime { get; set; }
    public DateTime Expiration { get; set; }
    public string Data { get; set; } = default!;
}

/// <summary>
/// User consent record
/// </summary>
public class Consent : TenantEntity
{
    public int Id { get; set; }
    public string SubjectId { get; set; } = default!;
    public string ClientId { get; set; } = default!;
    public string Scopes { get; set; } = default!;
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public DateTime? ExpiresAt { get; set; }

    public IEnumerable<string> GetScopes() =>
        string.IsNullOrEmpty(Scopes)
            ? Enumerable.Empty<string>()
            : Scopes.Split(' ', StringSplitOptions.RemoveEmptyEntries);

    public void SetScopes(IEnumerable<string> scopes) =>
        Scopes = string.Join(' ', scopes);
}

/// <summary>
/// Cryptographic signing key for JWT tokens.
/// Keys can be tenant-scoped (shared by all clients in tenant) or client-scoped.
/// Private key material is encrypted at rest or stored externally (Key Vault, KMS).
/// </summary>
public class SigningKey : TenantEntity
{
    public string Id { get; set; } = Guid.NewGuid().ToString("N");

    /// <summary>
    /// Human-friendly name for the key
    /// </summary>
    public string Name { get; set; } = default!;

    /// <summary>
    /// Key ID used in JWT header (kid claim)
    /// </summary>
    public string KeyId { get; set; } = default!;

    /// <summary>
    /// Type of key: RSA, EC, Symmetric
    /// </summary>
    public SigningKeyType KeyType { get; set; } = SigningKeyType.RSA;

    /// <summary>
    /// Algorithm for signing: RS256, RS384, RS512, ES256, ES384, ES512, HS256, etc.
    /// </summary>
    public string Algorithm { get; set; } = "RS256";

    /// <summary>
    /// Key use: Signing or Encryption
    /// </summary>
    public SigningKeyUse Use { get; set; } = SigningKeyUse.Signing;

    /// <summary>
    /// Key size in bits (2048, 4096 for RSA; 256, 384, 521 for EC)
    /// </summary>
    public int KeySize { get; set; } = 2048;

    /// <summary>
    /// Encrypted private key data (encrypted at rest).
    /// For cloud-backed keys, this may be empty as the key never leaves the provider.
    /// SECURITY: Never expose this in API responses - use DTOs that exclude this property.
    /// </summary>
    [JsonIgnore]
    public string PrivateKeyData { get; set; } = default!;

    /// <summary>
    /// Public key data for JWKS endpoint (base64 encoded).
    /// SECURITY: Use JWKS endpoint for public key exposure, not direct serialization.
    /// </summary>
    [JsonIgnore]
    public string PublicKeyData { get; set; } = default!;

    /// <summary>
    /// Key Vault or KMS URI if key is stored externally.
    /// Example: https://myvault.vault.azure.net/keys/mykey/version
    /// </summary>
    public string? KeyVaultUri { get; set; }

    /// <summary>
    /// Indicates the key storage provider
    /// </summary>
    public KeyStorageProvider StorageProvider { get; set; } = KeyStorageProvider.Local;

    /// <summary>
    /// Optional: specific client this key belongs to. If null, it's a tenant key.
    /// </summary>
    public string? ClientId { get; set; }

    /// <summary>
    /// Current status of the key
    /// </summary>
    public SigningKeyStatus Status { get; set; } = SigningKeyStatus.Active;

    /// <summary>
    /// When the key was created
    /// </summary>
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

    /// <summary>
    /// When the key becomes active (for scheduled rotation)
    /// </summary>
    public DateTime? ActivatedAt { get; set; }

    /// <summary>
    /// When the key expires and should no longer be used for signing
    /// </summary>
    public DateTime? ExpiresAt { get; set; }

    /// <summary>
    /// When the key was revoked (if applicable)
    /// </summary>
    public DateTime? RevokedAt { get; set; }

    /// <summary>
    /// Reason for revocation
    /// </summary>
    public string? RevocationReason { get; set; }

    /// <summary>
    /// When this key was last used for signing
    /// </summary>
    public DateTime? LastUsedAt { get; set; }

    /// <summary>
    /// Number of tokens signed with this key
    /// </summary>
    public long SignatureCount { get; set; }

    /// <summary>
    /// X.509 certificate thumbprint if key is from a certificate
    /// </summary>
    public string? X5t { get; set; }

    /// <summary>
    /// X.509 certificate thumbprint SHA-256 (for newer clients)
    /// </summary>
    public string? X5tS256 { get; set; }

    /// <summary>
    /// X.509 certificate chain (base64 encoded, comma-separated for chain)
    /// </summary>
    public string? X5c { get; set; }

    /// <summary>
    /// Certificate subject (CN, O, etc.)
    /// </summary>
    public string? CertificateSubject { get; set; }

    /// <summary>
    /// Certificate issuer
    /// </summary>
    public string? CertificateIssuer { get; set; }

    /// <summary>
    /// Certificate serial number
    /// </summary>
    public string? CertificateSerialNumber { get; set; }

    /// <summary>
    /// Certificate not valid before
    /// </summary>
    public DateTime? CertificateNotBefore { get; set; }

    /// <summary>
    /// Certificate not valid after
    /// </summary>
    public DateTime? CertificateNotAfter { get; set; }

    /// <summary>
    /// Whether this key has an associated X.509 certificate
    /// </summary>
    public bool HasCertificate => !string.IsNullOrEmpty(X5c);

    /// <summary>
    /// Priority for key selection (higher = preferred)
    /// </summary>
    public int Priority { get; set; } = 100;

    /// <summary>
    /// Whether this key should be included in JWKS
    /// </summary>
    public bool IncludeInJwks { get; set; } = true;

    /// <summary>
    /// Whether this key can be used for signing new tokens
    /// </summary>
    public bool CanSign => Status == SigningKeyStatus.Active &&
                           (ActivatedAt == null || ActivatedAt <= DateTime.UtcNow) &&
                           (ExpiresAt == null || ExpiresAt > DateTime.UtcNow);

    /// <summary>
    /// Whether this key can be used for verification (includes expired but not revoked)
    /// </summary>
    public bool CanVerify => Status != SigningKeyStatus.Revoked;

    /// <summary>
    /// Whether this key is expiring soon (within 7 days)
    /// </summary>
    public bool IsExpiringSoon => ExpiresAt.HasValue &&
                                  ExpiresAt.Value > DateTime.UtcNow &&
                                  ExpiresAt.Value <= DateTime.UtcNow.AddDays(7);

    /// <summary>
    /// Whether this key has expired
    /// </summary>
    public bool IsExpired => ExpiresAt.HasValue && ExpiresAt.Value <= DateTime.UtcNow;
}

public enum SigningKeyType
{
    RSA,
    EC,
    Symmetric
}

public enum SigningKeyUse
{
    Signing,
    Encryption
}

public enum SigningKeyStatus
{
    /// <summary>Key is pending activation</summary>
    Pending,
    /// <summary>Key is active and can be used for signing</summary>
    Active,
    /// <summary>Key is expired but can still be used for verification</summary>
    Expired,
    /// <summary>Key is revoked and should not be used</summary>
    Revoked,
    /// <summary>Key is archived (historical, not in JWKS)</summary>
    Archived
}

/// <summary>
/// Key storage provider types
/// </summary>
public enum KeyStorageProvider
{
    /// <summary>Keys stored locally (encrypted in database)</summary>
    Local,
    /// <summary>Keys stored in Azure Key Vault</summary>
    AzureKeyVault,
    /// <summary>Keys stored in AWS Key Management Service</summary>
    AwsKms,
    /// <summary>Keys stored in HashiCorp Vault</summary>
    HashiCorpVault,
    /// <summary>Keys stored in Google Cloud KMS</summary>
    GoogleCloudKms
}

/// <summary>
/// Configuration for automatic key rotation
/// </summary>
public class KeyRotationConfig : TenantEntity
{
    public string Id { get; set; } = Guid.NewGuid().ToString("N");

    /// <summary>Optional client-specific rotation config</summary>
    public string? ClientId { get; set; }

    /// <summary>Whether automatic rotation is enabled</summary>
    public bool Enabled { get; set; } = true;

    /// <summary>Key type to generate: RSA, EC</summary>
    public SigningKeyType KeyType { get; set; } = SigningKeyType.RSA;

    /// <summary>Algorithm for new keys</summary>
    public string Algorithm { get; set; } = "RS256";

    /// <summary>Key size for new keys</summary>
    public int KeySize { get; set; } = 2048;

    /// <summary>How long keys are valid (e.g., 90 days)</summary>
    public int KeyLifetimeDays { get; set; } = 90;

    /// <summary>How many days before expiration to generate new key</summary>
    public int RotationLeadDays { get; set; } = 14;

    /// <summary>How long expired keys remain in JWKS for verification</summary>
    public int GracePeriodDays { get; set; } = 30;

    /// <summary>Maximum number of keys to keep (including expired)</summary>
    public int MaxKeys { get; set; } = 5;

    /// <summary>When the last rotation occurred</summary>
    public DateTime? LastRotationAt { get; set; }

    /// <summary>When the next rotation is scheduled</summary>
    public DateTime? NextRotationAt { get; set; }

    /// <summary>Preferred storage provider for new keys</summary>
    public KeyStorageProvider PreferredStorageProvider { get; set; } = KeyStorageProvider.Local;
}

/// <summary>
/// Represents a Pushed Authorization Request (PAR) - RFC 9126
/// </summary>
public class PushedAuthorizationRequest : TenantEntity
{
    public long Id { get; set; }
    public string RequestUri { get; set; } = default!;
    public string ReferenceValueHash { get; set; } = default!;
    public string ClientId { get; set; } = default!;
    public string Parameters { get; set; } = default!;
    public DateTime CreationTime { get; set; }
    public DateTime ExpiresAtUtc { get; set; }
}

/// <summary>
/// Server-side session for user sessions
/// </summary>
public class ServerSideSession : TenantEntity
{
    public int Id { get; set; }
    public string Key { get; set; } = default!;
    public string Scheme { get; set; } = default!;
    public string SubjectId { get; set; } = default!;
    public string? SessionId { get; set; }
    public string? DisplayName { get; set; }
    public DateTime Created { get; set; }
    public DateTime Renewed { get; set; }
    public DateTime? Expires { get; set; }
    public string Data { get; set; } = default!;
}

/// <summary>
/// Persisted journey policy for authentication flows
/// </summary>
public class JourneyPolicyEntity : TenantEntity
{
    public string Id { get; set; } = default!;
    public string Name { get; set; } = default!;
    public string Type { get; set; } = default!;
    public bool Enabled { get; set; } = true;
    public int Priority { get; set; } = 100;
    public string? Description { get; set; }
    public int Version { get; set; } = 1;

    /// <summary>
    /// JSON-serialized steps array
    /// </summary>
    public string Steps { get; set; } = "[]";

    /// <summary>
    /// JSON-serialized conditions array
    /// </summary>
    public string? Conditions { get; set; }

    /// <summary>
    /// JSON-serialized output claims array
    /// </summary>
    public string? OutputClaims { get; set; }

    /// <summary>
    /// JSON-serialized session configuration
    /// </summary>
    public string? SessionConfig { get; set; }

    /// <summary>
    /// JSON-serialized UI configuration
    /// </summary>
    public string? UiConfig { get; set; }

    public int DefaultStepTimeoutSeconds { get; set; } = 300;
    public int MaxJourneyDurationMinutes { get; set; } = 30;

    /// <summary>
    /// Comma-separated tags
    /// </summary>
    public string? Tags { get; set; }

    /// <summary>
    /// Whether this journey requires authentication
    /// </summary>
    public bool RequiresAuthentication { get; set; } = true;

    /// <summary>
    /// Whether to persist collected data as submissions
    /// </summary>
    public bool PersistSubmissions { get; set; } = false;

    /// <summary>
    /// Collection name for storing submissions
    /// </summary>
    public string? SubmissionCollection { get; set; }

    /// <summary>
    /// Maximum submissions allowed (0 = unlimited)
    /// </summary>
    public int MaxSubmissions { get; set; } = 0;

    /// <summary>
    /// Whether to allow duplicate submissions
    /// </summary>
    public bool AllowDuplicates { get; set; } = false;

    /// <summary>
    /// Comma-separated fields for duplicate detection
    /// </summary>
    public string? DuplicateCheckFields { get; set; }

    /// <summary>
    /// Redirect URL after successful submission
    /// </summary>
    public string? SuccessRedirectUrl { get; set; }

    /// <summary>
    /// Success message to display after submission
    /// </summary>
    public string? SuccessMessage { get; set; }

    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public DateTime UpdatedAt { get; set; } = DateTime.UtcNow;
}

/// <summary>
/// Persisted journey state for in-progress authentication flows
/// </summary>
public class JourneyStateEntity : TenantEntity
{
    public string Id { get; set; } = default!;
    public string ClientId { get; set; } = default!;
    public string? UserId { get; set; }
    public string PolicyId { get; set; } = default!;
    public string CurrentStepId { get; set; } = default!;
    public string Status { get; set; } = default!;
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public DateTime? ExpiresAt { get; set; }

    /// <summary>
    /// JSON-serialized journey data
    /// </summary>
    public string? Data { get; set; }

    /// <summary>
    /// JSON-serialized claims bag
    /// </summary>
    public string? ClaimsBag { get; set; }

    public string? SessionId { get; set; }
    public string? AuthenticatedUserId { get; set; }
    public string? CorrelationId { get; set; }
    public string? CallbackUrl { get; set; }
}

/// <summary>
/// FIDO2/WebAuthn credential stored for passkey authentication
/// </summary>
public class Fido2CredentialEntity : TenantEntity
{
    public string Id { get; set; } = Guid.NewGuid().ToString();

    /// <summary>
    /// User ID who owns this credential
    /// </summary>
    public string UserId { get; set; } = default!;

    /// <summary>
    /// Base64URL-encoded credential ID from the authenticator
    /// </summary>
    public string CredentialId { get; set; } = default!;

    /// <summary>
    /// Base64URL-encoded public key for verification
    /// </summary>
    public string PublicKey { get; set; } = default!;

    /// <summary>
    /// Base64URL-encoded user handle
    /// </summary>
    public string UserHandle { get; set; } = default!;

    /// <summary>
    /// Signature counter for replay attack prevention
    /// </summary>
    public uint SignatureCounter { get; set; }

    /// <summary>
    /// COSE credential type (e.g., -7 for ES256)
    /// </summary>
    public int CredentialType { get; set; }

    /// <summary>
    /// Authenticator AAGUID
    /// </summary>
    public Guid AaGuid { get; set; }

    /// <summary>
    /// User-friendly name for this credential
    /// </summary>
    public string? DisplayName { get; set; }

    /// <summary>
    /// Type of authenticator: Platform or CrossPlatform
    /// </summary>
    public Fido2AuthenticatorType AuthenticatorType { get; set; }

    /// <summary>
    /// Attestation format (none, packed, tpm, android-key, etc.)
    /// </summary>
    public string? AttestationFormat { get; set; }

    /// <summary>
    /// Whether this is a discoverable credential (passkey)
    /// </summary>
    public bool IsDiscoverable { get; set; }

    /// <summary>
    /// Comma-separated list of transports (usb, nfc, ble, internal, hybrid)
    /// </summary>
    public string? Transports { get; set; }

    /// <summary>
    /// Whether this credential is active
    /// </summary>
    public bool IsActive { get; set; } = true;

    /// <summary>
    /// When this credential was registered
    /// </summary>
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

    /// <summary>
    /// When this credential was last used for authentication
    /// </summary>
    public DateTime? LastUsedAt { get; set; }

    /// <summary>
    /// Gets the transports as a list
    /// </summary>
    public List<string> GetTransports() =>
        string.IsNullOrEmpty(Transports)
            ? new List<string>()
            : Transports.Split(',', StringSplitOptions.RemoveEmptyEntries).ToList();

    /// <summary>
    /// Sets the transports from a list
    /// </summary>
    public void SetTransports(IEnumerable<string>? transports) =>
        Transports = transports != null ? string.Join(',', transports) : null;
}

public enum Fido2AuthenticatorType
{
    /// <summary>Built-in authenticator (Touch ID, Face ID, Windows Hello)</summary>
    Platform,
    /// <summary>External authenticator (USB security key, NFC, etc.)</summary>
    CrossPlatform
}

/// <summary>
/// Plugin metadata stored in database.
/// The actual WASM bytes are stored in file storage (local, Azure Blob, S3, etc.)
/// </summary>
public class PluginMetadata : TenantEntity
{
    /// <summary>
    /// Unique plugin identifier
    /// </summary>
    public string Id { get; set; } = Guid.NewGuid().ToString("N");

    /// <summary>
    /// Plugin name (used as identifier)
    /// </summary>
    public string Name { get; set; } = null!;

    /// <summary>
    /// Human-friendly display name
    /// </summary>
    public string? DisplayName { get; set; }

    /// <summary>
    /// Description of what the plugin does
    /// </summary>
    public string? Description { get; set; }

    /// <summary>
    /// Plugin version (semver format recommended)
    /// </summary>
    public string? Version { get; set; }

    /// <summary>
    /// Plugin author
    /// </summary>
    public string? Author { get; set; }

    /// <summary>
    /// Plugin scope: Global or Tenant-specific
    /// </summary>
    public PluginScope Scope { get; set; }

    /// <summary>
    /// Reference to the file in storage (path, blob URI, etc.)
    /// </summary>
    public string StorageReference { get; set; } = null!;

    /// <summary>
    /// Storage provider type
    /// </summary>
    public FileStorageProvider StorageProvider { get; set; } = FileStorageProvider.Local;

    /// <summary>
    /// Size of the plugin in bytes
    /// </summary>
    public long SizeBytes { get; set; }

    /// <summary>
    /// SHA256 hash of the plugin bytes for integrity verification
    /// </summary>
    public string? ContentHash { get; set; }

    /// <summary>
    /// Whether the plugin is enabled
    /// </summary>
    public bool Enabled { get; set; } = true;

    /// <summary>
    /// JSON-serialized list of required input claims
    /// </summary>
    public string? RequiredClaims { get; set; }

    /// <summary>
    /// JSON-serialized list of output claims
    /// </summary>
    public string? OutputClaims { get; set; }

    /// <summary>
    /// JSON-serialized configuration schema (JSON Schema format)
    /// </summary>
    public string? ConfigSchema { get; set; }

    /// <summary>
    /// JSON-serialized default configuration values
    /// </summary>
    public string? DefaultConfig { get; set; }

    /// <summary>
    /// Plugin type: Step, ClaimsTransform, Validator, Condition
    /// </summary>
    public PluginType Type { get; set; } = PluginType.Step;

    /// <summary>
    /// Tags for categorization (comma-separated)
    /// </summary>
    public string? Tags { get; set; }

    /// <summary>
    /// When the plugin was uploaded
    /// </summary>
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

    /// <summary>
    /// When the plugin was last updated
    /// </summary>
    public DateTime? UpdatedAt { get; set; }

    /// <summary>
    /// Who uploaded/created the plugin
    /// </summary>
    public string? CreatedBy { get; set; }

    /// <summary>
    /// Who last updated the plugin
    /// </summary>
    public string? UpdatedBy { get; set; }

    /// <summary>
    /// Number of times this plugin has been executed
    /// </summary>
    public long ExecutionCount { get; set; }

    /// <summary>
    /// Last time this plugin was executed
    /// </summary>
    public DateTime? LastExecutedAt { get; set; }

    /// <summary>
    /// Average execution time in milliseconds
    /// </summary>
    public double? AverageExecutionMs { get; set; }

    /// <summary>
    /// Gets the required claims as a list
    /// </summary>
    public List<string> GetRequiredClaimsList()
    {
        if (string.IsNullOrEmpty(RequiredClaims)) return new List<string>();
        return System.Text.Json.JsonSerializer.Deserialize<List<string>>(RequiredClaims) ?? new List<string>();
    }

    /// <summary>
    /// Sets the required claims from a list
    /// </summary>
    public void SetRequiredClaimsList(List<string>? claims)
    {
        RequiredClaims = claims != null ? System.Text.Json.JsonSerializer.Serialize(claims) : null;
    }

    /// <summary>
    /// Gets the output claims as a list
    /// </summary>
    public List<string> GetOutputClaimsList()
    {
        if (string.IsNullOrEmpty(OutputClaims)) return new List<string>();
        return System.Text.Json.JsonSerializer.Deserialize<List<string>>(OutputClaims) ?? new List<string>();
    }

    /// <summary>
    /// Sets the output claims from a list
    /// </summary>
    public void SetOutputClaimsList(List<string>? claims)
    {
        OutputClaims = claims != null ? System.Text.Json.JsonSerializer.Serialize(claims) : null;
    }

    /// <summary>
    /// Gets the tags as a list
    /// </summary>
    public List<string> GetTagsList() =>
        string.IsNullOrEmpty(Tags)
            ? new List<string>()
            : Tags.Split(',', StringSplitOptions.RemoveEmptyEntries).ToList();

    /// <summary>
    /// Sets the tags from a list
    /// </summary>
    public void SetTagsList(IEnumerable<string>? tags) =>
        Tags = tags != null ? string.Join(',', tags) : null;

    /// <summary>
    /// Gets the config schema as a dictionary
    /// </summary>
    public Dictionary<string, object>? GetConfigSchemaObject()
    {
        if (string.IsNullOrEmpty(ConfigSchema)) return null;
        return System.Text.Json.JsonSerializer.Deserialize<Dictionary<string, object>>(ConfigSchema);
    }

    /// <summary>
    /// Sets the config schema from a dictionary
    /// </summary>
    public void SetConfigSchemaObject(Dictionary<string, object>? schema)
    {
        ConfigSchema = schema != null ? System.Text.Json.JsonSerializer.Serialize(schema) : null;
    }

    /// <summary>
    /// Gets the default config as a dictionary
    /// </summary>
    public Dictionary<string, object>? GetDefaultConfigObject()
    {
        if (string.IsNullOrEmpty(DefaultConfig)) return null;
        return System.Text.Json.JsonSerializer.Deserialize<Dictionary<string, object>>(DefaultConfig);
    }

    /// <summary>
    /// Sets the default config from a dictionary
    /// </summary>
    public void SetDefaultConfigObject(Dictionary<string, object>? config)
    {
        DefaultConfig = config != null ? System.Text.Json.JsonSerializer.Serialize(config) : null;
    }
}

/// <summary>
/// Plugin scope
/// </summary>
public enum PluginScope
{
    /// <summary>Available to all tenants</summary>
    Global,
    /// <summary>Available only to specific tenant</summary>
    Tenant
}

/// <summary>
/// Plugin type
/// </summary>
public enum PluginType
{
    /// <summary>Full step implementation</summary>
    Step,
    /// <summary>Claims transformation only</summary>
    ClaimsTransform,
    /// <summary>Validation only</summary>
    Validator,
    /// <summary>Condition evaluation only</summary>
    Condition
}

/// <summary>
/// File storage provider types
/// </summary>
public enum FileStorageProvider
{
    /// <summary>Local file system</summary>
    Local,
    /// <summary>Azure Blob Storage</summary>
    AzureBlob,
    /// <summary>Amazon S3</summary>
    AwsS3,
    /// <summary>Google Cloud Storage</summary>
    GoogleCloudStorage,
    /// <summary>Database (for small files)</summary>
    Database
}

/// <summary>
/// Entity for storing journey submissions (data collection journeys like waitlists, surveys)
/// </summary>
public class JourneySubmissionEntity : TenantEntity
{
    /// <summary>
    /// Unique submission ID
    /// </summary>
    public string Id { get; set; } = Guid.NewGuid().ToString();

    /// <summary>
    /// Policy ID that created this submission
    /// </summary>
    public string PolicyId { get; set; } = default!;

    /// <summary>
    /// Policy name at time of submission
    /// </summary>
    public string? PolicyName { get; set; }

    /// <summary>
    /// Journey ID that created this submission
    /// </summary>
    public string? JourneyId { get; set; }

    /// <summary>
    /// JSON-serialized collected data
    /// </summary>
    public string Data { get; set; } = "{}";

    /// <summary>
    /// IP address of submitter
    /// </summary>
    public string? IpAddress { get; set; }

    /// <summary>
    /// User agent string
    /// </summary>
    public string? UserAgent { get; set; }

    /// <summary>
    /// Referrer URL
    /// </summary>
    public string? Referrer { get; set; }

    /// <summary>
    /// JSON-serialized UTM parameters
    /// </summary>
    public string? UtmParameters { get; set; }

    /// <summary>
    /// Country detected from IP
    /// </summary>
    public string? Country { get; set; }

    /// <summary>
    /// Browser locale
    /// </summary>
    public string? Locale { get; set; }

    /// <summary>
    /// Status of the submission
    /// </summary>
    public string Status { get; set; } = "New";

    /// <summary>
    /// Notes or comments on the submission
    /// </summary>
    public string? Notes { get; set; }

    /// <summary>
    /// Comma-separated tags for categorization
    /// </summary>
    public string? Tags { get; set; }

    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public DateTime? ReviewedAt { get; set; }
    public string? ReviewedBy { get; set; }
}
