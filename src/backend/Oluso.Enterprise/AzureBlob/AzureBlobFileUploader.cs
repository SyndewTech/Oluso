using System.Security.Cryptography;
using Azure.Storage.Blobs;
using Azure.Storage.Blobs.Models;
using Azure.Storage.Sas;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Oluso.Core.Domain.Entities;
using Oluso.Core.Storage;
using FileInfo = Oluso.Core.Storage.FileInfo;

namespace Oluso.Enterprise.AzureBlob;

/// <summary>
/// Azure Blob Storage implementation of IFileUploader
/// </summary>
public class AzureBlobFileUploader : IFileUploader
{
    private readonly BlobContainerClient _containerClient;
    private readonly AzureBlobOptions _options;
    private readonly ILogger<AzureBlobFileUploader> _logger;

    public FileStorageProvider Provider => FileStorageProvider.AzureBlob;

    public AzureBlobFileUploader(
        IOptions<AzureBlobOptions> options,
        ILogger<AzureBlobFileUploader> logger)
    {
        _options = options.Value;
        _logger = logger;

        var blobServiceClient = new BlobServiceClient(_options.ConnectionString);
        _containerClient = blobServiceClient.GetBlobContainerClient(_options.ContainerName);

        if (_options.CreateContainerIfNotExists)
        {
            _containerClient.CreateIfNotExists();
            _logger.LogInformation("Ensured Azure Blob container exists: {Container}", _options.ContainerName);
        }
    }

    public AzureBlobFileUploader(
        BlobContainerClient containerClient,
        AzureBlobOptions options,
        ILogger<AzureBlobFileUploader> logger)
    {
        _containerClient = containerClient;
        _options = options;
        _logger = logger;
    }

    public async Task<FileUploadResult> UploadAsync(
        string path,
        byte[] content,
        string? contentType = null,
        IDictionary<string, string>? metadata = null,
        CancellationToken cancellationToken = default)
    {
        try
        {
            if (_options.MaxFileSizeBytes > 0 && content.Length > _options.MaxFileSizeBytes)
            {
                return FileUploadResult.Fail(
                    $"File exceeds maximum size of {_options.MaxFileSizeBytes} bytes",
                    Provider);
            }

            var blobPath = GetBlobPath(path);
            var blobClient = _containerClient.GetBlobClient(blobPath);

            var uploadOptions = new BlobUploadOptions
            {
                HttpHeaders = new BlobHttpHeaders
                {
                    ContentType = contentType ?? _options.DefaultContentType
                }
            };

            if (metadata != null && metadata.Count > 0)
            {
                uploadOptions.Metadata = metadata.ToDictionary(k => k.Key, v => v.Value);
            }

            if (!string.IsNullOrEmpty(_options.AccessTier))
            {
                uploadOptions.AccessTier = new Azure.Storage.Blobs.Models.AccessTier(_options.AccessTier);
            }

            var response = await blobClient.UploadAsync(
                new BinaryData(content),
                uploadOptions,
                cancellationToken);

            string? contentHash = null;
            if (_options.ComputeContentHash)
            {
                contentHash = ComputeHash(content);
            }

            _logger.LogDebug("Uploaded blob: {Path} ({Size} bytes)", path, content.Length);

            return new FileUploadResult
            {
                Success = true,
                StorageReference = blobClient.Uri.ToString(),
                Provider = Provider,
                SizeBytes = content.Length,
                ContentHash = contentHash,
                ETag = response.Value.ETag.ToString()
            };
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to upload blob: {Path}", path);
            return FileUploadResult.Fail(ex.Message, Provider);
        }
    }

    public async Task<FileUploadResult> UploadAsync(
        string path,
        Stream content,
        string? contentType = null,
        IDictionary<string, string>? metadata = null,
        CancellationToken cancellationToken = default)
    {
        using var ms = new MemoryStream();
        await content.CopyToAsync(ms, cancellationToken);
        return await UploadAsync(path, ms.ToArray(), contentType, metadata, cancellationToken);
    }

    public async Task<byte[]?> DownloadAsync(string path, CancellationToken cancellationToken = default)
    {
        try
        {
            var blobPath = GetBlobPath(path);
            var blobClient = _containerClient.GetBlobClient(blobPath);

            if (!await blobClient.ExistsAsync(cancellationToken))
            {
                return null;
            }

            var response = await blobClient.DownloadContentAsync(cancellationToken);
            return response.Value.Content.ToArray();
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to download blob: {Path}", path);
            return null;
        }
    }

    public async Task<Stream?> DownloadStreamAsync(string path, CancellationToken cancellationToken = default)
    {
        try
        {
            var blobPath = GetBlobPath(path);
            var blobClient = _containerClient.GetBlobClient(blobPath);

            if (!await blobClient.ExistsAsync(cancellationToken))
            {
                return null;
            }

            var response = await blobClient.DownloadStreamingAsync(cancellationToken: cancellationToken);
            return response.Value.Content;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to download blob stream: {Path}", path);
            return null;
        }
    }

    public async Task<bool> DeleteAsync(string path, CancellationToken cancellationToken = default)
    {
        try
        {
            var blobPath = GetBlobPath(path);
            var blobClient = _containerClient.GetBlobClient(blobPath);

            var response = await blobClient.DeleteIfExistsAsync(cancellationToken: cancellationToken);

            if (response.Value)
            {
                _logger.LogDebug("Deleted blob: {Path}", path);
            }

            return response.Value;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to delete blob: {Path}", path);
            return false;
        }
    }

    public async Task<bool> ExistsAsync(string path, CancellationToken cancellationToken = default)
    {
        var blobPath = GetBlobPath(path);
        var blobClient = _containerClient.GetBlobClient(blobPath);
        return await blobClient.ExistsAsync(cancellationToken);
    }

    public async Task<FileInfo?> GetInfoAsync(string path, CancellationToken cancellationToken = default)
    {
        try
        {
            var blobPath = GetBlobPath(path);
            var blobClient = _containerClient.GetBlobClient(blobPath);

            if (!await blobClient.ExistsAsync(cancellationToken))
            {
                return null;
            }

            var props = await blobClient.GetPropertiesAsync(cancellationToken: cancellationToken);

            return new FileInfo
            {
                Path = path,
                Name = Path.GetFileName(blobPath),
                SizeBytes = props.Value.ContentLength,
                ContentType = props.Value.ContentType,
                ContentHash = props.Value.ContentHash != null
                    ? Convert.ToHexString(props.Value.ContentHash).ToLowerInvariant()
                    : null,
                ETag = props.Value.ETag.ToString(),
                CreatedAt = props.Value.CreatedOn.UtcDateTime,
                LastModifiedAt = props.Value.LastModified.UtcDateTime,
                Metadata = props.Value.Metadata?.ToDictionary(k => k.Key, v => v.Value)
            };
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to get blob info: {Path}", path);
            return null;
        }
    }

    public async Task<IEnumerable<FileInfo>> ListAsync(
        string? prefix = null,
        int? maxResults = null,
        CancellationToken cancellationToken = default)
    {
        var results = new List<FileInfo>();
        var blobPrefix = string.IsNullOrEmpty(prefix) ? _options.BasePath : GetBlobPath(prefix);

        await foreach (var blob in _containerClient.GetBlobsAsync(
            prefix: blobPrefix,
            cancellationToken: cancellationToken))
        {
            if (maxResults.HasValue && results.Count >= maxResults.Value)
            {
                break;
            }

            results.Add(new FileInfo
            {
                Path = GetRelativePath(blob.Name),
                Name = Path.GetFileName(blob.Name),
                SizeBytes = blob.Properties.ContentLength ?? 0,
                ContentType = blob.Properties.ContentType,
                ContentHash = blob.Properties.ContentHash != null
                    ? Convert.ToHexString(blob.Properties.ContentHash).ToLowerInvariant()
                    : null,
                ETag = blob.Properties.ETag?.ToString(),
                CreatedAt = blob.Properties.CreatedOn?.UtcDateTime,
                LastModifiedAt = blob.Properties.LastModified?.UtcDateTime
            });
        }

        return results;
    }

    public async Task<FileUploadResult> CopyAsync(
        string sourcePath,
        string destinationPath,
        CancellationToken cancellationToken = default)
    {
        try
        {
            var sourceBlobPath = GetBlobPath(sourcePath);
            var destBlobPath = GetBlobPath(destinationPath);

            var sourceClient = _containerClient.GetBlobClient(sourceBlobPath);
            var destClient = _containerClient.GetBlobClient(destBlobPath);

            if (!await sourceClient.ExistsAsync(cancellationToken))
            {
                return FileUploadResult.Fail("Source blob not found", Provider);
            }

            var copyOperation = await destClient.StartCopyFromUriAsync(
                sourceClient.Uri,
                cancellationToken: cancellationToken);

            await copyOperation.WaitForCompletionAsync(cancellationToken);

            var props = await destClient.GetPropertiesAsync(cancellationToken: cancellationToken);

            return new FileUploadResult
            {
                Success = true,
                StorageReference = destClient.Uri.ToString(),
                Provider = Provider,
                SizeBytes = props.Value.ContentLength,
                ETag = props.Value.ETag.ToString()
            };
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to copy blob: {Source} -> {Dest}", sourcePath, destinationPath);
            return FileUploadResult.Fail(ex.Message, Provider);
        }
    }

    public async Task<FileUploadResult> MoveAsync(
        string sourcePath,
        string destinationPath,
        CancellationToken cancellationToken = default)
    {
        var result = await CopyAsync(sourcePath, destinationPath, cancellationToken);

        if (result.Success)
        {
            await DeleteAsync(sourcePath, cancellationToken);
        }

        return result;
    }

    public Task<string?> GetSignedUrlAsync(
        string path,
        TimeSpan expiration,
        SignedUrlPermissions permissions = SignedUrlPermissions.Read,
        CancellationToken cancellationToken = default)
    {
        try
        {
            var blobPath = GetBlobPath(path);
            var blobClient = _containerClient.GetBlobClient(blobPath);

            if (!blobClient.CanGenerateSasUri)
            {
                _logger.LogWarning("Cannot generate SAS URI - container client must be created with account credentials");
                return Task.FromResult<string?>(null);
            }

            var sasPermissions = BlobSasPermissions.None;
            if (permissions.HasFlag(SignedUrlPermissions.Read))
                sasPermissions |= BlobSasPermissions.Read;
            if (permissions.HasFlag(SignedUrlPermissions.Write))
                sasPermissions |= BlobSasPermissions.Write | BlobSasPermissions.Create;
            if (permissions.HasFlag(SignedUrlPermissions.Delete))
                sasPermissions |= BlobSasPermissions.Delete;

            var sasBuilder = new BlobSasBuilder(sasPermissions, DateTimeOffset.UtcNow.Add(expiration))
            {
                BlobContainerName = _containerClient.Name,
                BlobName = blobPath
            };

            var sasUri = blobClient.GenerateSasUri(sasBuilder);
            return Task.FromResult<string?>(sasUri.ToString());
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to generate signed URL: {Path}", path);
            return Task.FromResult<string?>(null);
        }
    }

    private string GetBlobPath(string path)
    {
        // Normalize path
        path = path.Replace('\\', '/').TrimStart('/');

        // Combine with base path if configured
        if (!string.IsNullOrEmpty(_options.BasePath))
        {
            path = $"{_options.BasePath.TrimEnd('/')}/{path}";
        }

        return path;
    }

    private string GetRelativePath(string blobPath)
    {
        if (!string.IsNullOrEmpty(_options.BasePath) && blobPath.StartsWith(_options.BasePath))
        {
            return blobPath.Substring(_options.BasePath.Length).TrimStart('/');
        }
        return blobPath;
    }

    private string ComputeHash(byte[] content)
    {
        using var hasher = _options.HashAlgorithm.ToUpperInvariant() switch
        {
            "MD5" => (HashAlgorithm)MD5.Create(),
            "SHA256" => SHA256.Create(),
            "SHA512" => SHA512.Create(),
            _ => SHA256.Create()
        };

        var hashBytes = hasher.ComputeHash(content);
        return Convert.ToHexString(hashBytes).ToLowerInvariant();
    }
}
