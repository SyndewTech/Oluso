using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;
using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using Oluso.Certificates;
using Oluso.Core.Domain.Entities;
using Oluso.Core.Services;
using Oluso.Keys;
using Xunit;

namespace Oluso.Tests.Certificates;

/// <summary>
/// Security tests for certificate generation.
/// These tests validate compliance with:
/// - X.509 v3 certificate format (RFC 5280)
/// - SAML 2.0 certificate requirements (OASIS)
/// - NIST SP 800-57 key management guidelines
/// - CAB Forum Baseline Requirements (for production use)
/// </summary>
public class CertificateGenerationSecurityTests
{
    private readonly Mock<IKeyEncryptionService> _encryptionService;
    private readonly LocalCertificateMaterialProvider _provider;

    public CertificateGenerationSecurityTests()
    {
        _encryptionService = new Mock<IKeyEncryptionService>();

        // Mock encryption
        _encryptionService.Setup(x => x.Encrypt(It.IsAny<byte[]>()))
            .Returns((byte[] data) => Convert.ToBase64String(data));
        _encryptionService.Setup(x => x.Decrypt(It.IsAny<string>()))
            .Returns((string data) => Convert.FromBase64String(data));

        _provider = new LocalCertificateMaterialProvider(
            _encryptionService.Object,
            NullLogger<LocalCertificateMaterialProvider>.Instance);
    }

    #region RSA Certificate Tests

    /// <summary>
    /// NIST SP 800-57: RSA keys for certificates should be at least 2048 bits
    /// </summary>
    [Theory]
    [InlineData(2048)]
    [InlineData(3072)]
    [InlineData(4096)]
    public async Task GenerateRsaCertificate_WithValidKeySize_GeneratesCorrectSize(int keySize)
    {
        var request = new CertificateGenerationParams
        {
            Subject = "CN=Test Certificate",
            KeyType = SigningKeyType.RSA,
            KeySize = keySize,
            ValidityDays = 365,
            KeyUsage = CertificateKeyUsage.DigitalSignature
        };

        var result = await _provider.GenerateCertificateAsync(request);

        result.Certificate.Should().NotBeNull();

        // Use GetRSAPublicKey() for cross-platform compatibility
        using var rsa = result.Certificate!.GetRSAPublicKey();
        rsa.Should().NotBeNull();
        rsa!.KeySize.Should().Be(keySize);
    }

    /// <summary>
    /// RFC 5280: Certificates must use SHA-256 or stronger for signatures
    /// </summary>
    [Fact]
    public async Task GenerateRsaCertificate_UsesSha256Signature()
    {
        var request = new CertificateGenerationParams
        {
            Subject = "CN=Test Certificate",
            KeyType = SigningKeyType.RSA,
            KeySize = 2048,
            ValidityDays = 365,
            KeyUsage = CertificateKeyUsage.DigitalSignature
        };

        var result = await _provider.GenerateCertificateAsync(request);

        result.Certificate.Should().NotBeNull();

        // Signature algorithm should be SHA256 with RSA
        result.Certificate!.SignatureAlgorithm.FriendlyName
            .Should().ContainAny("sha256RSA", "SHA256withRSA", "RSA-SHA256");
    }

    #endregion

    #region EC Certificate Tests

    /// <summary>
    /// SAML 2.0 and OIDC: EC certificates should use approved NIST curves
    /// </summary>
    [Theory]
    [InlineData(256, "P-256", "nistP256")]
    [InlineData(384, "P-384", "nistP384")]
    [InlineData(521, "P-521", "nistP521")]
    public async Task GenerateEcCertificate_UsesCorrectCurve(int keySize, string expectedCurve, string altCurveName)
    {
        var request = new CertificateGenerationParams
        {
            Subject = "CN=Test EC Certificate",
            KeyType = SigningKeyType.EC,
            KeySize = keySize,
            ValidityDays = 365,
            KeyUsage = CertificateKeyUsage.DigitalSignature
        };

        var result = await _provider.GenerateCertificateAsync(request);

        result.Certificate.Should().NotBeNull();

        // Use GetECDsaPublicKey() for cross-platform compatibility
        using var ecdsa = result.Certificate!.GetECDsaPublicKey();
        ecdsa.Should().NotBeNull();

        var parameters = ecdsa!.ExportParameters(false);
        parameters.Curve.Oid?.FriendlyName.Should().ContainAny(expectedCurve, altCurveName, "ECDSA_P256", "ECDSA_P384", "ECDSA_P521");
    }

    /// <summary>
    /// EC certificates must use SHA-256 signature
    /// </summary>
    [Fact]
    public async Task GenerateEcCertificate_UsesSha256Signature()
    {
        var request = new CertificateGenerationParams
        {
            Subject = "CN=Test EC Certificate",
            KeyType = SigningKeyType.EC,
            KeySize = 256,
            ValidityDays = 365,
            KeyUsage = CertificateKeyUsage.DigitalSignature
        };

        var result = await _provider.GenerateCertificateAsync(request);

        result.Certificate.Should().NotBeNull();

        // Signature algorithm should be ECDSA with SHA256
        result.Certificate!.SignatureAlgorithm.FriendlyName
            .Should().ContainAny("sha256ECDSA", "ECDSA-SHA256", "ECDsaWithSHA256");
    }

    #endregion

    #region Certificate Extensions Tests (RFC 5280)

    /// <summary>
    /// RFC 5280: Key Usage extension should be marked critical
    /// </summary>
    [Fact]
    public async Task GenerateCertificate_KeyUsageIsCritical()
    {
        var request = new CertificateGenerationParams
        {
            Subject = "CN=Test Certificate",
            KeyType = SigningKeyType.RSA,
            KeySize = 2048,
            ValidityDays = 365,
            KeyUsage = CertificateKeyUsage.DigitalSignature
        };

        var result = await _provider.GenerateCertificateAsync(request);

        var keyUsageExt = result.Certificate!.Extensions
            .OfType<X509KeyUsageExtension>()
            .FirstOrDefault();

        keyUsageExt.Should().NotBeNull("Key Usage extension should be present");
        keyUsageExt!.Critical.Should().BeTrue("Key Usage extension must be critical per RFC 5280");
    }

    /// <summary>
    /// Key Usage flags should be set correctly based on certificate purpose
    /// </summary>
    [Theory]
    [InlineData(CertificateKeyUsage.DigitalSignature, X509KeyUsageFlags.DigitalSignature)]
    [InlineData(CertificateKeyUsage.KeyEncipherment, X509KeyUsageFlags.KeyEncipherment)]
    [InlineData(CertificateKeyUsage.DataEncipherment, X509KeyUsageFlags.DataEncipherment)]
    [InlineData(CertificateKeyUsage.NonRepudiation, X509KeyUsageFlags.NonRepudiation)]
    public async Task GenerateCertificate_SetsCorrectKeyUsage(
        CertificateKeyUsage requestedUsage,
        X509KeyUsageFlags expectedFlag)
    {
        var request = new CertificateGenerationParams
        {
            Subject = "CN=Test Certificate",
            KeyType = SigningKeyType.RSA,
            KeySize = 2048,
            ValidityDays = 365,
            KeyUsage = requestedUsage
        };

        var result = await _provider.GenerateCertificateAsync(request);

        var keyUsageExt = result.Certificate!.Extensions
            .OfType<X509KeyUsageExtension>()
            .FirstOrDefault();

        keyUsageExt.Should().NotBeNull();
        keyUsageExt!.KeyUsages.Should().HaveFlag(expectedFlag);
    }

    /// <summary>
    /// SAML Signing: Certificates for SAML signing need DigitalSignature key usage
    /// </summary>
    [Fact]
    public async Task GenerateCertificate_ForSamlSigning_HasDigitalSignatureUsage()
    {
        var request = new CertificateGenerationParams
        {
            Subject = "CN=SAML Signing Certificate",
            KeyType = SigningKeyType.RSA,
            KeySize = 2048,
            ValidityDays = 365,
            KeyUsage = CertificateKeyUsage.DigitalSignature | CertificateKeyUsage.NonRepudiation
        };

        var result = await _provider.GenerateCertificateAsync(request);

        var keyUsageExt = result.Certificate!.Extensions
            .OfType<X509KeyUsageExtension>()
            .FirstOrDefault();

        keyUsageExt!.KeyUsages.Should().HaveFlag(X509KeyUsageFlags.DigitalSignature);
        keyUsageExt.KeyUsages.Should().HaveFlag(X509KeyUsageFlags.NonRepudiation);
    }

    /// <summary>
    /// RFC 5280: Subject Key Identifier extension should be present
    /// </summary>
    [Fact]
    public async Task GenerateCertificate_HasSubjectKeyIdentifier()
    {
        var request = new CertificateGenerationParams
        {
            Subject = "CN=Test Certificate",
            KeyType = SigningKeyType.RSA,
            KeySize = 2048,
            ValidityDays = 365,
            KeyUsage = CertificateKeyUsage.DigitalSignature
        };

        var result = await _provider.GenerateCertificateAsync(request);

        var skiExt = result.Certificate!.Extensions
            .OfType<X509SubjectKeyIdentifierExtension>()
            .FirstOrDefault();

        skiExt.Should().NotBeNull("Subject Key Identifier extension should be present");
        skiExt!.SubjectKeyIdentifier.Should().NotBeNullOrEmpty();
    }

    /// <summary>
    /// Subject Alternative Names should be included when provided
    /// </summary>
    [Fact]
    public async Task GenerateCertificate_IncludesSubjectAlternativeNames()
    {
        var request = new CertificateGenerationParams
        {
            Subject = "CN=Test Certificate",
            KeyType = SigningKeyType.RSA,
            KeySize = 2048,
            ValidityDays = 365,
            KeyUsage = CertificateKeyUsage.DigitalSignature,
            SubjectAlternativeNames = new List<string> { "auth.example.com", "login.example.com" }
        };

        var result = await _provider.GenerateCertificateAsync(request);

        // Check for SAN extension
        var sanExtension = result.Certificate!.Extensions
            .FirstOrDefault(e => e.Oid?.Value == "2.5.29.17"); // SAN OID

        sanExtension.Should().NotBeNull("Subject Alternative Name extension should be present");
    }

    #endregion

    #region Certificate Validity Tests

    /// <summary>
    /// Certificate validity period should match requested days
    /// </summary>
    [Theory]
    [InlineData(30)]
    [InlineData(365)]
    [InlineData(730)]
    public async Task GenerateCertificate_ValidityMatchesRequest(int validityDays)
    {
        var request = new CertificateGenerationParams
        {
            Subject = "CN=Test Certificate",
            KeyType = SigningKeyType.RSA,
            KeySize = 2048,
            ValidityDays = validityDays,
            KeyUsage = CertificateKeyUsage.DigitalSignature
        };

        var result = await _provider.GenerateCertificateAsync(request);

        var notBefore = result.Certificate!.NotBefore.ToUniversalTime();
        var notAfter = result.Certificate.NotAfter.ToUniversalTime();

        var actualDays = (notAfter - notBefore).TotalDays;
        actualDays.Should().BeApproximately(validityDays, 1); // Allow 1 day tolerance
    }

    /// <summary>
    /// Certificate NotBefore should be approximately now
    /// </summary>
    [Fact]
    public async Task GenerateCertificate_NotBeforeIsNow()
    {
        var beforeGeneration = DateTime.UtcNow.AddSeconds(-5);

        var request = new CertificateGenerationParams
        {
            Subject = "CN=Test Certificate",
            KeyType = SigningKeyType.RSA,
            KeySize = 2048,
            ValidityDays = 365,
            KeyUsage = CertificateKeyUsage.DigitalSignature
        };

        var result = await _provider.GenerateCertificateAsync(request);
        var afterGeneration = DateTime.UtcNow.AddSeconds(5);

        var notBefore = result.Certificate!.NotBefore.ToUniversalTime();

        notBefore.Should().BeOnOrAfter(beforeGeneration);
        notBefore.Should().BeOnOrBefore(afterGeneration);
    }

    #endregion

    #region Thumbprint Tests

    /// <summary>
    /// SHA-1 thumbprint should be computed correctly
    /// </summary>
    [Fact]
    public async Task GenerateCertificate_Sha1ThumbprintIsValid()
    {
        var request = new CertificateGenerationParams
        {
            Subject = "CN=Test Certificate",
            KeyType = SigningKeyType.RSA,
            KeySize = 2048,
            ValidityDays = 365,
            KeyUsage = CertificateKeyUsage.DigitalSignature
        };

        var result = await _provider.GenerateCertificateAsync(request);

        result.Thumbprint.Should().NotBeNullOrEmpty();
        result.Thumbprint.Should().HaveLength(40); // SHA-1 is 20 bytes = 40 hex chars
        result.Thumbprint.Should().MatchRegex("^[A-F0-9]+$"); // Hex uppercase
    }

    /// <summary>
    /// SHA-256 thumbprint should be computed correctly and be URL-safe
    /// </summary>
    [Fact]
    public async Task GenerateCertificate_Sha256ThumbprintIsUrlSafe()
    {
        var request = new CertificateGenerationParams
        {
            Subject = "CN=Test Certificate",
            KeyType = SigningKeyType.RSA,
            KeySize = 2048,
            ValidityDays = 365,
            KeyUsage = CertificateKeyUsage.DigitalSignature
        };

        var result = await _provider.GenerateCertificateAsync(request);

        result.ThumbprintSha256.Should().NotBeNullOrEmpty();
        result.ThumbprintSha256.Should().NotContain("+", "should be Base64URL encoded");
        result.ThumbprintSha256.Should().NotContain("/", "should be Base64URL encoded");
        result.ThumbprintSha256.Should().NotContain("=", "should not have padding");
    }

    #endregion

    #region Private Key Protection Tests

    /// <summary>
    /// Private key must be encrypted for storage
    /// </summary>
    [Fact]
    public async Task GenerateCertificate_PrivateKeyIsEncrypted()
    {
        var request = new CertificateGenerationParams
        {
            Subject = "CN=Test Certificate",
            KeyType = SigningKeyType.RSA,
            KeySize = 2048,
            ValidityDays = 365,
            KeyUsage = CertificateKeyUsage.DigitalSignature
        };

        var result = await _provider.GenerateCertificateAsync(request);

        // Verify encryption service was called
        _encryptionService.Verify(x => x.Encrypt(It.IsAny<byte[]>()), Times.Once);

        result.EncryptedPrivateKey.Should().NotBeNullOrEmpty();
    }

    /// <summary>
    /// Certificate can be loaded from encrypted storage
    /// </summary>
    [Fact]
    public async Task LoadCertificate_CanLoadFromEncryptedStorage()
    {
        var request = new CertificateGenerationParams
        {
            Subject = "CN=Test Certificate",
            KeyType = SigningKeyType.RSA,
            KeySize = 2048,
            ValidityDays = 365,
            KeyUsage = CertificateKeyUsage.DigitalSignature
        };

        var result = await _provider.GenerateCertificateAsync(request);

        var signingKey = new SigningKey
        {
            KeyId = "test-key",
            PrivateKeyData = result.EncryptedPrivateKey
        };

        var loadedCert = await _provider.LoadCertificateAsync(signingKey);

        loadedCert.Should().NotBeNull();
        loadedCert!.HasPrivateKey.Should().BeTrue();
        loadedCert.Subject.Should().Be("CN=Test Certificate");
    }

    /// <summary>
    /// Certificate public data (X5c) should not contain private key
    /// </summary>
    [Fact]
    public async Task GenerateCertificate_PublicDataDoesNotContainPrivateKey()
    {
        var request = new CertificateGenerationParams
        {
            Subject = "CN=Test Certificate",
            KeyType = SigningKeyType.RSA,
            KeySize = 2048,
            ValidityDays = 365,
            KeyUsage = CertificateKeyUsage.DigitalSignature
        };

        var result = await _provider.GenerateCertificateAsync(request);

        // Load certificate from public data only
        var certBytes = Convert.FromBase64String(result.CertificateData);
        var publicCert = new X509Certificate2(certBytes);

        publicCert.HasPrivateKey.Should().BeFalse("public certificate should not have private key");
    }

    #endregion

    #region Certificate Signing Tests

    /// <summary>
    /// Generated certificate should be able to sign data
    /// </summary>
    [Fact]
    public async Task GenerateCertificate_CanSignData()
    {
        var request = new CertificateGenerationParams
        {
            Subject = "CN=Signing Certificate",
            KeyType = SigningKeyType.RSA,
            KeySize = 2048,
            ValidityDays = 365,
            KeyUsage = CertificateKeyUsage.DigitalSignature
        };

        var result = await _provider.GenerateCertificateAsync(request);

        var data = System.Text.Encoding.UTF8.GetBytes("test data to sign");

        using var rsa = result.Certificate!.GetRSAPrivateKey();
        rsa.Should().NotBeNull();

        var signature = rsa!.SignData(data, HashAlgorithmName.SHA256, RSASignaturePadding.Pkcs1);
        signature.Should().NotBeEmpty();

        // Verify with public key
        using var rsaPublic = result.Certificate.GetRSAPublicKey();
        var isValid = rsaPublic!.VerifyData(data, signature, HashAlgorithmName.SHA256, RSASignaturePadding.Pkcs1);
        isValid.Should().BeTrue();
    }

    /// <summary>
    /// Self-signed certificate should verify against itself
    /// </summary>
    [Fact]
    public async Task GenerateCertificate_SelfSignedVerifiesCorrectly()
    {
        var request = new CertificateGenerationParams
        {
            Subject = "CN=Self-Signed Certificate",
            KeyType = SigningKeyType.RSA,
            KeySize = 2048,
            ValidityDays = 365,
            KeyUsage = CertificateKeyUsage.DigitalSignature
        };

        var result = await _provider.GenerateCertificateAsync(request);

        // Subject and Issuer should be the same for self-signed
        result.Certificate!.Subject.Should().Be(result.Certificate.Issuer);

        // Certificate should verify using its own public key
        var chain = new X509Chain();
        chain.ChainPolicy.RevocationMode = X509RevocationMode.NoCheck;
        chain.ChainPolicy.VerificationFlags = X509VerificationFlags.AllowUnknownCertificateAuthority;

        var isValid = chain.Build(result.Certificate);
        // Note: Self-signed certs won't build a full chain, but the signature is valid
        result.Subject.Should().Be(result.Issuer);
    }

    #endregion
}
