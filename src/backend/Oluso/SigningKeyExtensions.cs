using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Oluso.Core.Domain.Entities;
using Oluso.Core.Services;
using Oluso.Keys;

namespace Oluso;

/// <summary>
/// Extension methods for configuring signing key management
/// </summary>
public static class SigningKeyExtensions
{
    /// <summary>
    /// Adds signing key management with local key storage (encrypted in database).
    /// For production deployments, consider using AddAzureKeyVault() for HSM-backed keys.
    /// </summary>
    public static OlusoBuilder AddSigningKeys(
        this OlusoBuilder builder,
        Action<SigningKeyOptions>? configure = null)
    {
        var options = new SigningKeyOptions();
        configure?.Invoke(options);

        // Register core services
        builder.Services.TryAddScoped<ISigningKeyService, SigningKeyService>();
        builder.Services.TryAddSingleton<IKeyEncryptionService, DataProtectionKeyEncryptionService>();

        // Register local provider
        builder.Services.AddSingleton<IKeyMaterialProvider, LocalKeyMaterialProvider>();

        // Register provider registry
        builder.Services.TryAddSingleton<IKeyMaterialProviderRegistry>(sp =>
            new KeyMaterialProviderRegistry(
                sp.GetServices<IKeyMaterialProvider>(),
                options.DefaultStorageProvider));

        return builder;
    }

    /// <summary>
    /// Registers a custom key encryption service for encrypting local keys at rest
    /// </summary>
    public static OlusoBuilder AddKeyEncryptionService<TService>(this OlusoBuilder builder)
        where TService : class, IKeyEncryptionService
    {
        builder.Services.AddSingleton<IKeyEncryptionService, TService>();
        return builder;
    }

    /// <summary>
    /// Registers a custom key material provider
    /// </summary>
    public static OlusoBuilder AddKeyMaterialProvider<TProvider>(this OlusoBuilder builder)
        where TProvider : class, IKeyMaterialProvider
    {
        builder.Services.AddSingleton<IKeyMaterialProvider, TProvider>();
        return builder;
    }
}

/// <summary>
/// Options for signing key configuration
/// </summary>
public class SigningKeyOptions
{
    /// <summary>
    /// Default storage provider for new keys.
    /// Default is Local. Change to AzureKeyVault for production.
    /// </summary>
    public KeyStorageProvider DefaultStorageProvider { get; set; } = KeyStorageProvider.Local;

    /// <summary>
    /// Default key type for generated keys.
    /// RSA is more widely compatible, EC is more efficient.
    /// </summary>
    public SigningKeyType DefaultKeyType { get; set; } = SigningKeyType.RSA;

    /// <summary>
    /// Default algorithm for RSA keys
    /// </summary>
    public string DefaultRsaAlgorithm { get; set; } = "RS256";

    /// <summary>
    /// Default algorithm for EC keys
    /// </summary>
    public string DefaultEcAlgorithm { get; set; } = "ES256";

    /// <summary>
    /// Default RSA key size in bits
    /// </summary>
    public int DefaultRsaKeySize { get; set; } = 2048;

    /// <summary>
    /// Default key lifetime in days
    /// </summary>
    public int DefaultKeyLifetimeDays { get; set; } = 90;

    /// <summary>
    /// Days before expiration to generate new key for rotation
    /// </summary>
    public int RotationLeadDays { get; set; } = 14;

    /// <summary>
    /// Days after expiration to keep key for verification
    /// </summary>
    public int GracePeriodDays { get; set; } = 30;

    /// <summary>
    /// Maximum number of keys to retain per tenant/client
    /// </summary>
    public int MaxKeysPerContext { get; set; } = 5;

    /// <summary>
    /// Enable automatic key rotation background service
    /// </summary>
    public bool EnableAutoRotation { get; set; } = true;

    /// <summary>
    /// Interval between automatic rotation checks (in hours)
    /// </summary>
    public int AutoRotationIntervalHours { get; set; } = 24;
}
