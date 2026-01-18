namespace Oluso.Core.Domain.Entities;

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
