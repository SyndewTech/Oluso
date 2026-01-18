using System.Text.Json.Serialization;

namespace Oluso.Core.Domain.Entities;

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
