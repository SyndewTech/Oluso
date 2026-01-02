using System.Text.Json.Serialization;
using Microsoft.IdentityModel.Tokens;
using Oluso.Core.Domain.Entities;

namespace Oluso.Core.Services;

/// <summary>
/// Abstraction for key material providers (local, Key Vault, KMS, etc.)
/// Private key material never leaves the provider - signing happens inside.
/// </summary>
public interface IKeyMaterialProvider
{
    /// <summary>
    /// The storage provider type this implementation supports
    /// </summary>
    KeyStorageProvider ProviderType { get; }

    /// <summary>
    /// Generate a new key and store it securely
    /// </summary>
    /// <param name="request">Key generation parameters</param>
    /// <param name="cancellationToken">Cancellation token</param>
    /// <returns>Key metadata (no private key material)</returns>
    Task<KeyMaterialResult> GenerateKeyAsync(
        KeyGenerationParams request,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Get signing credentials for a key (private key never exposed)
    /// </summary>
    /// <param name="key">The signing key metadata</param>
    /// <param name="cancellationToken">Cancellation token</param>
    /// <returns>Signing credentials that can sign without exposing key material</returns>
    Task<SigningCredentials?> GetSigningCredentialsAsync(
        SigningKey key,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Get the public key for verification/JWKS
    /// </summary>
    /// <param name="key">The signing key metadata</param>
    /// <param name="cancellationToken">Cancellation token</param>
    /// <returns>Security key (public only) for verification</returns>
    Task<SecurityKey?> GetPublicKeyAsync(
        SigningKey key,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Get JSON Web Key for JWKS endpoint
    /// </summary>
    /// <param name="key">The signing key metadata</param>
    /// <param name="cancellationToken">Cancellation token</param>
    /// <returns>JWK representation of public key</returns>
    Task<JsonWebKey?> GetJsonWebKeyAsync(
        SigningKey key,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Delete/destroy a key from the provider
    /// </summary>
    /// <param name="key">The signing key to delete</param>
    /// <param name="cancellationToken">Cancellation token</param>
    Task DeleteKeyAsync(
        SigningKey key,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Check if the provider is available/configured
    /// </summary>
    Task<bool> IsAvailableAsync(CancellationToken cancellationToken = default);
}

/// <summary>
/// Parameters for key generation
/// </summary>
public class KeyGenerationParams
{
    public string? TenantId { get; set; }
    public string? ClientId { get; set; }
    public string? Name { get; set; }
    public SigningKeyType KeyType { get; set; } = SigningKeyType.RSA;
    public string Algorithm { get; set; } = "RS256";
    public int KeySize { get; set; } = 2048;
    public SigningKeyUse Use { get; set; } = SigningKeyUse.Signing;
    public int? LifetimeDays { get; set; }
    public DateTime? ExpiresAt { get; set; }
    public Dictionary<string, string>? Tags { get; set; }
}

/// <summary>
/// Result of key generation (no private key material)
/// </summary>
public class KeyMaterialResult
{
    /// <summary>
    /// Generated key ID (kid)
    /// </summary>
    public string KeyId { get; set; } = null!;

    /// <summary>
    /// Key Vault URI or provider-specific identifier
    /// </summary>
    public string? KeyVaultUri { get; set; }

    /// <summary>
    /// Public key data for JWKS (base64 encoded)
    /// </summary>
    public string PublicKeyData { get; set; } = null!;

    /// <summary>
    /// Encrypted private key reference (for local provider only)
    /// For cloud providers, this should be empty/null.
    /// SECURITY: Never expose in API responses.
    /// </summary>
    [JsonIgnore]
    public string? EncryptedPrivateKey { get; set; }

    /// <summary>
    /// X.509 certificate thumbprint if applicable
    /// </summary>
    public string? X5t { get; set; }

    /// <summary>
    /// X.509 certificate chain if applicable
    /// </summary>
    public string? X5c { get; set; }
}

/// <summary>
/// Registry for key material providers
/// </summary>
public interface IKeyMaterialProviderRegistry
{
    /// <summary>
    /// Get provider by type
    /// </summary>
    IKeyMaterialProvider? GetProvider(KeyStorageProvider providerType);

    /// <summary>
    /// Get the default provider
    /// </summary>
    IKeyMaterialProvider GetDefaultProvider();

    /// <summary>
    /// Get all available providers
    /// </summary>
    IEnumerable<IKeyMaterialProvider> GetAvailableProviders();
}
