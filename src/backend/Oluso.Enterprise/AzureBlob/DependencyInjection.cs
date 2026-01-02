using Microsoft.Extensions.DependencyInjection;
using Oluso.Core.Storage;

namespace Oluso.Enterprise.AzureBlob;

/// <summary>
/// Extension methods for registering Azure Blob file uploader
/// </summary>
public static class AzureBlobExtensions
{
    /// <summary>
    /// Add Azure Blob Storage file uploader
    /// </summary>
    public static IServiceCollection AddAzureBlobFileUploader(
        this IServiceCollection services,
        Action<AzureBlobOptions> configure)
    {
        services.Configure(configure);
        services.AddSingleton<IFileUploader, AzureBlobFileUploader>();
        return services;
    }

    /// <summary>
    /// Add Azure Blob Storage file uploader with connection string
    /// </summary>
    public static IServiceCollection AddAzureBlobFileUploader(
        this IServiceCollection services,
        string connectionString,
        string containerName,
        Action<AzureBlobOptions>? additionalConfig = null)
    {
        services.Configure<AzureBlobOptions>(options =>
        {
            options.ConnectionString = connectionString;
            options.ContainerName = containerName;
            additionalConfig?.Invoke(options);
        });

        services.AddSingleton<IFileUploader, AzureBlobFileUploader>();
        return services;
    }
}
