using Azure;
using Azure.Core;
using Azure.Core.Cryptography;
using Azure.Identity;
using Azure.Security.KeyVault.Keys;
using Azure.Security.KeyVault.Keys.Cryptography;
using Azure.Storage.Blobs;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace Oluso.Enterprise.AzureDataProtection;

/// <summary>
/// One data protection key ring for every host of a deployment. Tenant secrets, journey
/// secrets and integration credentials are stored encrypted with these keys, so a host
/// that makes its own keys (the ASP.NET Core default) can't read what another host or an
/// earlier deploy wrote. The ring is kept in a blob and its keys are wrapped with a Key
/// Vault key; every host uses the same application name.
/// </summary>
public static class AzureDataProtectionExtensions
{
    /// <summary>
    /// Keep data protection keys in the configured blob, wrapped with the configured Key
    /// Vault key (<see cref="OlusoDataProtectionOptions.SectionName"/>).
    /// </summary>
    /// <remarks>
    /// A host with <see cref="OlusoDataProtectionOptions.ReadOnly"/> set only reads the ring:
    /// it never creates keys, so it needs no vault address, only an identity that may
    /// unwrap with the vault key (the wrapped key names its vault key). Exactly one kind of
    /// host — the platform — should create keys.
    /// </remarks>
    public static IDataProtectionBuilder AddOlusoAzureDataProtection(
        this IServiceCollection services,
        IConfiguration configuration,
        Action<OlusoDataProtectionOptions>? configure = null)
    {
        var options = new OlusoDataProtectionOptions();
        configuration.GetSection(OlusoDataProtectionOptions.SectionName).Bind(options);
        configure?.Invoke(options);
        options.Validate();

        var credential = new DefaultAzureCredential();
        var builder = services.AddDataProtection()
            .SetApplicationName(options.ApplicationName)
            .PersistKeysToAzureBlobStorage(_ =>
            {
                var container = new BlobContainerClient(options.BlobConnectionString, options.ContainerName);
                container.CreateIfNotExists();
                return container.GetBlobClient(options.BlobName);
            });

        if (options.ReadOnly)
        {
            services.AddSingleton<IKeyEncryptionKeyResolver>(new KeyResolver(credential));
            builder.DisableAutomaticKeyGeneration();
            return builder;
        }

        builder.ProtectKeysWithAzureKeyVault(
            sp => EnsureKey(options, credential, sp.GetService<ILoggerFactory>()?.CreateLogger(typeof(AzureDataProtectionExtensions))),
            _ => credential);
        return builder;
    }

    private static string EnsureKey(OlusoDataProtectionOptions options, TokenCredential credential, ILogger? logger)
    {
        var client = new KeyClient(new Uri(options.KeyVaultUri!), credential);
        try
        {
            client.GetKey(options.KeyName);
        }
        catch (RequestFailedException ex) when (ex.Status == 404)
        {
            client.CreateRsaKey(new CreateRsaKeyOptions(options.KeyName) { KeySize = 3072 });
            logger?.LogInformation("Created Key Vault key {KeyName} to wrap data protection keys", options.KeyName);
        }

        return new Uri(new Uri(options.KeyVaultUri!), $"keys/{options.KeyName}").ToString();
    }
}

/// <summary>
/// Where the shared data protection key ring is kept (<c>Oluso:DataProtection</c>).
/// </summary>
public class OlusoDataProtectionOptions
{
    public const string SectionName = "Oluso:DataProtection";

    /// <summary>
    /// Shared by every host that reads the same protected data. Changing it makes
    /// everything already protected unreadable.
    /// </summary>
    public string ApplicationName { get; set; } = "Oluso";

    /// <summary>Storage account holding the key ring.</summary>
    public string? BlobConnectionString { get; set; }

    public string ContainerName { get; set; } = "dataprotection";

    public string BlobName { get; set; } = "keys.xml";

    /// <summary>Vault whose key wraps the ring's keys. Not needed by read-only hosts.</summary>
    public string? KeyVaultUri { get; set; }

    /// <summary>Key in <see cref="KeyVaultUri"/> that wraps the ring's keys; created when missing.</summary>
    public string KeyName { get; set; } = "oluso-dataprotection";

    /// <summary>
    /// Read the ring without creating keys (function apps and companion sites). The
    /// platform host creates and rotates them.
    /// </summary>
    public bool ReadOnly { get; set; }

    internal void Validate()
    {
        if (string.IsNullOrWhiteSpace(BlobConnectionString))
            throw new InvalidOperationException($"{SectionName}:BlobConnectionString is required to share the data protection key ring.");
        if (!ReadOnly && string.IsNullOrWhiteSpace(KeyVaultUri))
            throw new InvalidOperationException($"{SectionName}:KeyVaultUri is required on the host that creates data protection keys.");
    }
}
