using System.Security.Cryptography;
using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.IdentityModel.Tokens;
using Moq;
using Oluso.Core.Domain.Entities;
using Oluso.Core.Services;
using Oluso.Keys;
using Xunit;

namespace Oluso.Tests.Keys;

/// <summary>
/// Security tests for key generation.
/// These tests validate compliance with:
/// - RFC 7517 (JSON Web Key)
/// - RFC 7518 (JSON Web Algorithms) - Key size requirements
/// - NIST SP 800-57 recommendations
/// - OWASP cryptographic guidelines
/// </summary>
public class KeyGenerationSecurityTests
{
    private readonly Mock<IKeyEncryptionService> _encryptionService;
    private readonly LocalKeyMaterialProvider _provider;

    public KeyGenerationSecurityTests()
    {
        _encryptionService = new Mock<IKeyEncryptionService>();

        // Mock encryption to just return the input (for testing purposes)
        _encryptionService.Setup(x => x.EncryptAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((string s, CancellationToken _) => $"encrypted:{s}");
        _encryptionService.Setup(x => x.DecryptAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((string s, CancellationToken _) => s.Replace("encrypted:", ""));

        _provider = new LocalKeyMaterialProvider(
            _encryptionService.Object,
            NullLogger<LocalKeyMaterialProvider>.Instance);
    }

    #region RSA Key Generation Tests

    /// <summary>
    /// RFC 7518 Section 3.3: RSA keys MUST be at least 2048 bits for RS256/RS384/RS512
    /// </summary>
    [Theory]
    [InlineData(2048)]
    [InlineData(3072)]
    [InlineData(4096)]
    public async Task GenerateRsaKey_WithValidKeySize_GeneratesCorrectSize(int keySize)
    {
        var request = new KeyGenerationParams
        {
            KeyType = SigningKeyType.RSA,
            Algorithm = "RS256",
            KeySize = keySize
        };

        var result = await _provider.GenerateKeyAsync(request);

        result.PublicKeyData.Should().NotBeNullOrEmpty();
        result.EncryptedPrivateKey.Should().NotBeNullOrEmpty();

        // Verify actual key size
        var publicKeyBytes = Convert.FromBase64String(result.PublicKeyData);
        using var rsa = RSA.Create();
        rsa.ImportRSAPublicKey(publicKeyBytes, out _);
        rsa.KeySize.Should().Be(keySize);
    }

    /// <summary>
    /// NIST SP 800-57: Very weak RSA keys (512-bit) are rejected by the runtime.
    /// </summary>
    [Fact]
    public async Task GenerateRsaKey_With512BitKey_ThrowsException()
    {
        // .NET runtime correctly rejects 512-bit RSA keys
        var request = new KeyGenerationParams
        {
            KeyType = SigningKeyType.RSA,
            Algorithm = "RS256",
            KeySize = 512
        };

        Func<Task> act = async () => await _provider.GenerateKeyAsync(request);

        // 512-bit keys are rejected at the crypto layer
        await act.Should().ThrowAsync<CryptographicException>();
    }

    /// <summary>
    /// NIST SP 800-57: 1024-bit RSA keys are deprecated but still technically allowed by .NET.
    /// Note: Consider adding application-level validation to reject keys < 2048 bits.
    /// </summary>
    [Fact]
    public async Task GenerateRsaKey_With1024BitKey_IsAllowedButDeprecated()
    {
        // 1024-bit keys are deprecated but .NET runtime still allows them
        // This documents current behavior - consider adding validation
        var request = new KeyGenerationParams
        {
            KeyType = SigningKeyType.RSA,
            Algorithm = "RS256",
            KeySize = 1024
        };

        var result = await _provider.GenerateKeyAsync(request);

        result.PublicKeyData.Should().NotBeNullOrEmpty();

        var publicKeyBytes = Convert.FromBase64String(result.PublicKeyData);
        using var rsa = RSA.Create();
        rsa.ImportRSAPublicKey(publicKeyBytes, out _);
        rsa.KeySize.Should().Be(1024);

        // SECURITY NOTE: 1024-bit RSA is deprecated per NIST SP 800-57
        // Consider adding validation to require minimum 2048 bits
    }

    /// <summary>
    /// RSA keys must be usable for signing operations
    /// </summary>
    [Fact]
    public async Task GenerateRsaKey_CanSignAndVerify()
    {
        var request = new KeyGenerationParams
        {
            KeyType = SigningKeyType.RSA,
            Algorithm = "RS256",
            KeySize = 2048
        };

        var result = await _provider.GenerateKeyAsync(request);

        // Create signing key entity
        var signingKey = new SigningKey
        {
            KeyId = result.KeyId,
            KeyType = SigningKeyType.RSA,
            Algorithm = "RS256",
            PrivateKeyData = result.EncryptedPrivateKey,
            PublicKeyData = result.PublicKeyData
        };

        // Get signing credentials
        var credentials = await _provider.GetSigningCredentialsAsync(signingKey);
        credentials.Should().NotBeNull();
        credentials!.Algorithm.Should().Be("RS256");

        // Test actual signing
        var data = System.Text.Encoding.UTF8.GetBytes("test data to sign");
        var rsaKey = (RsaSecurityKey)credentials.Key;
        using var sha256 = SHA256.Create();
        var hash = sha256.ComputeHash(data);
        var signature = rsaKey.Rsa.SignHash(hash, HashAlgorithmName.SHA256, RSASignaturePadding.Pkcs1);

        // Verify with public key
        var publicKey = await _provider.GetPublicKeyAsync(signingKey);
        publicKey.Should().NotBeNull();
        var rsaPublic = (RsaSecurityKey)publicKey!;
        var isValid = rsaPublic.Rsa.VerifyHash(hash, signature, HashAlgorithmName.SHA256, RSASignaturePadding.Pkcs1);
        isValid.Should().BeTrue();
    }

    #endregion

    #region EC Key Generation Tests

    /// <summary>
    /// RFC 7518 Section 3.4: ES256 requires P-256 curve
    /// </summary>
    [Fact]
    public async Task GenerateEcKey_ES256_UsesP256Curve()
    {
        var request = new KeyGenerationParams
        {
            KeyType = SigningKeyType.EC,
            Algorithm = "ES256",
            KeySize = 256
        };

        var result = await _provider.GenerateKeyAsync(request);

        var publicKeyBytes = Convert.FromBase64String(result.PublicKeyData);
        using var ecdsa = ECDsa.Create();
        ecdsa.ImportSubjectPublicKeyInfo(publicKeyBytes, out _);

        var parameters = ecdsa.ExportParameters(false);
        parameters.Curve.Oid?.FriendlyName.Should().ContainAny("nistP256", "ECDSA_P256", "P-256");
    }

    /// <summary>
    /// RFC 7518 Section 3.4: ES384 requires P-384 curve
    /// </summary>
    [Fact]
    public async Task GenerateEcKey_ES384_UsesP384Curve()
    {
        var request = new KeyGenerationParams
        {
            KeyType = SigningKeyType.EC,
            Algorithm = "ES384",
            KeySize = 384
        };

        var result = await _provider.GenerateKeyAsync(request);

        var publicKeyBytes = Convert.FromBase64String(result.PublicKeyData);
        using var ecdsa = ECDsa.Create();
        ecdsa.ImportSubjectPublicKeyInfo(publicKeyBytes, out _);

        var parameters = ecdsa.ExportParameters(false);
        parameters.Curve.Oid?.FriendlyName.Should().ContainAny("nistP384", "ECDSA_P384", "P-384");
    }

    /// <summary>
    /// RFC 7518 Section 3.4: ES512 requires P-521 curve
    /// </summary>
    [Fact]
    public async Task GenerateEcKey_ES512_UsesP521Curve()
    {
        var request = new KeyGenerationParams
        {
            KeyType = SigningKeyType.EC,
            Algorithm = "ES512",
            KeySize = 521
        };

        var result = await _provider.GenerateKeyAsync(request);

        var publicKeyBytes = Convert.FromBase64String(result.PublicKeyData);
        using var ecdsa = ECDsa.Create();
        ecdsa.ImportSubjectPublicKeyInfo(publicKeyBytes, out _);

        var parameters = ecdsa.ExportParameters(false);
        parameters.Curve.Oid?.FriendlyName.Should().ContainAny("nistP521", "ECDSA_P521", "P-521");
    }

    /// <summary>
    /// EC keys must be usable for signing operations
    /// </summary>
    [Fact]
    public async Task GenerateEcKey_CanSignAndVerify()
    {
        var request = new KeyGenerationParams
        {
            KeyType = SigningKeyType.EC,
            Algorithm = "ES256",
            KeySize = 256
        };

        var result = await _provider.GenerateKeyAsync(request);

        var signingKey = new SigningKey
        {
            KeyId = result.KeyId,
            KeyType = SigningKeyType.EC,
            Algorithm = "ES256",
            PrivateKeyData = result.EncryptedPrivateKey,
            PublicKeyData = result.PublicKeyData
        };

        var credentials = await _provider.GetSigningCredentialsAsync(signingKey);
        credentials.Should().NotBeNull();
        credentials!.Algorithm.Should().Be("ES256");

        // Test actual signing
        var data = System.Text.Encoding.UTF8.GetBytes("test data to sign");
        var ecdsaKey = (ECDsaSecurityKey)credentials.Key;
        var signature = ecdsaKey.ECDsa.SignData(data, HashAlgorithmName.SHA256);

        // Verify with public key
        var publicKey = await _provider.GetPublicKeyAsync(signingKey);
        publicKey.Should().NotBeNull();
        var ecdsaPublic = (ECDsaSecurityKey)publicKey!;
        var isValid = ecdsaPublic.ECDsa.VerifyData(data, signature, HashAlgorithmName.SHA256);
        isValid.Should().BeTrue();
    }

    #endregion

    #region Symmetric Key Generation Tests

    /// <summary>
    /// RFC 7518 Section 3.2: HS256 requires minimum 256-bit key
    /// </summary>
    [Theory]
    [InlineData(256, "HS256")]
    [InlineData(384, "HS384")]
    [InlineData(512, "HS512")]
    public async Task GenerateSymmetricKey_WithValidSize_GeneratesCorrectSize(int keySize, string algorithm)
    {
        var request = new KeyGenerationParams
        {
            KeyType = SigningKeyType.Symmetric,
            Algorithm = algorithm,
            KeySize = keySize
        };

        var result = await _provider.GenerateKeyAsync(request);

        result.PublicKeyData.Should().NotBeNullOrEmpty();

        // Symmetric key should be exactly the requested size
        var keyBytes = Convert.FromBase64String(result.PublicKeyData);
        (keyBytes.Length * 8).Should().Be(keySize);
    }

    /// <summary>
    /// Symmetric keys must use cryptographically secure random generation
    /// </summary>
    [Fact]
    public async Task GenerateSymmetricKey_GeneratesUniqueKeys()
    {
        var request = new KeyGenerationParams
        {
            KeyType = SigningKeyType.Symmetric,
            Algorithm = "HS256",
            KeySize = 256
        };

        var keys = new HashSet<string>();
        for (int i = 0; i < 100; i++)
        {
            var result = await _provider.GenerateKeyAsync(request);
            keys.Add(result.PublicKeyData).Should().BeTrue("each generated key should be unique");
        }
    }

    #endregion

    #region JWK Generation Tests (RFC 7517)

    /// <summary>
    /// RFC 7517: JWK for RSA must include kty, n, e
    /// </summary>
    [Fact]
    public async Task GetJsonWebKey_Rsa_ContainsRequiredFields()
    {
        var request = new KeyGenerationParams
        {
            KeyType = SigningKeyType.RSA,
            Algorithm = "RS256",
            KeySize = 2048
        };

        var result = await _provider.GenerateKeyAsync(request);

        var signingKey = new SigningKey
        {
            KeyId = result.KeyId,
            KeyType = SigningKeyType.RSA,
            Algorithm = "RS256",
            Use = SigningKeyUse.Signing,
            PublicKeyData = result.PublicKeyData
        };

        var jwk = await _provider.GetJsonWebKeyAsync(signingKey);

        jwk.Should().NotBeNull();
        jwk!.Kty.Should().Be("RSA");
        jwk.Kid.Should().Be(result.KeyId);
        jwk.Alg.Should().Be("RS256");
        jwk.Use.Should().Be("sig");
        jwk.N.Should().NotBeNullOrEmpty("modulus is required for RSA JWK");
        jwk.E.Should().NotBeNullOrEmpty("exponent is required for RSA JWK");

        // Private key components should NOT be in JWK
        jwk.D.Should().BeNullOrEmpty("private exponent should not be in public JWK");
        jwk.P.Should().BeNullOrEmpty("prime p should not be in public JWK");
        jwk.Q.Should().BeNullOrEmpty("prime q should not be in public JWK");
    }

    /// <summary>
    /// RFC 7517: JWK for EC must include kty, crv, x, y
    /// </summary>
    [Fact]
    public async Task GetJsonWebKey_Ec_ContainsRequiredFields()
    {
        var request = new KeyGenerationParams
        {
            KeyType = SigningKeyType.EC,
            Algorithm = "ES256",
            KeySize = 256
        };

        var result = await _provider.GenerateKeyAsync(request);

        var signingKey = new SigningKey
        {
            KeyId = result.KeyId,
            KeyType = SigningKeyType.EC,
            Algorithm = "ES256",
            Use = SigningKeyUse.Signing,
            PublicKeyData = result.PublicKeyData
        };

        var jwk = await _provider.GetJsonWebKeyAsync(signingKey);

        jwk.Should().NotBeNull();
        jwk!.Kty.Should().Be("EC");
        jwk.Kid.Should().Be(result.KeyId);
        jwk.Alg.Should().Be("ES256");
        jwk.Use.Should().Be("sig");
        jwk.Crv.Should().Be("P-256");
        jwk.X.Should().NotBeNullOrEmpty("x coordinate is required for EC JWK");
        jwk.Y.Should().NotBeNullOrEmpty("y coordinate is required for EC JWK");

        // Private key component should NOT be in JWK
        jwk.D.Should().BeNullOrEmpty("private key d should not be in public JWK");
    }

    /// <summary>
    /// RFC 7517: JWK 'use' should be 'sig' for signing keys, 'enc' for encryption
    /// </summary>
    [Theory]
    [InlineData(SigningKeyUse.Signing, "sig")]
    [InlineData(SigningKeyUse.Encryption, "enc")]
    public async Task GetJsonWebKey_SetsCorrectUseValue(SigningKeyUse use, string expectedUse)
    {
        var request = new KeyGenerationParams
        {
            KeyType = SigningKeyType.RSA,
            Algorithm = "RS256",
            KeySize = 2048
        };

        var result = await _provider.GenerateKeyAsync(request);

        var signingKey = new SigningKey
        {
            KeyId = result.KeyId,
            KeyType = SigningKeyType.RSA,
            Algorithm = "RS256",
            Use = use,
            PublicKeyData = result.PublicKeyData
        };

        var jwk = await _provider.GetJsonWebKeyAsync(signingKey);

        jwk!.Use.Should().Be(expectedUse);
    }

    #endregion

    #region Key ID Generation Tests

    /// <summary>
    /// Key IDs should be unique and URL-safe (Base64URL)
    /// </summary>
    [Fact]
    public async Task GenerateKey_KeyIdIsUrlSafe()
    {
        var request = new KeyGenerationParams
        {
            KeyType = SigningKeyType.RSA,
            Algorithm = "RS256",
            KeySize = 2048
        };

        var result = await _provider.GenerateKeyAsync(request);

        result.KeyId.Should().NotBeNullOrEmpty();
        result.KeyId.Should().NotContain("+", "Key ID should be Base64URL encoded");
        result.KeyId.Should().NotContain("/", "Key ID should be Base64URL encoded");
        result.KeyId.Should().NotContain("=", "Key ID should not have padding");
    }

    /// <summary>
    /// Key IDs must be unique across generations
    /// </summary>
    [Fact]
    public async Task GenerateKey_KeyIdsAreUnique()
    {
        var request = new KeyGenerationParams
        {
            KeyType = SigningKeyType.RSA,
            Algorithm = "RS256",
            KeySize = 2048
        };

        var keyIds = new HashSet<string>();
        for (int i = 0; i < 50; i++)
        {
            var result = await _provider.GenerateKeyAsync(request);
            keyIds.Add(result.KeyId).Should().BeTrue("each key ID should be unique");
        }
    }

    #endregion

    #region Private Key Protection Tests

    /// <summary>
    /// Private keys must be encrypted before storage
    /// </summary>
    [Fact]
    public async Task GenerateKey_PrivateKeyIsEncrypted()
    {
        var request = new KeyGenerationParams
        {
            KeyType = SigningKeyType.RSA,
            Algorithm = "RS256",
            KeySize = 2048
        };

        var result = await _provider.GenerateKeyAsync(request);

        // Verify encryption service was called
        _encryptionService.Verify(
            x => x.EncryptAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()),
            Times.Once);

        // The encrypted private key should start with our mock prefix
        result.EncryptedPrivateKey.Should().StartWith("encrypted:");
    }

    /// <summary>
    /// Private key must not appear in public key data
    /// </summary>
    [Fact]
    public async Task GenerateKey_PublicKeyDoesNotContainPrivateKey()
    {
        var request = new KeyGenerationParams
        {
            KeyType = SigningKeyType.RSA,
            Algorithm = "RS256",
            KeySize = 2048
        };

        var result = await _provider.GenerateKeyAsync(request);

        // Decode public key and verify it's actually a public key
        var publicKeyBytes = Convert.FromBase64String(result.PublicKeyData);
        using var rsa = RSA.Create();
        rsa.ImportRSAPublicKey(publicKeyBytes, out _);

        // Attempting to export private key should fail
        Action exportPrivate = () => rsa.ExportRSAPrivateKey();
        exportPrivate.Should().Throw<CryptographicException>("public key should not contain private key material");
    }

    #endregion
}
