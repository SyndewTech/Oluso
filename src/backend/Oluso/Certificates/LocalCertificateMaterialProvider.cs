using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;
using Microsoft.Extensions.Logging;
using Oluso.Core.Domain.Entities;
using Oluso.Core.Services;
using Oluso.Keys;

namespace Oluso.Certificates;

/// <summary>
/// Local certificate provider that generates self-signed certificates
/// and stores them encrypted in the database.
/// </summary>
public class LocalCertificateMaterialProvider : ICertificateMaterialProvider
{
    private readonly IKeyEncryptionService _encryptionService;
    private readonly ILogger<LocalCertificateMaterialProvider> _logger;

    public KeyStorageProvider ProviderType => KeyStorageProvider.Local;

    public LocalCertificateMaterialProvider(
        IKeyEncryptionService encryptionService,
        ILogger<LocalCertificateMaterialProvider> logger)
    {
        _encryptionService = encryptionService;
        _logger = logger;
    }

    public Task<bool> IsAvailableAsync(CancellationToken cancellationToken = default)
    {
        return Task.FromResult(true);
    }

    public Task<CertificateMaterialResult> GenerateCertificateAsync(
        CertificateGenerationParams request,
        CancellationToken cancellationToken = default)
    {
        _logger.LogInformation(
            "Generating self-signed certificate: Subject={Subject}, KeyType={KeyType}, KeySize={KeySize}, ValidityDays={ValidityDays}",
            request.Subject, request.KeyType, request.KeySize, request.ValidityDays);

        X509Certificate2 cert;

        if (request.KeyType == SigningKeyType.RSA)
        {
            cert = GenerateRsaCertificate(request);
        }
        else if (request.KeyType == SigningKeyType.EC)
        {
            cert = GenerateEcCertificate(request);
        }
        else
        {
            throw new ArgumentException($"Unsupported key type for certificates: {request.KeyType}");
        }

        // Export private key for encrypted storage
        var pfxBytes = cert.Export(X509ContentType.Pfx);
        var encryptedPrivateKey = _encryptionService.Encrypt(pfxBytes);

        // Get public certificate for X5c
        var publicCertBytes = cert.Export(X509ContentType.Cert);
        var certificateData = Convert.ToBase64String(publicCertBytes);

        // Compute thumbprints
        var thumbprint = cert.Thumbprint;
        var thumbprintSha256 = ComputeSha256Thumbprint(cert);

        var result = new CertificateMaterialResult
        {
            Certificate = cert,
            Thumbprint = thumbprint,
            ThumbprintSha256 = thumbprintSha256,
            CertificateData = certificateData,
            EncryptedPrivateKey = encryptedPrivateKey,
            Subject = cert.Subject,
            Issuer = cert.Issuer,
            SerialNumber = cert.SerialNumber,
            NotBefore = cert.NotBefore.ToUniversalTime(),
            NotAfter = cert.NotAfter.ToUniversalTime()
        };

        _logger.LogInformation(
            "Generated certificate: Thumbprint={Thumbprint}, Subject={Subject}, ValidUntil={NotAfter}",
            thumbprint, cert.Subject, cert.NotAfter);

        return Task.FromResult(result);
    }

    public Task<X509Certificate2?> LoadCertificateAsync(
        SigningKey key,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrEmpty(key.PrivateKeyData))
        {
            _logger.LogWarning("No private key data for certificate: {KeyId}", key.KeyId);
            return Task.FromResult<X509Certificate2?>(null);
        }

        try
        {
            // Decrypt and load the PFX
            var pfxBytes = _encryptionService.Decrypt(key.PrivateKeyData);

            // EphemeralKeySet is not supported on macOS - use platform-appropriate flags
            var keyStorageFlags = OperatingSystem.IsMacOS()
                ? X509KeyStorageFlags.Exportable
                : X509KeyStorageFlags.MachineKeySet | X509KeyStorageFlags.EphemeralKeySet;

            var cert = new X509Certificate2(pfxBytes, (string?)null, keyStorageFlags);

            return Task.FromResult<X509Certificate2?>(cert);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to load certificate: {KeyId}", key.KeyId);
            return Task.FromResult<X509Certificate2?>(null);
        }
    }

    public Task DeleteCertificateAsync(SigningKey key, CancellationToken cancellationToken = default)
    {
        // For local storage, deletion is handled by removing the SigningKey entity
        // No additional cleanup needed
        return Task.CompletedTask;
    }

    private X509Certificate2 GenerateRsaCertificate(CertificateGenerationParams request)
    {
        using var rsa = RSA.Create(request.KeySize);

        var distinguishedName = new X500DistinguishedName(request.Subject);
        var certRequest = new CertificateRequest(
            distinguishedName,
            rsa,
            HashAlgorithmName.SHA256,
            RSASignaturePadding.Pkcs1);

        // Add key usage
        AddKeyUsage(certRequest, request.KeyUsage);

        // Add Subject Alternative Names if provided
        if (request.SubjectAlternativeNames?.Any() == true)
        {
            var sanBuilder = new SubjectAlternativeNameBuilder();
            foreach (var san in request.SubjectAlternativeNames)
            {
                sanBuilder.AddDnsName(san);
            }
            certRequest.CertificateExtensions.Add(sanBuilder.Build());
        }

        // Add Subject Key Identifier
        certRequest.CertificateExtensions.Add(
            new X509SubjectKeyIdentifierExtension(certRequest.PublicKey, false));

        var notBefore = DateTimeOffset.UtcNow;
        var notAfter = notBefore.AddDays(request.ValidityDays);

        var cert = certRequest.CreateSelfSigned(notBefore, notAfter);

        // Export/import to get a certificate with properly associated private key
        var pfxBytes = cert.Export(X509ContentType.Pfx);

        // Use platform-appropriate flags
        var keyStorageFlags = OperatingSystem.IsMacOS()
            ? X509KeyStorageFlags.Exportable
            : X509KeyStorageFlags.MachineKeySet | X509KeyStorageFlags.Exportable;

        return new X509Certificate2(pfxBytes, (string?)null, keyStorageFlags);
    }

    private X509Certificate2 GenerateEcCertificate(CertificateGenerationParams request)
    {
        var curve = request.KeySize switch
        {
            256 => ECCurve.NamedCurves.nistP256,
            384 => ECCurve.NamedCurves.nistP384,
            521 => ECCurve.NamedCurves.nistP521,
            _ => ECCurve.NamedCurves.nistP256
        };

        using var ecdsa = ECDsa.Create(curve);

        var distinguishedName = new X500DistinguishedName(request.Subject);
        var certRequest = new CertificateRequest(
            distinguishedName,
            ecdsa,
            HashAlgorithmName.SHA256);

        // Add key usage
        AddKeyUsage(certRequest, request.KeyUsage);

        // Add Subject Alternative Names if provided
        if (request.SubjectAlternativeNames?.Any() == true)
        {
            var sanBuilder = new SubjectAlternativeNameBuilder();
            foreach (var san in request.SubjectAlternativeNames)
            {
                sanBuilder.AddDnsName(san);
            }
            certRequest.CertificateExtensions.Add(sanBuilder.Build());
        }

        // Add Subject Key Identifier
        certRequest.CertificateExtensions.Add(
            new X509SubjectKeyIdentifierExtension(certRequest.PublicKey, false));

        var notBefore = DateTimeOffset.UtcNow;
        var notAfter = notBefore.AddDays(request.ValidityDays);

        var cert = certRequest.CreateSelfSigned(notBefore, notAfter);

        var pfxBytes = cert.Export(X509ContentType.Pfx);

        // Use platform-appropriate flags
        var keyStorageFlags = OperatingSystem.IsMacOS()
            ? X509KeyStorageFlags.Exportable
            : X509KeyStorageFlags.MachineKeySet | X509KeyStorageFlags.Exportable;

        return new X509Certificate2(pfxBytes, (string?)null, keyStorageFlags);
    }

    private static void AddKeyUsage(CertificateRequest certRequest, CertificateKeyUsage usage)
    {
        var x509Usage = X509KeyUsageFlags.None;

        if (usage.HasFlag(CertificateKeyUsage.DigitalSignature))
            x509Usage |= X509KeyUsageFlags.DigitalSignature;

        if (usage.HasFlag(CertificateKeyUsage.KeyEncipherment))
            x509Usage |= X509KeyUsageFlags.KeyEncipherment;

        if (usage.HasFlag(CertificateKeyUsage.DataEncipherment))
            x509Usage |= X509KeyUsageFlags.DataEncipherment;

        if (usage.HasFlag(CertificateKeyUsage.NonRepudiation))
            x509Usage |= X509KeyUsageFlags.NonRepudiation;

        if (x509Usage != X509KeyUsageFlags.None)
        {
            certRequest.CertificateExtensions.Add(
                new X509KeyUsageExtension(x509Usage, critical: true));
        }
    }

    private static string ComputeSha256Thumbprint(X509Certificate2 cert)
    {
        using var sha256 = SHA256.Create();
        var hash = sha256.ComputeHash(cert.RawData);
        return Convert.ToBase64String(hash)
            .TrimEnd('=')
            .Replace('+', '-')
            .Replace('/', '_');
    }
}
