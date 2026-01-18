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
