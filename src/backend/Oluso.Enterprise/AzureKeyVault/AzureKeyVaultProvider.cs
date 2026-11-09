using System.Security.Cryptography;
using Azure.Identity;
using Azure.Security.KeyVault.Keys;
using Azure.Security.KeyVault.Keys.Cryptography;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using Microsoft.IdentityModel.Tokens;
using Oluso.Core.Domain.Entities;
using Oluso.Core.Services;
using JsonWebKey = Microsoft.IdentityModel.Tokens.JsonWebKey;

namespace Oluso.Enterprise.AzureKeyVault;

/// <summary>
/// Azure Key Vault implementation of IKeyMaterialProvider.
/// Private keys never leave Key Vault - signing is performed by the vault.
/// </summary>
public class AzureKeyVaultProvider : IKeyMaterialProvider
{
    private readonly KeyClient? _keyClient;
    private readonly IConfiguration _configuration;
    private readonly ILogger<AzureKeyVaultProvider> _logger;
    private readonly string? _vaultUri;
    private readonly bool _isConfigured;

    public KeyStorageProvider ProviderType => KeyStorageProvider.AzureKeyVault;

    public AzureKeyVaultProvider(
        IConfiguration configuration,
        ILogger<AzureKeyVaultProvider> logger)
    {
        _configuration = configuration;
        _logger = logger;

        _vaultUri = configuration["Oluso:AzureKeyVault:VaultUri"];
        _isConfigured = !string.IsNullOrEmpty(_vaultUri);

        if (_isConfigured)
        {
            try
            {
                // Use DefaultAzureCredential for flexible authentication
                // Supports: Managed Identity, Azure CLI, Environment Variables, etc.
                var credential = new DefaultAzureCredential(new DefaultAzureCredentialOptions
                {
                    ExcludeEnvironmentCredential = false,
                    ExcludeManagedIdentityCredential = false,
                    ExcludeAzureCliCredential = false,
                    ExcludeVisualStudioCredential = true,
                    ExcludeVisualStudioCodeCredential = true,
                    ExcludeInteractiveBrowserCredential = true
                });

                _keyClient = new KeyClient(new Uri(_vaultUri!), credential);
                _logger.LogInformation("Azure Key Vault provider initialized: {VaultUri}", _vaultUri);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to initialize Azure Key Vault client");
                _isConfigured = false;
            }
        }
        else
        {
            _logger.LogWarning("Azure Key Vault not configured - provider unavailable");
        }
    }

    public Task<bool> IsAvailableAsync(CancellationToken cancellationToken = default)
    {
        return Task.FromResult(_isConfigured && _keyClient != null);
    }

    public async Task<KeyMaterialResult> GenerateKeyAsync(
        KeyGenerationParams request,
        CancellationToken cancellationToken = default)
    {
        if (_keyClient == null)
        {
            throw new InvalidOperationException("Azure Key Vault is not configured");
        }

        // Create a unique key name for the vault
        var keyName = GenerateKeyVaultKeyName(request.TenantId, request.ClientId, request.Name);

        _logger.LogInformation(
            "Creating key in Azure Key Vault: {KeyName}, Type: {KeyType}, Size: {KeySize}",
            keyName, request.KeyType, request.KeySize);

        // Create the key in Key Vault
        KeyVaultKey key;
        if (request.KeyType == SigningKeyType.RSA)
        {
            var options = new CreateRsaKeyOptions(keyName)
            {
                KeySize = request.KeySize,
                KeyOperations = { KeyOperation.Sign, KeyOperation.Verify },
                Enabled = true
            };

            // Set expiration if specified
            if (request.ExpiresAt.HasValue)
            {
                options.ExpiresOn = request.ExpiresAt.Value;
            }
            else if (request.LifetimeDays.HasValue)
            {
                options.ExpiresOn = DateTimeOffset.UtcNow.AddDays(request.LifetimeDays.Value);
            }

            // Add tags for organization
            options.Tags["tenantId"] = request.TenantId ?? "global";
            options.Tags["clientId"] = request.ClientId ?? "tenant-default";
            options.Tags["createdBy"] = "Oluso";
            options.Tags["algorithm"] = request.Algorithm;

            if (request.Tags != null)
            {
                foreach (var tag in request.Tags)
                {
                    options.Tags[tag.Key] = tag.Value;
                }
            }

            key = await _keyClient.CreateRsaKeyAsync(options, cancellationToken);
        }
        else if (request.KeyType == SigningKeyType.EC)
        {
            var curveName = GetEcCurveName(request.Algorithm);
            var options = new CreateEcKeyOptions(keyName)
            {
                CurveName = curveName,
                KeyOperations = { KeyOperation.Sign, KeyOperation.Verify },
                Enabled = true
            };

            // Set expiration if specified
            if (request.ExpiresAt.HasValue)
            {
                options.ExpiresOn = request.ExpiresAt.Value;
            }
            else if (request.LifetimeDays.HasValue)
            {
                options.ExpiresOn = DateTimeOffset.UtcNow.AddDays(request.LifetimeDays.Value);
            }

            // Add tags
            options.Tags["tenantId"] = request.TenantId ?? "global";
            options.Tags["clientId"] = request.ClientId ?? "tenant-default";
            options.Tags["createdBy"] = "Oluso";
            options.Tags["algorithm"] = request.Algorithm;

            key = await _keyClient.CreateEcKeyAsync(options, cancellationToken);
        }
        else
        {
            throw new ArgumentException($"Unsupported key type for Key Vault: {request.KeyType}");
        }

        _logger.LogInformation(
            "Created key in Azure Key Vault: {KeyId}, Version: {Version}",
            key.Id, key.Properties.Version);

        // Extract public key for JWKS
        var publicKeyData = ExtractPublicKeyData(key);

        return new KeyMaterialResult
        {
            KeyId = key.Properties.Version ?? key.Name,
            KeyVaultUri = key.Id.ToString(),
            PublicKeyData = publicKeyData,
            EncryptedPrivateKey = null // Key never leaves Key Vault
        };
    }

    public async Task<SigningCredentials?> GetSigningCredentialsAsync(
        SigningKey key,
        CancellationToken cancellationToken = default)
    {
        if (_keyClient == null || string.IsNullOrEmpty(key.KeyVaultUri))
        {
            return null;
        }

        try
        {
            // Create a CryptographyClient that will perform signing in Key Vault
            var keyUri = new Uri(key.KeyVaultUri);
            var cryptoClient = new CryptographyClient(keyUri, new DefaultAzureCredential());

            // Create a custom SecurityKey that uses Key Vault for signing
            var kvSecurityKey = new AzureKeyVaultSecurityKey(
                cryptoClient,
                key.KeyId,
                key.Algorithm,
                _logger);

            return new SigningCredentials(kvSecurityKey, key.Algorithm);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to get signing credentials from Key Vault: {KeyId}", key.KeyId);
            return null;
        }
    }

    public async Task<SecurityKey?> GetPublicKeyAsync(
        SigningKey key,
        CancellationToken cancellationToken = default)
    {
        if (_keyClient == null || string.IsNullOrEmpty(key.KeyVaultUri))
        {
            return null;
        }

        try
        {
            // Get the key from vault to extract public key
            var keyUri = new Uri(key.KeyVaultUri);
            var keyName = ExtractKeyNameFromUri(keyUri);
            var keyVersion = ExtractKeyVersionFromUri(keyUri);

            var kvKey = await _keyClient.GetKeyAsync(keyName, keyVersion, cancellationToken);
            return CreateSecurityKeyFromKeyVaultKey(kvKey.Value, key.KeyId);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to get public key from Key Vault: {KeyId}", key.KeyId);
            return null;
        }
    }

    public async Task<JsonWebKey?> GetJsonWebKeyAsync(
        SigningKey key,
        CancellationToken cancellationToken = default)
    {
        if (_keyClient == null || string.IsNullOrEmpty(key.KeyVaultUri))
        {
            return null;
        }

        try
        {
            var keyUri = new Uri(key.KeyVaultUri);
            var keyName = ExtractKeyNameFromUri(keyUri);
            var keyVersion = ExtractKeyVersionFromUri(keyUri);

            var kvKey = await _keyClient.GetKeyAsync(keyName, keyVersion, cancellationToken);
            return CreateJsonWebKeyFromKeyVaultKey(kvKey.Value, key);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to get JWK from Key Vault: {KeyId}", key.KeyId);
            return null;
        }
    }

    public async Task DeleteKeyAsync(
        SigningKey key,
        CancellationToken cancellationToken = default)
    {
        if (_keyClient == null || string.IsNullOrEmpty(key.KeyVaultUri))
        {
            return;
        }

        try
        {
            var keyUri = new Uri(key.KeyVaultUri);
            var keyName = ExtractKeyNameFromUri(keyUri);

            // Start deletion (Key Vault uses soft delete by default)
            var operation = await _keyClient.StartDeleteKeyAsync(keyName, cancellationToken);

            _logger.LogInformation("Deleted key from Azure Key Vault: {KeyName}", keyName);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to delete key from Key Vault: {KeyId}", key.KeyId);
            throw;
        }
    }

    #region Private Methods

    private static string GenerateKeyVaultKeyName(string? tenantId, string? clientId, string? name)
    {
        var baseName = name ?? "signing-key";
        var tenant = tenantId ?? "global";
        var client = clientId ?? "default";

        // Key Vault key names: alphanumeric and hyphens only
        var safeName = $"{tenant}-{client}-{baseName}-{DateTime.UtcNow:yyyyMMddHHmmss}"
            .Replace("_", "-")
            .Replace(" ", "-");

        // Max 127 characters
        if (safeName.Length > 127)
        {
            safeName = safeName[..127];
        }

        return safeName;
    }

    private static KeyCurveName GetEcCurveName(string algorithm)
    {
        return algorithm switch
        {
            "ES256" => KeyCurveName.P256,
            "ES384" => KeyCurveName.P384,
            "ES512" => KeyCurveName.P521,
            _ => KeyCurveName.P256
        };
    }

    private static string ExtractPublicKeyData(KeyVaultKey key)
    {
        if (key.Key.KeyType == KeyType.Rsa)
        {
            using var rsa = key.Key.ToRSA();
            var publicKey = rsa.ExportRSAPublicKey();
            return Convert.ToBase64String(publicKey);
        }
        else if (key.Key.KeyType == KeyType.Ec)
        {
            using var ecdsa = key.Key.ToECDsa();
            var publicKey = ecdsa.ExportSubjectPublicKeyInfo();
            return Convert.ToBase64String(publicKey);
        }

        throw new NotSupportedException($"Key type {key.Key.KeyType} not supported");
    }

    private static string ExtractKeyNameFromUri(Uri keyUri)
    {
        // Format: https://vault.vault.azure.net/keys/keyname/version
        var segments = keyUri.Segments;
        return segments.Length >= 3 ? segments[2].TrimEnd('/') : throw new ArgumentException("Invalid key URI");
    }

    private static string? ExtractKeyVersionFromUri(Uri keyUri)
    {
        var segments = keyUri.Segments;
        return segments.Length >= 4 ? segments[3].TrimEnd('/') : null;
    }

    private static SecurityKey CreateSecurityKeyFromKeyVaultKey(KeyVaultKey kvKey, string keyId)
    {
        if (kvKey.Key.KeyType == KeyType.Rsa)
        {
            // Pass RSA instance directly so RsaSecurityKey.Rsa is set (needed by JWKS endpoint)
            var rsa = kvKey.Key.ToRSA();
            return new RsaSecurityKey(rsa) { KeyId = keyId };
        }
        else if (kvKey.Key.KeyType == KeyType.Ec)
        {
            var ecdsa = kvKey.Key.ToECDsa();
            return new ECDsaSecurityKey(ecdsa) { KeyId = keyId };
        }

        throw new NotSupportedException($"Key type {kvKey.Key.KeyType} not supported");
    }

    private static JsonWebKey CreateJsonWebKeyFromKeyVaultKey(KeyVaultKey kvKey, SigningKey key)
    {
        if (kvKey.Key.KeyType == KeyType.Rsa)
        {
            using var rsa = kvKey.Key.ToRSA();
            var parameters = rsa.ExportParameters(false);

            return new JsonWebKey
            {
                Kty = "RSA",
                Use = key.Use == SigningKeyUse.Signing ? "sig" : "enc",
                Kid = key.KeyId,
                Alg = key.Algorithm,
                N = Base64UrlEncoder.Encode(parameters.Modulus!),
                E = Base64UrlEncoder.Encode(parameters.Exponent!)
            };
        }
        else if (kvKey.Key.KeyType == KeyType.Ec)
        {
            using var ecdsa = kvKey.Key.ToECDsa();
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

        throw new NotSupportedException($"Key type {kvKey.Key.KeyType} not supported");
    }

    #endregion
}

/// <summary>
/// Custom SecurityKey that performs signing operations in Azure Key Vault.
/// The private key never leaves Key Vault. Uses a custom CryptoProviderFactory
/// so that JwtSecurityTokenHandler can create signatures via Key Vault.
/// </summary>
public class AzureKeyVaultSecurityKey : AsymmetricSecurityKey
{
    internal readonly CryptographyClient CryptoClient;
    private readonly ILogger _logger;

    public override int KeySize => 2048;

    public AzureKeyVaultSecurityKey(
        CryptographyClient cryptoClient,
        string keyId,
        string algorithm,
        ILogger logger)
    {
        CryptoClient = cryptoClient;
        KeyId = keyId;
        _logger = logger;
        CryptoProviderFactory = new AzureKeyVaultCryptoProviderFactory(logger)
        {
            CacheSignatureProviders = false
        };
    }

    [Obsolete("Use PrivateKeyStatus instead")]
    public override bool HasPrivateKey => true;

    public override PrivateKeyStatus PrivateKeyStatus => PrivateKeyStatus.Exists;

    public override bool IsSupportedAlgorithm(string algorithm)
    {
        return algorithm is SecurityAlgorithms.RsaSha256 or SecurityAlgorithms.RsaSha384
            or SecurityAlgorithms.RsaSha512 or SecurityAlgorithms.EcdsaSha256
            or SecurityAlgorithms.EcdsaSha384 or SecurityAlgorithms.EcdsaSha512;
    }
}

/// <summary>
/// CryptoProviderFactory that creates SignatureProviders backed by Azure Key Vault.
/// </summary>
internal class AzureKeyVaultCryptoProviderFactory : CryptoProviderFactory
{
    private readonly ILogger _logger;

    public AzureKeyVaultCryptoProviderFactory(ILogger logger)
    {
        _logger = logger;
    }

    public override bool IsSupportedAlgorithm(string algorithm, SecurityKey key)
    {
        if (key is AzureKeyVaultSecurityKey)
        {
            return algorithm is SecurityAlgorithms.RsaSha256 or SecurityAlgorithms.RsaSha384
                or SecurityAlgorithms.RsaSha512 or SecurityAlgorithms.EcdsaSha256
                or SecurityAlgorithms.EcdsaSha384 or SecurityAlgorithms.EcdsaSha512;
        }
        return base.IsSupportedAlgorithm(algorithm, key);
    }

    public override SignatureProvider CreateForSigning(SecurityKey key, string algorithm, bool cacheProvider)
    {
        if (key is AzureKeyVaultSecurityKey kvKey)
        {
            return new AzureKeyVaultSignatureProvider(kvKey, algorithm, _logger);
        }
        return base.CreateForSigning(key, algorithm, cacheProvider);
    }

    public override SignatureProvider CreateForSigning(SecurityKey key, string algorithm)
    {
        return CreateForSigning(key, algorithm, false);
    }

    public override SignatureProvider CreateForVerifying(SecurityKey key, string algorithm, bool cacheProvider)
    {
        if (key is AzureKeyVaultSecurityKey kvKey)
        {
            return new AzureKeyVaultSignatureProvider(kvKey, algorithm, _logger);
        }
        return base.CreateForVerifying(key, algorithm, cacheProvider);
    }

    public override SignatureProvider CreateForVerifying(SecurityKey key, string algorithm)
    {
        return CreateForVerifying(key, algorithm, false);
    }
}

/// <summary>
/// SignatureProvider that delegates signing/verification to Azure Key Vault's CryptographyClient.
/// </summary>
internal class AzureKeyVaultSignatureProvider : SignatureProvider
{
    private readonly CryptographyClient _cryptoClient;
    private readonly ILogger _logger;

    public AzureKeyVaultSignatureProvider(AzureKeyVaultSecurityKey key, string algorithm, ILogger logger)
        : base(key, algorithm)
    {
        _cryptoClient = key.CryptoClient;
        _logger = logger;
    }

    public override byte[] Sign(byte[] input)
    {
        var kvAlgorithm = MapToKeyVaultAlgorithm(Algorithm);
        var result = _cryptoClient.SignData(kvAlgorithm, input);
        return result.Signature;
    }

    public override bool Verify(byte[] input, byte[] signature)
    {
        var kvAlgorithm = MapToKeyVaultAlgorithm(Algorithm);
        var result = _cryptoClient.VerifyData(kvAlgorithm, input, signature);
        return result.IsValid;
    }

    public override bool Verify(byte[] input, int inputOffset, int inputLength, byte[] signature, int signatureOffset, int signatureLength)
    {
        var inputSlice = new byte[inputLength];
        Buffer.BlockCopy(input, inputOffset, inputSlice, 0, inputLength);
        var sigSlice = new byte[signatureLength];
        Buffer.BlockCopy(signature, signatureOffset, sigSlice, 0, signatureLength);
        return Verify(inputSlice, sigSlice);
    }

    protected override void Dispose(bool disposing)
    {
    }

    private static SignatureAlgorithm MapToKeyVaultAlgorithm(string algorithm) => algorithm switch
    {
        SecurityAlgorithms.RsaSha256 => SignatureAlgorithm.RS256,
        SecurityAlgorithms.RsaSha384 => SignatureAlgorithm.RS384,
        SecurityAlgorithms.RsaSha512 => SignatureAlgorithm.RS512,
        SecurityAlgorithms.EcdsaSha256 => SignatureAlgorithm.ES256,
        SecurityAlgorithms.EcdsaSha384 => SignatureAlgorithm.ES384,
        SecurityAlgorithms.EcdsaSha512 => SignatureAlgorithm.ES512,
        _ => throw new NotSupportedException($"Algorithm '{algorithm}' is not supported for Key Vault signing")
    };
}
