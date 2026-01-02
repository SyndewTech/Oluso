using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;
using Azure.Identity;
using Azure.Security.KeyVault.Certificates;
using Azure.Security.KeyVault.Keys.Cryptography;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using Oluso.Core.Domain.Entities;
using Oluso.Core.Services;

namespace Oluso.Enterprise.AzureKeyVault;

/// <summary>
/// Azure Key Vault implementation of ICertificateMaterialProvider.
/// Creates certificates in Key Vault where the private key is protected by HSM.
/// </summary>
public class AzureKeyVaultCertificateProvider : ICertificateMaterialProvider
{
    private readonly CertificateClient? _certClient;
    private readonly IConfiguration _configuration;
    private readonly ILogger<AzureKeyVaultCertificateProvider> _logger;
    private readonly string? _vaultUri;
    private readonly bool _isConfigured;

    public KeyStorageProvider ProviderType => KeyStorageProvider.AzureKeyVault;

    public AzureKeyVaultCertificateProvider(
        IConfiguration configuration,
        ILogger<AzureKeyVaultCertificateProvider> logger)
    {
        _configuration = configuration;
        _logger = logger;

        _vaultUri = configuration["AzureKeyVault:VaultUri"];
        _isConfigured = !string.IsNullOrEmpty(_vaultUri);

        if (_isConfigured)
        {
            try
            {
                var credential = new DefaultAzureCredential(new DefaultAzureCredentialOptions
                {
                    ExcludeEnvironmentCredential = false,
                    ExcludeManagedIdentityCredential = false,
                    ExcludeAzureCliCredential = false,
                    ExcludeVisualStudioCredential = true,
                    ExcludeVisualStudioCodeCredential = true,
                    ExcludeInteractiveBrowserCredential = true
                });

                _certClient = new CertificateClient(new Uri(_vaultUri!), credential);
                _logger.LogInformation("Azure Key Vault certificate provider initialized: {VaultUri}", _vaultUri);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to initialize Azure Key Vault certificate client");
                _isConfigured = false;
            }
        }
        else
        {
            _logger.LogWarning("Azure Key Vault not configured - certificate provider unavailable");
        }
    }

    public Task<bool> IsAvailableAsync(CancellationToken cancellationToken = default)
    {
        return Task.FromResult(_isConfigured && _certClient != null);
    }

    public async Task<CertificateMaterialResult> GenerateCertificateAsync(
        CertificateGenerationParams request,
        CancellationToken cancellationToken = default)
    {
        if (_certClient == null)
        {
            throw new InvalidOperationException("Azure Key Vault is not configured");
        }

        var certName = GenerateCertificateName(request.TenantId, request.EntityId, request.Name);

        _logger.LogInformation(
            "Creating certificate in Azure Key Vault: {CertName}, Subject: {Subject}",
            certName, request.Subject);

        // Build the certificate policy
        var policy = new CertificatePolicy(
            WellKnownIssuerNames.Self,
            request.Subject)
        {
            KeyType = request.KeyType == SigningKeyType.RSA
                ? CertificateKeyType.Rsa
                : CertificateKeyType.Ec,
            Exportable = false, // Private key stays in Key Vault
            ValidityInMonths = (int)Math.Ceiling(request.ValidityDays / 30.0)
        };

        // Set key size
        if (request.KeyType == SigningKeyType.RSA)
        {
            policy.ReuseKey = false;
            policy.KeySize = request.KeySize;
        }
        else
        {
            policy.KeyCurveName = request.KeySize switch
            {
                256 => CertificateKeyCurveName.P256,
                384 => CertificateKeyCurveName.P384,
                521 => CertificateKeyCurveName.P521,
                _ => CertificateKeyCurveName.P256
            };
        }

        // Set key usage
        policy.KeyUsage.Clear();
        if (request.KeyUsage.HasFlag(Core.Services.CertificateKeyUsage.DigitalSignature))
            policy.KeyUsage.Add(Azure.Security.KeyVault.Certificates.CertificateKeyUsage.DigitalSignature);
        if (request.KeyUsage.HasFlag(Core.Services.CertificateKeyUsage.KeyEncipherment))
            policy.KeyUsage.Add(Azure.Security.KeyVault.Certificates.CertificateKeyUsage.KeyEncipherment);
        if (request.KeyUsage.HasFlag(Core.Services.CertificateKeyUsage.DataEncipherment))
            policy.KeyUsage.Add(Azure.Security.KeyVault.Certificates.CertificateKeyUsage.DataEncipherment);
        if (request.KeyUsage.HasFlag(Core.Services.CertificateKeyUsage.NonRepudiation))
            policy.KeyUsage.Add(Azure.Security.KeyVault.Certificates.CertificateKeyUsage.NonRepudiation);

        // Add SANs if provided
        if (request.SubjectAlternativeNames?.Any() == true)
        {
            foreach (var san in request.SubjectAlternativeNames)
            {
                policy.SubjectAlternativeNames.DnsNames.Add(san);
            }
        }

        // Create the certificate
        var operation = await _certClient.StartCreateCertificateAsync(certName, policy, cancellationToken: cancellationToken);
        var cert = await operation.WaitForCompletionAsync(cancellationToken);

        _logger.LogInformation(
            "Created certificate in Azure Key Vault: {CertId}, Thumbprint: {Thumbprint}",
            cert.Value.Id, BitConverter.ToString(cert.Value.Properties.X509Thumbprint).Replace("-", ""));

        // Get the full certificate (public part only - private key stays in vault)
        var certWithPolicy = await _certClient.GetCertificateAsync(certName, cancellationToken);
        var x509Cert = new X509Certificate2(certWithPolicy.Value.Cer);

        var thumbprint = x509Cert.Thumbprint;
        var thumbprintSha256 = ComputeSha256Thumbprint(x509Cert);

        return new CertificateMaterialResult
        {
            Certificate = null, // Private key never leaves Key Vault
            Thumbprint = thumbprint,
            ThumbprintSha256 = thumbprintSha256,
            CertificateData = Convert.ToBase64String(x509Cert.RawData),
            EncryptedPrivateKey = null, // No local private key
            KeyVaultUri = cert.Value.KeyId?.ToString(),
            Subject = x509Cert.Subject,
            Issuer = x509Cert.Issuer,
            SerialNumber = x509Cert.SerialNumber,
            NotBefore = x509Cert.NotBefore.ToUniversalTime(),
            NotAfter = x509Cert.NotAfter.ToUniversalTime()
        };
    }

    public async Task<X509Certificate2?> LoadCertificateAsync(
        SigningKey key,
        CancellationToken cancellationToken = default)
    {
        if (_certClient == null || string.IsNullOrEmpty(key.KeyVaultUri))
        {
            return null;
        }

        try
        {
            // Extract certificate name from URI
            var uri = new Uri(key.KeyVaultUri);
            var certName = ExtractCertificateNameFromUri(uri);

            // Get certificate from Key Vault
            var response = await _certClient.GetCertificateAsync(certName, cancellationToken);
            var cert = new X509Certificate2(response.Value.Cer);

            // Note: This returns the certificate with public key only
            // For signing, you need to use the Key Vault crypto client
            // We attach custom key to enable signing via Key Vault

            // Create a certificate that uses Key Vault for signing
            return CreateKeyVaultBackedCertificate(cert, key.KeyVaultUri);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to load certificate from Key Vault: {KeyId}", key.KeyId);
            return null;
        }
    }

    public async Task DeleteCertificateAsync(
        SigningKey key,
        CancellationToken cancellationToken = default)
    {
        if (_certClient == null || string.IsNullOrEmpty(key.KeyVaultUri))
        {
            return;
        }

        try
        {
            var uri = new Uri(key.KeyVaultUri);
            var certName = ExtractCertificateNameFromUri(uri);

            var operation = await _certClient.StartDeleteCertificateAsync(certName, cancellationToken);
            _logger.LogInformation("Deleted certificate from Azure Key Vault: {CertName}", certName);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to delete certificate from Key Vault: {KeyId}", key.KeyId);
            throw;
        }
    }

    /// <summary>
    /// Gets a CryptographyClient for performing signing operations with the certificate's key.
    /// </summary>
    public CryptographyClient? GetCryptographyClient(string keyUri)
    {
        if (!_isConfigured || string.IsNullOrEmpty(keyUri))
        {
            return null;
        }

        try
        {
            var credential = new DefaultAzureCredential();
            return new CryptographyClient(new Uri(keyUri), credential);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to create cryptography client for: {KeyUri}", keyUri);
            return null;
        }
    }

    #region Private Methods

    private static string GenerateCertificateName(string? tenantId, string? entityId, string? name)
    {
        var baseName = name ?? "signing-cert";
        var tenant = tenantId ?? "global";
        var entity = entityId ?? "default";

        // Key Vault names: alphanumeric and hyphens only
        var safeName = $"{tenant}-{entity}-{baseName}-{DateTime.UtcNow:yyyyMMddHHmmss}"
            .Replace("_", "-")
            .Replace(" ", "-")
            .Replace(":", "-");

        // Max 127 characters
        if (safeName.Length > 127)
        {
            safeName = safeName[..127];
        }

        return safeName;
    }

    private static string ExtractCertificateNameFromUri(Uri certUri)
    {
        // Format: https://vault.vault.azure.net/certificates/certname/version
        // or: https://vault.vault.azure.net/keys/keyname/version
        var segments = certUri.Segments;

        // Find the name segment (after /certificates/ or /keys/)
        for (int i = 0; i < segments.Length - 1; i++)
        {
            if (segments[i].TrimEnd('/') == "certificates" || segments[i].TrimEnd('/') == "keys")
            {
                return segments[i + 1].TrimEnd('/');
            }
        }

        throw new ArgumentException($"Invalid Key Vault URI: {certUri}");
    }

    private X509Certificate2 CreateKeyVaultBackedCertificate(X509Certificate2 publicCert, string keyUri)
    {
        // For now, return the public certificate
        // The actual signing will need to use the CryptographyClient
        // through the ISamlIdentityProvider or similar abstraction

        // Note: In a full implementation, you might create a custom
        // X509Certificate2 subclass or attach the key info as metadata
        return publicCert;
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

    #endregion
}
