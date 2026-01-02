using Microsoft.IdentityModel.Tokens;
using Oluso.Core.Domain.Entities;

namespace Oluso.Core.Services;

/// <summary>
/// Service for managing signing keys including generation, rotation, and JWKS.
/// Uses pluggable key material providers (local, Azure Key Vault, AWS KMS, etc.)
/// Private keys never leave the provider - they are NEVER exposed in API responses.
/// </summary>
public interface ISigningKeyService
{
    /// <summary>
    /// Generate a new signing key using the configured provider
    /// </summary>
    Task<SigningKey> GenerateKeyAsync(GenerateKeyRequest request, CancellationToken cancellationToken = default);

    /// <summary>
    /// Get signing credentials for token generation (private key stays in provider)
    /// </summary>
    Task<SigningCredentials?> GetSigningCredentialsAsync(string? clientId = null, CancellationToken cancellationToken = default);

    /// <summary>
    /// Get validation keys (public only) for token verification
    /// </summary>
    Task<IEnumerable<SecurityKey>> GetValidationKeysAsync(string? clientId = null, CancellationToken cancellationToken = default);

    /// <summary>
    /// Get JWKS for discovery endpoint (public keys only)
    /// </summary>
    Task<JsonWebKeySet> GetJwksAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// Rotate keys using configured provider
    /// </summary>
    Task RotateKeysAsync(string? clientId = null, CancellationToken cancellationToken = default);

    /// <summary>
    /// Revoke a key (also revokes in provider if applicable)
    /// </summary>
    Task RevokeKeyAsync(string keyId, string reason, CancellationToken cancellationToken = default);

    /// <summary>
    /// Process automatic key rotation for expiring keys
    /// </summary>
    Task ProcessScheduledRotationsAsync(CancellationToken cancellationToken = default);
}

/// <summary>
/// Request to generate a new signing key
/// </summary>
public class GenerateKeyRequest
{
    public string? TenantId { get; set; }
    public string? ClientId { get; set; }
    public string? Name { get; set; }
    public SigningKeyType KeyType { get; set; } = SigningKeyType.RSA;
    public string? Algorithm { get; set; }
    public int? KeySize { get; set; }
    public SigningKeyUse? Use { get; set; }
    public int? LifetimeDays { get; set; }
    public DateTime? ExpiresAt { get; set; }
    public DateTime? ActivateAt { get; set; }
    public bool ActivateImmediately { get; set; } = true;
    public int? Priority { get; set; }
    public KeyStorageProvider? StorageProvider { get; set; }
}

/// <summary>
/// Interface for key encryption at rest (used by local provider)
/// </summary>
public interface IKeyEncryptionService
{
    /// <summary>
    /// Encrypt a string and return encrypted string
    /// </summary>
    Task<string> EncryptAsync(string data, CancellationToken cancellationToken = default);

    /// <summary>
    /// Decrypt an encrypted string
    /// </summary>
    Task<string> DecryptAsync(string encryptedData, CancellationToken cancellationToken = default);

    /// <summary>
    /// Encrypt bytes and return as base64 string
    /// </summary>
    string Encrypt(byte[] data);

    /// <summary>
    /// Decrypt base64-encoded encrypted data to bytes
    /// </summary>
    byte[] Decrypt(string encryptedData);
}
