using Oluso.Core.Domain.Entities;
using Oluso.Core.UserJourneys;

namespace Oluso.Core.Storage;

/// <summary>What a journey step accepts for one upload field.</summary>
public sealed record JourneyUploadPolicy
{
    /// <summary><see cref="FileContentSniffer"/> kinds accepted (pdf, png, jpeg, gif, webp, heic).</summary>
    public required IReadOnlyCollection<string> AllowedKinds { get; init; }

    public required long MaxBytes { get; init; }

    public required int MaxFiles { get; init; }

    /// <summary>The <c>accept</c> attribute for the file input: the accepted media types and extensions.</summary>
    public string AcceptAttribute => string.Join(',', AllowedKinds
        .Select(k => FileContentSniffer.Types.GetValueOrDefault(k))
        .OfType<SniffedFileType>()
        .SelectMany(t => t.Extensions.Prepend(t.MediaType)));
}

/// <summary>Why an upload was refused; the values are locale keys' suffixes.</summary>
public static class JourneyUploadErrors
{
    public const string Empty = "file_empty";
    public const string TooLarge = "file_too_large";
    public const string TooMany = "file_too_many";
    public const string TypeNotAllowed = "file_type_not_allowed";
    public const string ExtensionMismatch = "file_extension_mismatch";
}

/// <summary>A file that passed a <see cref="JourneyUploadPolicy"/>: its sniffed type travels with it.</summary>
public sealed record AcceptedJourneyFile(JourneyInputFile File, SniffedFileType Type);

public sealed record JourneyUploadRequest
{
    public required string TenantId { get; init; }
    public required string JourneyId { get; init; }

    /// <summary>The signed-in user, or null before the journey has one.</summary>
    public string? UserId { get; init; }

    public required AcceptedJourneyFile File { get; init; }

    /// <summary>The owning record's type; an <see cref="IFileAccessProvider"/> must be registered for it.</summary>
    public required string EntityType { get; init; }
    public required string EntityId { get; init; }
    public string? SubEntityType { get; init; }
    public string? SubEntityId { get; init; }
}

/// <summary>
/// Stores files posted to journey steps through the platform's attachment service:
/// per-tenant storage paths, no public URL, the sniffed media type (never the browser's),
/// and reads authorised by the entity type's <see cref="IFileAccessProvider"/>.
/// </summary>
public interface IJourneyFileUploadService
{
    /// <summary>
    /// Checks every file against the policy: count, emptiness, size, sniffed type and a
    /// file name extension that matches the sniffed type. Returns the first error, or
    /// null with <paramref name="accepted"/> filled.
    /// </summary>
    Task<string?> ValidateAsync(IReadOnlyList<JourneyInputFile> files, JourneyUploadPolicy policy,
        List<AcceptedJourneyFile> accepted, CancellationToken cancellationToken = default);

    Task<FileAttachment> SaveAsync(JourneyUploadRequest request, CancellationToken cancellationToken = default);
}

/// <summary>Conventions for files posted to journeys.</summary>
public static class JourneyUploads
{
    /// <summary>Entity type of files posted to a form step; the entity id is the journey id.</summary>
    public const string EntityType = "journey_upload";

    private const string PendingPrefix = "journey:";

    /// <summary>The uploader recorded for a file posted before the journey has a user; replaced once it has one.</summary>
    public static string PendingUploader(string journeyId) => PendingPrefix + journeyId;

    public static bool IsPendingUploader(string? uploader) => uploader?.StartsWith(PendingPrefix, StringComparison.Ordinal) == true;
}
