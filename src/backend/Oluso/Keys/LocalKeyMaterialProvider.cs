using System.Security.Cryptography;
using Microsoft.Extensions.Logging;
using Microsoft.IdentityModel.Tokens;
using Oluso.Core.Domain.Entities;
using Oluso.Core.Services;

namespace Oluso.Keys;

/// <summary>
/// Local key material provider that stores encrypted keys in the database.
/// For production, consider using Azure Key Vault or similar HSM-backed solution.
/// </summary>
public class LocalKeyMaterialProvider : IKeyMaterialProvider
{
    private readonly IKeyEncryptionService _encryptionService;
    private readonly ILogger<LocalKeyMaterialProvider> _logger;

    public KeyStorageProvider ProviderType => KeyStorageProvider.Local;

    public LocalKeyMaterialProvider(
        IKeyEncryptionService encryptionService,
        ILogger<LocalKeyMaterialProvider> logger)
    {
        _encryptionService = encryptionService;
        _logger = logger;
    }

    public Task<bool> IsAvailableAsync(CancellationToken cancellationToken = default)
    {
        return Task.FromResult(true); // Local provider is always available
    }

    public async Task<KeyMaterialResult> GenerateKeyAsync(
        KeyGenerationParams request,
        CancellationToken cancellationToken = default)
    {
        _logger.LogInformation(
            "Generating local key: Type={KeyType}, Algorithm={Algorithm}, Size={KeySize}",
            request.KeyType, request.Algorithm, request.KeySize);

        var (privateKey, publicKey) = request.KeyType switch
        {
            SigningKeyType.RSA => GenerateRsaKey(request.KeySize),
            SigningKeyType.EC => GenerateEcKey(request.Algorithm),
            SigningKeyType.Symmetric => GenerateSymmetricKey(request.KeySize),
            _ => throw new ArgumentException($"Unsupported key type: {request.KeyType}")
        };

        var keyId = GenerateKeyId();
        var encryptedPrivateKey = await _encryptionService.EncryptAsync(privateKey, cancellationToken);

        return new KeyMaterialResult
        {
            KeyId = keyId,
            KeyVaultUri = null, // Local keys don't have a vault URI
            PublicKeyData = publicKey,
            EncryptedPrivateKey = encryptedPrivateKey
        };
    }

    public async Task<SigningCredentials?> GetSigningCredentialsAsync(
        SigningKey key,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrEmpty(key.PrivateKeyData))
        {
            _logger.LogWarning("No private key data for key {KeyId}", key.KeyId);
            return null;
        }

        try
        {
            var decryptedPrivateKey = await _encryptionService.DecryptAsync(key.PrivateKeyData, cancellationToken);
            var privateKeyBytes = Convert.FromBase64String(decryptedPrivateKey);

            SecurityKey securityKey = key.KeyType switch
            {
                SigningKeyType.RSA => CreateRsaSecurityKey(privateKeyBytes, key.KeyId),
                SigningKeyType.EC => CreateEcSecurityKey(privateKeyBytes, key.KeyId),
                SigningKeyType.Symmetric => new SymmetricSecurityKey(privateKeyBytes) { KeyId = key.KeyId },
                _ => throw new NotSupportedException($"Key type {key.KeyType} not supported")
            };

            return new SigningCredentials(securityKey, key.Algorithm);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to create signing credentials for key {KeyId}", key.KeyId);
            return null;
        }
    }

    public Task<SecurityKey?> GetPublicKeyAsync(
        SigningKey key,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrEmpty(key.PublicKeyData))
        {
            return Task.FromResult<SecurityKey?>(null);
        }

        try
        {
            var publicKeyBytes = Convert.FromBase64String(key.PublicKeyData);

            SecurityKey? securityKey = key.KeyType switch
            {
                SigningKeyType.RSA => CreateRsaSecurityKeyFromPublic(publicKeyBytes, key.KeyId),
                SigningKeyType.EC => CreateEcSecurityKeyFromPublic(publicKeyBytes, key.KeyId),
                SigningKeyType.Symmetric => new SymmetricSecurityKey(publicKeyBytes) { KeyId = key.KeyId },
                _ => null
            };

            return Task.FromResult(securityKey);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to create public key for {KeyId}", key.KeyId);
            return Task.FromResult<SecurityKey?>(null);
        }
    }

    public Task<JsonWebKey?> GetJsonWebKeyAsync(
        SigningKey key,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrEmpty(key.PublicKeyData))
        {
            return Task.FromResult<JsonWebKey?>(null);
        }

        try
        {
            var publicKeyBytes = Convert.FromBase64String(key.PublicKeyData);

            JsonWebKey jwk = key.KeyType switch
            {
                SigningKeyType.RSA => CreateRsaJsonWebKey(publicKeyBytes, key),
                SigningKeyType.EC => CreateEcJsonWebKey(publicKeyBytes, key),
                _ => throw new NotSupportedException($"Key type {key.KeyType} not supported for JWKS")
            };

            return Task.FromResult<JsonWebKey?>(jwk);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to create JWK for {KeyId}", key.KeyId);
            return Task.FromResult<JsonWebKey?>(null);
        }
    }

    public Task DeleteKeyAsync(
        SigningKey key,
        CancellationToken cancellationToken = default)
    {
        // For local keys, deletion is handled by the store
        // This is a no-op since the key material is stored encrypted in the database
        _logger.LogInformation("Local key deletion requested for {KeyId}", key.KeyId);
        return Task.CompletedTask;
    }

    #region Key Generation

    private static (string privateKey, string publicKey) GenerateRsaKey(int keySize)
    {
        using var rsa = RSA.Create(keySize);

        var privateKeyBytes = rsa.ExportRSAPrivateKey();
        var publicKeyBytes = rsa.ExportRSAPublicKey();

        return (Convert.ToBase64String(privateKeyBytes), Convert.ToBase64String(publicKeyBytes));
    }

    private static (string privateKey, string publicKey) GenerateEcKey(string algorithm)
    {
        var curve = algorithm switch
        {
            "ES256" => ECCurve.NamedCurves.nistP256,
            "ES384" => ECCurve.NamedCurves.nistP384,
            "ES512" => ECCurve.NamedCurves.nistP521,
            _ => ECCurve.NamedCurves.nistP256
        };

        using var ecdsa = ECDsa.Create(curve);

        var privateKeyBytes = ecdsa.ExportECPrivateKey();
        var publicKeyBytes = ecdsa.ExportSubjectPublicKeyInfo();

        return (Convert.ToBase64String(privateKeyBytes), Convert.ToBase64String(publicKeyBytes));
    }

    private static (string privateKey, string publicKey) GenerateSymmetricKey(int keySize)
    {
        var keyBytes = new byte[keySize / 8];
        RandomNumberGenerator.Fill(keyBytes);

        var key = Convert.ToBase64String(keyBytes);
        return (key, key); // Symmetric key is same for both
    }

    private static string GenerateKeyId()
    {
        var bytes = new byte[16];
        RandomNumberGenerator.Fill(bytes);
        return Convert.ToBase64String(bytes)
            .Replace('+', '-')
            .Replace('/', '_')
            .TrimEnd('=');
    }

    #endregion

    #region Security Key Creation

    private static RsaSecurityKey CreateRsaSecurityKey(byte[] privateKeyBytes, string keyId)
    {
        var rsa = RSA.Create();
        rsa.ImportRSAPrivateKey(privateKeyBytes, out _);
        return new RsaSecurityKey(rsa) { KeyId = keyId };
    }

    private static RsaSecurityKey CreateRsaSecurityKeyFromPublic(byte[] publicKeyBytes, string keyId)
    {
        var rsa = RSA.Create();
        rsa.ImportRSAPublicKey(publicKeyBytes, out _);
        return new RsaSecurityKey(rsa) { KeyId = keyId };
    }

    private static ECDsaSecurityKey CreateEcSecurityKey(byte[] privateKeyBytes, string keyId)
    {
        var ecdsa = ECDsa.Create();
        ecdsa.ImportECPrivateKey(privateKeyBytes, out _);
        return new ECDsaSecurityKey(ecdsa) { KeyId = keyId };
    }

    private static ECDsaSecurityKey CreateEcSecurityKeyFromPublic(byte[] publicKeyBytes, string keyId)
    {
        var ecdsa = ECDsa.Create();
        ecdsa.ImportSubjectPublicKeyInfo(publicKeyBytes, out _);
        return new ECDsaSecurityKey(ecdsa) { KeyId = keyId };
    }

    #endregion

    #region JWK Creation

    private static JsonWebKey CreateRsaJsonWebKey(byte[] publicKeyBytes, SigningKey key)
    {
        using var rsa = RSA.Create();
        rsa.ImportRSAPublicKey(publicKeyBytes, out _);

        var parameters = rsa.ExportParameters(false);

        return new JsonWebKey
        {
            Kty = "RSA",
            Use = key.Use == SigningKeyUse.Signing ? "sig" : "enc",
            Kid = key.KeyId,
            Alg = key.Algorithm,
            N = Base64UrlEncoder.Encode(parameters.Modulus!),
            E = Base64UrlEncoder.Encode(parameters.Exponent!),
            X5t = key.X5t
        };
    }

    private static JsonWebKey CreateEcJsonWebKey(byte[] publicKeyBytes, SigningKey key)
    {
        using var ecdsa = ECDsa.Create();
        ecdsa.ImportSubjectPublicKeyInfo(publicKeyBytes, out _);

        var parameters = ecdsa.ExportParameters(false);
        var curve = parameters.Curve.Oid?.FriendlyName switch
        {
            "nistP256" or "ECDSA_P256" => "P-256",
            "nistP384" or "ECDSA_P384" => "P-384",
            "nistP521" or "ECDSA_P521" => "P-521",
            _ => "P-256"
        };

        return new JsonWebKey
        {
            Kty = "EC",
            Use = key.Use == SigningKeyUse.Signing ? "sig" : "enc",
            Kid = key.KeyId,
            Alg = key.Algorithm,
            Crv = curve,
            X = Base64UrlEncoder.Encode(parameters.Q.X!),
            Y = Base64UrlEncoder.Encode(parameters.Q.Y!)
        };
    }

    #endregion
}
