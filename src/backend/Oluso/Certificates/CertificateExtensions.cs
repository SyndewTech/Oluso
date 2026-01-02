using Microsoft.Extensions.DependencyInjection;
using Oluso.Core.Services;

namespace Oluso.Certificates;

/// <summary>
/// Extension methods for configuring certificate services.
/// </summary>
public static class CertificateExtensions
{
    /// <summary>
    /// Configures certificate management options.
    /// Certificate services are automatically registered by AddOluso().
    /// Use this method to customize certificate options.
    /// </summary>
    /// <remarks>
    /// Certificate services (ICertificateService, local provider) are automatically
    /// included when you call AddOluso(). This method is only needed if you want
    /// to customize the certificate options.
    ///
    /// To add Azure Key Vault certificate support, use AddOlusoAzureKeyVault().
    /// </remarks>
    /// <example>
    /// <code>
    /// builder.Services.AddOluso(configuration)
    ///     .ConfigureCertificates(options =>
    ///     {
    ///         options.DefaultValidityDays = 730;
    ///         options.AutoGenerateSelfSigned = true;
    ///     });
    /// </code>
    /// </example>
    public static OlusoBuilder ConfigureCertificates(
        this OlusoBuilder builder,
        Action<CertificateOptions> configure)
    {
        var options = new CertificateOptions();
        configure(options);
        builder.Services.AddSingleton(options);
        return builder;
    }

    /// <summary>
    /// Adds a custom certificate material provider.
    /// The provider will be used alongside the default local provider.
    /// </summary>
    /// <typeparam name="TProvider">The custom provider type</typeparam>
    /// <example>
    /// <code>
    /// builder.Services.AddOluso(configuration)
    ///     .AddCertificateProvider&lt;MyCustomCertificateProvider&gt;();
    /// </code>
    /// </example>
    public static OlusoBuilder AddCertificateProvider<TProvider>(this OlusoBuilder builder)
        where TProvider : class, ICertificateMaterialProvider
    {
        builder.Services.AddSingleton<ICertificateMaterialProvider, TProvider>();
        return builder;
    }
}

/// <summary>
/// Configuration options for certificate management.
/// </summary>
public class CertificateOptions
{
    /// <summary>
    /// Default key type for generated certificates.
    /// </summary>
    public Core.Domain.Entities.SigningKeyType DefaultKeyType { get; set; } = Core.Domain.Entities.SigningKeyType.RSA;

    /// <summary>
    /// Default key size for RSA certificates.
    /// </summary>
    public int DefaultRsaKeySize { get; set; } = 2048;

    /// <summary>
    /// Default key size for EC certificates (256, 384, 521).
    /// </summary>
    public int DefaultEcKeySize { get; set; } = 256;

    /// <summary>
    /// Default validity period in days for self-signed certificates.
    /// </summary>
    public int DefaultValidityDays { get; set; } = 365;

    /// <summary>
    /// Default subject format for self-signed certificates.
    /// {0} is replaced with the certificate purpose.
    /// </summary>
    public string DefaultSubjectFormat { get; set; } = "CN=Oluso {0} Certificate, O=Oluso";

    /// <summary>
    /// Whether to auto-generate self-signed certificates when not found.
    /// </summary>
    public bool AutoGenerateSelfSigned { get; set; } = true;

    /// <summary>
    /// Preferred storage provider for certificates.
    /// </summary>
    public Core.Domain.Entities.KeyStorageProvider DefaultStorageProvider { get; set; } =
        Core.Domain.Entities.KeyStorageProvider.Local;

    /// <summary>
    /// Whether to log warnings when using self-signed certificates.
    /// </summary>
    public bool WarnOnSelfSigned { get; set; } = true;

    /// <summary>
    /// Days before expiration to start warning about certificate renewal.
    /// </summary>
    public int ExpirationWarningDays { get; set; } = 30;
}
