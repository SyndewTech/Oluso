using System.Security.Cryptography;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Oluso.Core.Domain.Entities;
using Oluso.Core.Storage;
using FileInfo = Oluso.Core.Storage.FileInfo;

namespace Oluso.Storage;

/// <summary>
/// Local file system implementation of IFileUploader
/// </summary>
public class LocalFileUploader : IFileUploader
{
    private readonly LocalFileOptions _options;
    private readonly ILogger<LocalFileUploader> _logger;

    public FileStorageProvider Provider => FileStorageProvider.Local;

    public LocalFileUploader(
        IOptions<LocalFileOptions> options,
        ILogger<LocalFileUploader> logger)
    {
        _options = options.Value;
        _logger = logger;

        if (_options.CreateDirectoryIfNotExists && !Directory.Exists(_options.BaseDirectory))
        {
            Directory.CreateDirectory(_options.BaseDirectory);
            _logger.LogInformation("Created storage directory: {Directory}", _options.BaseDirectory);
        }
    }

    public LocalFileUploader(
        LocalFileOptions options,
        ILogger<LocalFileUploader> logger)
    {
        _options = options;
        _logger = logger;

        if (_options.CreateDirectoryIfNotExists && !Directory.Exists(_options.BaseDirectory))
        {
            Directory.CreateDirectory(_options.BaseDirectory);
            _logger.LogInformation("Created storage directory: {Directory}", _options.BaseDirectory);
        }
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

            var fullPath = GetFullPath(path);
            var directory = Path.GetDirectoryName(fullPath);

            if (!string.IsNullOrEmpty(directory) && !Directory.Exists(directory))
            {
                Directory.CreateDirectory(directory);
            }

            await File.WriteAllBytesAsync(fullPath, content, cancellationToken);

            string? contentHash = null;
            if (_options.ComputeContentHash)
            {
                contentHash = ComputeHash(content);
            }

            // Store metadata in a sidecar file if provided
            if (metadata != null && metadata.Count > 0)
            {
                await SaveMetadataAsync(fullPath, metadata, cancellationToken);
            }

            _logger.LogDebug("Uploaded file: {Path} ({Size} bytes)", path, content.Length);

            return FileUploadResult.Ok(fullPath, Provider, content.Length, contentHash);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to upload file: {Path}", path);
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
        var fullPath = GetFullPath(path);

        if (!File.Exists(fullPath))
        {
            return null;
        }

        return await File.ReadAllBytesAsync(fullPath, cancellationToken);
    }

    public async Task<Stream?> DownloadStreamAsync(string path, CancellationToken cancellationToken = default)
    {
        var fullPath = GetFullPath(path);

        if (!File.Exists(fullPath))
        {
            return null;
        }

        var bytes = await File.ReadAllBytesAsync(fullPath, cancellationToken);
        return new MemoryStream(bytes);
    }

    public Task<bool> DeleteAsync(string path, CancellationToken cancellationToken = default)
    {
        var fullPath = GetFullPath(path);

        if (!File.Exists(fullPath))
        {
            return Task.FromResult(false);
        }

        File.Delete(fullPath);

        // Also delete metadata file if exists
        var metadataPath = fullPath + ".meta";
        if (File.Exists(metadataPath))
        {
            File.Delete(metadataPath);
        }

        _logger.LogDebug("Deleted file: {Path}", path);
        return Task.FromResult(true);
    }

    public Task<bool> ExistsAsync(string path, CancellationToken cancellationToken = default)
    {
        var fullPath = GetFullPath(path);
        return Task.FromResult(File.Exists(fullPath));
    }

    public async Task<FileInfo?> GetInfoAsync(string path, CancellationToken cancellationToken = default)
    {
        var fullPath = GetFullPath(path);

        if (!File.Exists(fullPath))
        {
            return null;
        }

        var fileInfo = new System.IO.FileInfo(fullPath);
        var metadata = await LoadMetadataAsync(fullPath, cancellationToken);

        return new FileInfo
        {
            Path = path,
            Name = fileInfo.Name,
            SizeBytes = fileInfo.Length,
            CreatedAt = fileInfo.CreationTimeUtc,
            LastModifiedAt = fileInfo.LastWriteTimeUtc,
            Metadata = metadata
        };
    }

    public Task<IEnumerable<FileInfo>> ListAsync(
        string? prefix = null,
        int? maxResults = null,
        CancellationToken cancellationToken = default)
    {
        var searchPath = string.IsNullOrEmpty(prefix)
            ? _options.BaseDirectory
            : GetFullPath(prefix);

        if (!Directory.Exists(searchPath))
        {
            // If prefix is a partial path, search in parent
            searchPath = Path.GetDirectoryName(searchPath) ?? _options.BaseDirectory;
        }

        var files = Directory.GetFiles(searchPath, "*", SearchOption.AllDirectories)
            .Where(f => !f.EndsWith(".meta")) // Exclude metadata files
            .Where(f => string.IsNullOrEmpty(prefix) || GetRelativePath(f).StartsWith(prefix))
            .Take(maxResults ?? int.MaxValue)
            .Select(f =>
            {
                var fileInfo = new System.IO.FileInfo(f);
                return new FileInfo
                {
                    Path = GetRelativePath(f),
                    Name = fileInfo.Name,
                    SizeBytes = fileInfo.Length,
                    CreatedAt = fileInfo.CreationTimeUtc,
                    LastModifiedAt = fileInfo.LastWriteTimeUtc
                };
            });

        return Task.FromResult(files);
    }

    public async Task<FileUploadResult> CopyAsync(
        string sourcePath,
        string destinationPath,
        CancellationToken cancellationToken = default)
    {
        var sourceFullPath = GetFullPath(sourcePath);
        var destFullPath = GetFullPath(destinationPath);

        if (!File.Exists(sourceFullPath))
        {
            return FileUploadResult.Fail("Source file not found", Provider);
        }

        var directory = Path.GetDirectoryName(destFullPath);
        if (!string.IsNullOrEmpty(directory) && !Directory.Exists(directory))
        {
            Directory.CreateDirectory(directory);
        }

        File.Copy(sourceFullPath, destFullPath, overwrite: true);

        // Copy metadata file if exists
        var sourceMetaPath = sourceFullPath + ".meta";
        var destMetaPath = destFullPath + ".meta";
        if (File.Exists(sourceMetaPath))
        {
            File.Copy(sourceMetaPath, destMetaPath, overwrite: true);
        }

        var fileInfo = new System.IO.FileInfo(destFullPath);

        string? contentHash = null;
        if (_options.ComputeContentHash)
        {
            var content = await File.ReadAllBytesAsync(destFullPath, cancellationToken);
            contentHash = ComputeHash(content);
        }

        return FileUploadResult.Ok(destFullPath, Provider, fileInfo.Length, contentHash);
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
        // Local file system doesn't support signed URLs
        return Task.FromResult<string?>(null);
    }

    private string GetFullPath(string path)
    {
        // Normalize path separators and remove any leading separators
        path = path.Replace('\\', '/').TrimStart('/');

        // Combine with base directory
        var fullPath = Path.Combine(_options.BaseDirectory, path);

        // Normalize for the current OS
        fullPath = Path.GetFullPath(fullPath);

        // Security: ensure path is within base directory
        if (!fullPath.StartsWith(_options.BaseDirectory))
        {
            throw new InvalidOperationException("Path traversal detected");
        }

        return fullPath;
    }

    private string GetRelativePath(string fullPath)
    {
        return Path.GetRelativePath(_options.BaseDirectory, fullPath).Replace('\\', '/');
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

    private async Task SaveMetadataAsync(
        string filePath,
        IDictionary<string, string> metadata,
        CancellationToken cancellationToken)
    {
        var metadataPath = filePath + ".meta";
        var json = System.Text.Json.JsonSerializer.Serialize(metadata);
        await File.WriteAllTextAsync(metadataPath, json, cancellationToken);
    }

    private async Task<IDictionary<string, string>?> LoadMetadataAsync(
        string filePath,
        CancellationToken cancellationToken)
    {
        var metadataPath = filePath + ".meta";

        if (!File.Exists(metadataPath))
        {
            return null;
        }

        var json = await File.ReadAllTextAsync(metadataPath, cancellationToken);
        return System.Text.Json.JsonSerializer.Deserialize<Dictionary<string, string>>(json);
    }
}
