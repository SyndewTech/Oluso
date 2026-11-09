using Oluso.Core.Domain.Entities;

namespace Oluso.Core.Storage;

/// <summary>
/// Abstraction for file storage operations.
/// Supports multiple backends: local file system, Azure Blob, S3, GCS, etc.
/// </summary>
public interface IFileUploader
{
    /// <summary>
    /// The storage provider type this uploader uses
    /// </summary>
    FileStorageProvider Provider { get; }

    /// <summary>
    /// Upload a file
    /// </summary>
    /// <param name="path">Relative path/key for the file</param>
    /// <param name="content">File content</param>
    /// <param name="contentType">MIME content type (optional)</param>
    /// <param name="metadata">Additional metadata to store with the file</param>
    /// <param name="cancellationToken">Cancellation token</param>
    /// <returns>Storage reference (full path/URI)</returns>
    Task<FileUploadResult> UploadAsync(
        string path,
        byte[] content,
        string? contentType = null,
        IDictionary<string, string>? metadata = null,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Upload a file from a stream
    /// </summary>
    Task<FileUploadResult> UploadAsync(
        string path,
        Stream content,
        string? contentType = null,
        IDictionary<string, string>? metadata = null,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Upload a file at the given <paramref name="accessLevel"/>. Public uploads
    /// are stored so they can be served anonymously (a dedicated public container
    /// when configured, otherwise the default location served via the prefix-
    /// guarded public endpoint) and <see cref="FileUploadResult.PublicUrl"/> is
    /// populated with a stable, non-expiring URL when one is available.
    ///
    /// <para>Default implementation ignores the access level and delegates to the
    /// private <see cref="UploadAsync(string, byte[], string?, IDictionary{string, string}?, CancellationToken)"/>
    /// — implementations that genuinely support public storage override this.
    /// Existing behaviour (and every existing caller) is therefore unchanged.</para>
    /// </summary>
    Task<FileUploadResult> UploadAsync(
        string path,
        byte[] content,
        FileAccessLevel accessLevel,
        string? contentType = null,
        IDictionary<string, string>? metadata = null,
        CancellationToken cancellationToken = default)
        => UploadAsync(path, content, contentType, metadata, cancellationToken);

    /// <summary>
    /// Stream overload of the access-level-aware upload. Default implementation
    /// delegates to the private stream upload.
    /// </summary>
    Task<FileUploadResult> UploadAsync(
        string path,
        Stream content,
        FileAccessLevel accessLevel,
        string? contentType = null,
        IDictionary<string, string>? metadata = null,
        CancellationToken cancellationToken = default)
        => UploadAsync(path, content, contentType, metadata, cancellationToken);

    /// <summary>
    /// Resolve the stable, non-expiring public URL for an already-stored public
    /// path (e.g. a CDN or public-container URL). Returns <c>null</c> when the
    /// provider cannot self-host a public URL (e.g. local storage, or a private
    /// container with no CDN) — callers then fall back to the anonymous public
    /// serve endpoint. Never returns a URL for a private path.
    /// </summary>
    string? GetPublicUrl(string path, FileAccessLevel accessLevel) => null;

    /// <summary>
    /// Access-level-aware download. Critical for the anonymous public serve
    /// endpoint: a server-side download uses the storage account's own
    /// credentials and is NOT gated by SAS, so reading through the private
    /// container would expose any blob (e.g. <c>sensitive/…</c>) by path alone.
    /// When a dedicated public container is configured, the Public overload
    /// resolves reads against THAT container, so the public endpoint physically
    /// cannot address private blobs — container isolation backing up the path
    /// guard. Default implementation delegates to the private download (single-
    /// container / proxy mode), where the path guard is the sole control.
    /// </summary>
    Task<byte[]?> DownloadAsync(string path, FileAccessLevel accessLevel, CancellationToken cancellationToken = default)
        => DownloadAsync(path, cancellationToken);

    /// <summary>
    /// Stream form of the access-level-aware download. See
    /// <see cref="DownloadAsync(string, FileAccessLevel, CancellationToken)"/>.
    /// </summary>
    Task<Stream?> DownloadStreamAsync(string path, FileAccessLevel accessLevel, CancellationToken cancellationToken = default)
        => DownloadStreamAsync(path, cancellationToken);

    /// <summary>Access-level-aware existence check (mirrors the download overloads).</summary>
    Task<bool> ExistsAsync(string path, FileAccessLevel accessLevel, CancellationToken cancellationToken = default)
        => ExistsAsync(path, cancellationToken);

    /// <summary>
    /// Download a file
    /// </summary>
    /// <param name="path">Relative path/key for the file</param>
    /// <param name="cancellationToken">Cancellation token</param>
    /// <returns>File content or null if not found</returns>
    Task<byte[]?> DownloadAsync(string path, CancellationToken cancellationToken = default);

    /// <summary>
    /// Download a file to a stream
    /// </summary>
    Task<Stream?> DownloadStreamAsync(string path, CancellationToken cancellationToken = default);

    /// <summary>
    /// Delete a file
    /// </summary>
    /// <param name="path">Relative path/key for the file</param>
    /// <param name="cancellationToken">Cancellation token</param>
    /// <returns>True if deleted, false if not found</returns>
    Task<bool> DeleteAsync(string path, CancellationToken cancellationToken = default);

    /// <summary>
    /// Check if a file exists
    /// </summary>
    Task<bool> ExistsAsync(string path, CancellationToken cancellationToken = default);

    /// <summary>
    /// Get file info without downloading content
    /// </summary>
    Task<FileInfo?> GetInfoAsync(string path, CancellationToken cancellationToken = default);

    /// <summary>
    /// List files in a directory/prefix
    /// </summary>
    Task<IEnumerable<FileInfo>> ListAsync(
        string? prefix = null,
        int? maxResults = null,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Copy a file to a new location
    /// </summary>
    Task<FileUploadResult> CopyAsync(
        string sourcePath,
        string destinationPath,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Move a file to a new location
    /// </summary>
    Task<FileUploadResult> MoveAsync(
        string sourcePath,
        string destinationPath,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Generate a signed URL for temporary access (if supported)
    /// </summary>
    /// <param name="path">Relative path/key for the file</param>
    /// <param name="expiration">When the URL expires</param>
    /// <param name="permissions">Read, Write, or ReadWrite</param>
    /// <param name="cancellationToken">Cancellation token</param>
    /// <returns>Signed URL or null if not supported</returns>
    Task<string?> GetSignedUrlAsync(
        string path,
        TimeSpan expiration,
        SignedUrlPermissions permissions = SignedUrlPermissions.Read,
        CancellationToken cancellationToken = default);
}

/// <summary>
/// Result of a file upload operation
/// </summary>
public class FileUploadResult
{
    /// <summary>
    /// Whether the upload succeeded
    /// </summary>
    public bool Success { get; init; }

    /// <summary>
    /// Storage reference (full path, blob URI, S3 key, etc.)
    /// </summary>
    public string StorageReference { get; init; } = null!;

    /// <summary>
    /// Storage provider used
    /// </summary>
    public FileStorageProvider Provider { get; init; }

    /// <summary>
    /// Size of the uploaded file in bytes
    /// </summary>
    public long SizeBytes { get; init; }

    /// <summary>
    /// Content hash (e.g., MD5 or SHA256)
    /// </summary>
    public string? ContentHash { get; init; }

    /// <summary>
    /// ETag for the file (if supported)
    /// </summary>
    public string? ETag { get; init; }

    /// <summary>
    /// Public URL if available
    /// </summary>
    public string? PublicUrl { get; init; }

    /// <summary>
    /// Error message if upload failed
    /// </summary>
    public string? Error { get; init; }

    public static FileUploadResult Ok(string storageReference, FileStorageProvider provider, long sizeBytes, string? contentHash = null)
        => new()
        {
            Success = true,
            StorageReference = storageReference,
            Provider = provider,
            SizeBytes = sizeBytes,
            ContentHash = contentHash
        };

    public static FileUploadResult Fail(string error, FileStorageProvider provider)
        => new()
        {
            Success = false,
            StorageReference = string.Empty,
            Provider = provider,
            Error = error
        };
}

/// <summary>
/// Information about a stored file
/// </summary>
public class FileInfo
{
    /// <summary>
    /// Relative path/key
    /// </summary>
    public string Path { get; init; } = null!;

    /// <summary>
    /// File name
    /// </summary>
    public string Name { get; init; } = null!;

    /// <summary>
    /// File size in bytes
    /// </summary>
    public long SizeBytes { get; init; }

    /// <summary>
    /// Content type (MIME type)
    /// </summary>
    public string? ContentType { get; init; }

    /// <summary>
    /// Content hash
    /// </summary>
    public string? ContentHash { get; init; }

    /// <summary>
    /// ETag
    /// </summary>
    public string? ETag { get; init; }

    /// <summary>
    /// When the file was created
    /// </summary>
    public DateTime? CreatedAt { get; init; }

    /// <summary>
    /// When the file was last modified
    /// </summary>
    public DateTime? LastModifiedAt { get; init; }

    /// <summary>
    /// Custom metadata
    /// </summary>
    public IDictionary<string, string>? Metadata { get; init; }
}

/// <summary>
/// Permissions for signed URLs
/// </summary>
[Flags]
public enum SignedUrlPermissions
{
    Read = 1,
    Write = 2,
    Delete = 4,
    ReadWrite = Read | Write
}

/// <summary>
/// Options for configuring file uploaders
/// </summary>
public class FileUploaderOptions
{
    /// <summary>
    /// Base path/container for all uploads
    /// </summary>
    public string? BasePath { get; set; }

    /// <summary>
    /// Default content type for uploads
    /// </summary>
    public string DefaultContentType { get; set; } = "application/octet-stream";

    /// <summary>
    /// Whether to compute content hash on upload
    /// </summary>
    public bool ComputeContentHash { get; set; } = true;

    /// <summary>
    /// Hash algorithm to use (SHA256, MD5)
    /// </summary>
    public string HashAlgorithm { get; set; } = "SHA256";

    /// <summary>
    /// Maximum file size in bytes (0 = unlimited)
    /// </summary>
    public long MaxFileSizeBytes { get; set; } = 0;

    /// <summary>
    /// Absolute base URL the provider's PUBLIC assets are addressed from (no
    /// trailing slash) — e.g. a CDN host mapped to the public container. Used by
    /// the uploader to build the stable direct public URL. When null, the
    /// provider returns null from <see cref="IFileUploader.GetPublicUrl"/> and
    /// callers fall back to proxy serving (local/dev).
    /// </summary>
    public string? PublicBaseUrl { get; set; }
}

/// <summary>
/// Options for Azure Blob Storage
/// </summary>
public class AzureBlobOptions : FileUploaderOptions
{
    /// <summary>
    /// Azure Storage connection string
    /// </summary>
    public string ConnectionString { get; set; } = null!;

    /// <summary>
    /// Container name
    /// </summary>
    public string ContainerName { get; set; } = null!;

    /// <summary>
    /// Whether to create container if it doesn't exist
    /// </summary>
    public bool CreateContainerIfNotExists { get; set; } = true;

    /// <summary>
    /// Access tier for blobs (Hot, Cool, Archive)
    /// </summary>
    public string? AccessTier { get; set; }

    /// <summary>
    /// Optional dedicated container for PUBLIC assets. When set, public uploads
    /// go here instead of <see cref="ContainerName"/>, so this container alone
    /// can be granted anonymous read / fronted by a CDN while the private
    /// container stays locked down. When null, public assets stay in the private
    /// container and are served only via the prefix-guarded public endpoint.
    /// </summary>
    public string? PublicContainerName { get; set; }

    /// <summary>
    /// True only if <see cref="PublicContainerName"/> has been granted
    /// container-level anonymous (blob) read access. Enables returning a direct
    /// blob URL as the public URL. Leave false (default) unless ops have
    /// deliberately made that container public — proxy serving works regardless.
    /// </summary>
    public bool PublicContainerAnonymousRead { get; set; }
}

/// <summary>
/// Options for local file system storage
/// </summary>
public class LocalFileOptions : FileUploaderOptions
{
    /// <summary>
    /// Base directory for file storage
    /// </summary>
    public string BaseDirectory { get; set; } = null!;

    /// <summary>
    /// Whether to create directory if it doesn't exist
    /// </summary>
    public bool CreateDirectoryIfNotExists { get; set; } = true;
}

/// <summary>
/// Options for AWS S3 storage
/// </summary>
public class AwsS3Options : FileUploaderOptions
{
    /// <summary>
    /// AWS Region
    /// </summary>
    public string Region { get; set; } = null!;

    /// <summary>
    /// S3 Bucket name
    /// </summary>
    public string BucketName { get; set; } = null!;

    /// <summary>
    /// AWS Access Key ID (optional if using IAM roles)
    /// </summary>
    public string? AccessKeyId { get; set; }

    /// <summary>
    /// AWS Secret Access Key (optional if using IAM roles)
    /// </summary>
    public string? SecretAccessKey { get; set; }

    /// <summary>
    /// Whether to create bucket if it doesn't exist
    /// </summary>
    public bool CreateBucketIfNotExists { get; set; } = false;
}

/// <summary>
/// Whether a stored file is privately accessed (authorized reads / SAS only,
/// the default for every existing caller) or publicly accessed (anonymous,
/// stable URL, CDN-frontable). Public-ness is otherwise derived from the path
/// via <see cref="IPublicAssetPathRegistry"/>; this enum is how a caller declares intent
/// to the uploader.
/// </summary>
public enum FileAccessLevel
{
    Private = 0,
    Public = 1,
}
