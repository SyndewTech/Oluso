using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Logging;
using Microsoft.IdentityModel.Tokens;
using Oluso.Core.Domain.Entities;
using Oluso.Core.Domain.Interfaces;
using Oluso.Core.Licensing;
using Oluso.Core.Services;
using Oluso.Keys;

namespace Oluso.Enterprise.AzureKeyVault;

/// <summary>
/// Extension methods for registering Azure Key Vault services
/// </summary>
public static class AzureKeyVaultExtensions
{
    /// <summary>
    /// Adds Azure Key Vault as a key and certificate provider.
    /// Private keys never leave the vault - all signing operations are performed by Key Vault.
    /// Requires Professional or higher license unless SkipLicenseValidation is true.
    /// </summary>
    /// <remarks>
    /// This registers:
    /// - Azure Key Vault as a key material provider
    /// - Azure Key Vault as a certificate material provider
    /// - Production SigningCredentialStore that uses the key management system
    ///
    /// The production SigningCredentialStore replaces DevelopmentSigningCredentialStore
    /// and uses the full key management system with automatic key provisioning.
    ///
    /// Requires configuration:
    /// <code>
    /// {
    ///   "AzureKeyVault": {
    ///     "VaultUri": "https://your-vault.vault.azure.net/"
    ///   }
    /// }
    /// </code>
    ///
    /// Authentication uses DefaultAzureCredential which supports:
    /// - Managed Identity (recommended for production)
    /// - Azure CLI (for local development)
    /// - Environment variables (AZURE_CLIENT_ID, AZURE_CLIENT_SECRET, AZURE_TENANT_ID)
    /// </remarks>
    /// <example>
    /// <code>
    /// // Basic usage
    /// builder.Services.AddOluso(configuration)
    ///     .AddSigningKeys()
    ///     .AddOlusoAzureKeyVault();
    ///
    /// // With options - use Key Vault as default for new keys
    /// builder.Services.AddOluso(configuration)
    ///     .AddSigningKeys(opts => opts.DefaultStorageProvider = KeyStorageProvider.AzureKeyVault)
    ///     .AddOlusoAzureKeyVault();
    /// </code>
    /// </example>
    public static OlusoBuilder AddOlusoAzureKeyVault(
        this OlusoBuilder builder,
        Action<AzureKeyVaultOptions>? configure = null)
    {
        var options = new AzureKeyVaultOptions();
        configure?.Invoke(options);

        // Register license-aware Key Vault provider factory
        if (!options.SkipLicenseValidation)
        {
            builder.Services.AddSingleton<IKeyMaterialProvider>(sp =>
            {
                var licenseValidator = sp.GetService<ILicenseValidator>();
                if (licenseValidator != null)
                {
                    var result = licenseValidator.ValidateFeature(LicensedFeatures.KeyVault);
                    if (!result.IsValid)
                    {
                        var logger = sp.GetService<ILogger<AzureKeyVaultProvider>>();
                        logger?.LogWarning(
                            "Azure Key Vault feature requires Pro+ license. Key Vault provider will not be available. {Message}",
                            result.Message);
                        // Return null - the composite provider will skip unavailable providers
                        return new DisabledKeyMaterialProvider(KeyStorageProvider.AzureKeyVault);
                    }
                }
                return ActivatorUtilities.CreateInstance<AzureKeyVaultProvider>(sp);
            });

            builder.Services.AddSingleton<ICertificateMaterialProvider>(sp =>
            {
                var licenseValidator = sp.GetService<ILicenseValidator>();
                if (licenseValidator != null)
                {
                    var result = licenseValidator.ValidateFeature(LicensedFeatures.KeyVault);
                    if (!result.IsValid)
                    {
                        // Return disabled provider
                        return new DisabledCertificateMaterialProvider();
                    }
                }
                return ActivatorUtilities.CreateInstance<AzureKeyVaultCertificateProvider>(sp);
            });
        }
        else
        {
            // Skip license validation - register directly
            builder.Services.AddSingleton<IKeyMaterialProvider, AzureKeyVaultProvider>();
            builder.Services.AddSingleton<ICertificateMaterialProvider, AzureKeyVaultCertificateProvider>();
        }

        // Replace DevelopmentSigningCredentialStore with production SigningCredentialStore
        // This uses the key management system with ISigningKeyService
        builder.Services.RemoveAll<ISigningCredentialStore>();
        builder.Services.AddScoped<ISigningCredentialStore, SigningCredentialStore>();

        return builder;
    }

    /// <summary>
    /// Adds Azure Key Vault as a key and certificate provider (IServiceCollection extension).
    /// Use the OlusoBuilder overload for full integration including SigningCredentialStore.
    /// </summary>
    public static IServiceCollection AddOlusoAzureKeyVault(
        this IServiceCollection services,
        Action<AzureKeyVaultOptions>? configure = null)
    {
        var options = new AzureKeyVaultOptions();
        configure?.Invoke(options);

        // Register license-aware Key Vault provider factory
        if (!options.SkipLicenseValidation)
        {
            services.AddSingleton<IKeyMaterialProvider>(sp =>
            {
                var licenseValidator = sp.GetService<ILicenseValidator>();
                if (licenseValidator != null)
                {
                    var result = licenseValidator.ValidateFeature(LicensedFeatures.KeyVault);
                    if (!result.IsValid)
                    {
                        var logger = sp.GetService<ILogger<AzureKeyVaultProvider>>();
                        logger?.LogWarning(
                            "Azure Key Vault feature requires Pro+ license. Key Vault provider will not be available. {Message}",
                            result.Message);
                        return new DisabledKeyMaterialProvider(KeyStorageProvider.AzureKeyVault);
                    }
                }
                return ActivatorUtilities.CreateInstance<AzureKeyVaultProvider>(sp);
            });

            services.AddSingleton<ICertificateMaterialProvider>(sp =>
            {
                var licenseValidator = sp.GetService<ILicenseValidator>();
                if (licenseValidator != null)
                {
                    var result = licenseValidator.ValidateFeature(LicensedFeatures.KeyVault);
                    if (!result.IsValid)
                    {
                        return new DisabledCertificateMaterialProvider();
                    }
                }
                return ActivatorUtilities.CreateInstance<AzureKeyVaultCertificateProvider>(sp);
            });
        }
        else
        {
            // Skip license validation - register directly
            services.AddSingleton<IKeyMaterialProvider, AzureKeyVaultProvider>();
            services.AddSingleton<ICertificateMaterialProvider, AzureKeyVaultCertificateProvider>();
        }

        return services;
    }
}

/// <summary>
/// Disabled key material provider returned when license validation fails.
/// All operations return unavailable/null to gracefully degrade.
/// </summary>
internal class DisabledKeyMaterialProvider : IKeyMaterialProvider
{
    public KeyStorageProvider ProviderType { get; }

    public DisabledKeyMaterialProvider(KeyStorageProvider providerType)
    {
        ProviderType = providerType;
    }

    public Task<bool> IsAvailableAsync(CancellationToken cancellationToken = default)
        => Task.FromResult(false);

    public Task<KeyMaterialResult> GenerateKeyAsync(KeyGenerationParams request, CancellationToken cancellationToken = default)
        => throw new LicenseException($"{ProviderType} requires a Professional+ license");

    public Task<SigningCredentials?> GetSigningCredentialsAsync(SigningKey key, CancellationToken cancellationToken = default)
        => Task.FromResult<SigningCredentials?>(null);

    public Task<SecurityKey?> GetPublicKeyAsync(SigningKey key, CancellationToken cancellationToken = default)
        => Task.FromResult<SecurityKey?>(null);

    public Task<Microsoft.IdentityModel.Tokens.JsonWebKey?> GetJsonWebKeyAsync(SigningKey key, CancellationToken cancellationToken = default)
        => Task.FromResult<Microsoft.IdentityModel.Tokens.JsonWebKey?>(null);

    public Task DeleteKeyAsync(SigningKey key, CancellationToken cancellationToken = default)
        => Task.CompletedTask;
}

/// <summary>
/// Disabled certificate material provider returned when license validation fails.
/// </summary>
internal class DisabledCertificateMaterialProvider : ICertificateMaterialProvider
{
    public KeyStorageProvider ProviderType => KeyStorageProvider.AzureKeyVault;

    public Task<bool> IsAvailableAsync(CancellationToken cancellationToken = default)
        => Task.FromResult(false);

    public Task<CertificateMaterialResult> GenerateCertificateAsync(CertificateGenerationParams request, CancellationToken cancellationToken = default)
        => throw new LicenseException("Azure Key Vault certificates require a Professional+ license");

    public Task<System.Security.Cryptography.X509Certificates.X509Certificate2?> LoadCertificateAsync(SigningKey key, CancellationToken cancellationToken = default)
        => Task.FromResult<System.Security.Cryptography.X509Certificates.X509Certificate2?>(null);

    public Task DeleteCertificateAsync(SigningKey key, CancellationToken cancellationToken = default)
        => Task.CompletedTask;
}

/// <summary>
/// Options for Azure Key Vault configuration
/// </summary>
public class AzureKeyVaultOptions
{
    /// <summary>
    /// Azure Key Vault URI. If not set here, reads from configuration["AzureKeyVault:VaultUri"]
    /// </summary>
    public string? VaultUri { get; set; }

    /// <summary>
    /// Whether to use Azure Key Vault as the default key storage provider.
    /// When true, new keys will be created in Key Vault by default.
    /// </summary>
    public bool UseAsDefault { get; set; } = true;

    /// <summary>
    /// Whether to enable HSM-backed keys (Key Vault Premium tier required)
    /// </summary>
    public bool UseHsmKeys { get; set; } = false;

    /// <summary>
    /// Skip license validation (for development/testing only).
    /// In production, Azure Key Vault requires a Professional+ license.
    /// </summary>
    public bool SkipLicenseValidation { get; set; }
}
