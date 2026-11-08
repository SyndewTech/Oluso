using Microsoft.Extensions.Logging;
using Oluso.Core.Domain.Entities;
using Oluso.Core.Domain.Interfaces;
using Oluso.Core.Services;
using Oluso.Core.Storage;
using Oluso.Core.UserJourneys;

namespace Oluso.Storage;

public sealed class JourneyFileUploadService : IJourneyFileUploadService
{
    private const int MaxFileNameLength = 200;

    private readonly IFileAttachmentService _attachments;
    private readonly ILogger<JourneyFileUploadService> _logger;

    public JourneyFileUploadService(IFileAttachmentService attachments, ILogger<JourneyFileUploadService> logger)
    {
        _attachments = attachments;
        _logger = logger;
    }

    public async Task<string?> ValidateAsync(IReadOnlyList<JourneyInputFile> files, JourneyUploadPolicy policy,
        List<AcceptedJourneyFile> accepted, CancellationToken cancellationToken = default)
    {
        if (files.Count > policy.MaxFiles) return JourneyUploadErrors.TooMany;

        foreach (var file in files)
        {
            if (file.Length <= 0) return JourneyUploadErrors.Empty;
            if (file.Length > policy.MaxBytes) return JourneyUploadErrors.TooLarge;

            await using var stream = file.OpenReadStream();
            var type = await FileContentSniffer.SniffAsync(stream, cancellationToken);
            if (type == null || !policy.AllowedKinds.Contains(type.Kind, StringComparer.OrdinalIgnoreCase))
                return JourneyUploadErrors.TypeNotAllowed;

            var extension = Path.GetExtension(file.FileName);
            if (!type.Extensions.Contains(extension, StringComparer.OrdinalIgnoreCase))
                return JourneyUploadErrors.ExtensionMismatch;

            accepted.Add(new AcceptedJourneyFile(file, type));
        }
        return null;
    }

    public async Task<FileAttachment> SaveAsync(JourneyUploadRequest request, CancellationToken cancellationToken = default)
    {
        await using var content = request.File.File.OpenReadStream();
        var attachment = await _attachments.UploadAsync(new UploadAttachmentRequest
        {
            TenantId = request.TenantId,
            UserId = request.UserId ?? JourneyUploads.PendingUploader(request.JourneyId),
            FileName = SafeFileName(request.File),
            ContentType = request.File.Type.MediaType,
            Content = content,
            EntityType = request.EntityType,
            EntityId = request.EntityId,
            SubEntityType = request.SubEntityType,
            SubEntityId = request.SubEntityId,
        }, cancellationToken);

        _logger.LogInformation("Journey {JourneyId} stored upload {AttachmentId} ({Kind}, {Bytes} bytes) for {EntityType}",
            request.JourneyId, attachment.Id, request.File.Type.Kind, attachment.SizeBytes, request.EntityType);
        return attachment;
    }

    /// <summary>The browser's name without any path or control characters, ending in the sniffed type's extension.</summary>
    internal static string SafeFileName(AcceptedJourneyFile file)
    {
        var name = Path.GetFileNameWithoutExtension(file.File.FileName.Replace('\\', '/').Split('/').Last());
        var cleaned = new string(name.Where(c => !char.IsControl(c) && c is not ('"' or '<' or '>' or '|' or ':' or '*' or '?')).ToArray()).Trim();
        if (cleaned.Length == 0) cleaned = "upload";
        var extension = Path.GetExtension(file.File.FileName).ToLowerInvariant();
        if (cleaned.Length + extension.Length > MaxFileNameLength) cleaned = cleaned[..(MaxFileNameLength - extension.Length)];
        return cleaned + extension;
    }
}

/// <summary>
/// Files posted to form steps (<see cref="JourneyUploads.EntityType"/>, keyed by journey):
/// readable by the user who posted them and by administrators who can read submissions, except files of a
/// module's journey, which only that module serves. Never deletable through the generic endpoint: they are
/// part of the submission.
/// </summary>
public sealed class JourneyUploadFileAccessProvider : IFileAccessProvider
{
    public const string SubmissionsReadPermission = "submissions.read";

    private readonly IFileAttachmentStore _store;
    private readonly IProfileService _profiles;
    private readonly IJourneyPolicyStore? _policies;
    private readonly IJourneySubmissionStore? _submissions;
    private readonly IJourneyStateStore? _states;

    public JourneyUploadFileAccessProvider(IFileAttachmentStore store, IProfileService profiles, IJourneyPolicyStore? policies = null,
        IJourneySubmissionStore? submissions = null, IJourneyStateStore? states = null)
    {
        _store = store;
        _profiles = profiles;
        _policies = policies;
        _submissions = submissions;
        _states = states;
    }

    public string EntityType => JourneyUploads.EntityType;

    public async Task<bool> CanReadAsync(string tenantId, string userId, string entityId, string? subEntityType, string? subEntityId, CancellationToken cancellationToken = default)
    {
        var files = await _store.GetByEntityAsync(JourneyUploads.EntityType, entityId, cancellationToken);
        if (files.Any(f => f.TenantId == tenantId && f.UploadedByUserId == userId)) return true;

        if (await BelongsToModuleJourneyAsync(entityId, cancellationToken)) return false;

        var permissions = await _profiles.GetUserPermissionsAsync(userId, tenantId, cancellationToken);
        return permissions.Contains(SubmissionsReadPermission, StringComparer.Ordinal);
    }

    /// <summary>True when the journey run's policy is owned by a module, found from the run's record or its state.</summary>
    private async Task<bool> BelongsToModuleJourneyAsync(string journeyId, CancellationToken cancellationToken)
    {
        if (_policies == null) return false;
        var policyId = (_submissions == null ? null : await _submissions.GetByJourneyAsync(journeyId, cancellationToken))?.PolicyId
            ?? (_states == null ? null : await _states.GetAsync(journeyId, cancellationToken))?.PolicyId;
        if (policyId == null) return false;
        return (await _policies.GetAsync(policyId, cancellationToken))?.IsModuleOwned == true;
    }

    public Task<bool> CanDeleteAsync(string tenantId, string userId, FileAttachment file, CancellationToken cancellationToken = default) =>
        Task.FromResult(false);
}

/// <summary>Gives files posted before sign-up or sign-in to the user the journey then attached.</summary>
public sealed class JourneyUploadUserAttachedHandler : IJourneyUserAttachedHandler
{
    private readonly IFileAttachmentStore _store;

    public JourneyUploadUserAttachedHandler(IFileAttachmentStore store) => _store = store;

    public async Task OnUserAttachedAsync(JourneyUserAttached attached, CancellationToken cancellationToken = default)
    {
        var pending = JourneyUploads.PendingUploader(attached.JourneyId);
        foreach (var file in await _store.GetByEntityAsync(JourneyUploads.EntityType, attached.JourneyId, cancellationToken))
        {
            if (file.TenantId != attached.TenantId || file.UploadedByUserId != pending) continue;
            file.UploadedByUserId = attached.UserId;
            file.CreatedBy = attached.UserId;
            await _store.UpdateAsync(file, cancellationToken);
        }
    }
}
