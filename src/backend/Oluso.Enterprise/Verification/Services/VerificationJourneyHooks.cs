using Microsoft.Extensions.DependencyInjection;
using Oluso.Core.Domain.Entities;
using Oluso.Core.Services;
using Oluso.Core.Storage;
using Oluso.Core.UserJourneys;
using Oluso.Enterprise.Verification.Stores;

namespace Oluso.Enterprise.Verification.Services;

/// <summary>Ties links made before sign-up to the user the same journey then signed up or in.</summary>
public sealed class BusinessApplicantJourneyLinker : IJourneyUserAttachedHandler
{
    private readonly IBusinessVerificationService _service;

    public BusinessApplicantJourneyLinker(IBusinessVerificationService service) => _service = service;

    public Task OnUserAttachedAsync(JourneyUserAttached attached, CancellationToken cancellationToken = default) =>
        _service.ClaimJourneyApplicantsAsync(attached.TenantId, attached.JourneyId, attached.UserId, cancellationToken);
}

/// <summary>
/// An applicant's evidence (<see cref="VerificationFileEntityTypes.BusinessApplicant"/>, keyed by
/// applicant): readable by every user linked to the same business and by administrators who
/// can read business verifications. Never deletable: it's the record a decision rests on.
/// <para>Every file request builds every registered provider to find the one for the file's
/// entity type, so this one resolves the module's store only when asked about a file it owns.</para>
/// </summary>
public sealed class BusinessApplicantFileAccessProvider : IFileAccessProvider
{
    private readonly IServiceProvider _services;

    public BusinessApplicantFileAccessProvider(IServiceProvider services) => _services = services;

    public string EntityType => VerificationFileEntityTypes.BusinessApplicant;

    public async Task<bool> CanReadAsync(string tenantId, string userId, string entityId, string? subEntityType, string? subEntityId, CancellationToken cancellationToken = default) =>
        await BusinessVerificationAccess.CanSeeApplicantAsync(
            _services.GetRequiredService<IBusinessVerificationStore>(), _services.GetRequiredService<IProfileService>(),
            tenantId, userId, entityId, cancellationToken);

    public Task<bool> CanDeleteAsync(string tenantId, string userId, FileAttachment file, CancellationToken cancellationToken = default) =>
        Task.FromResult(false);
}

/// <summary>Who may see a business's verification data: the users linked to it, and administrators with the read permission.</summary>
public static class BusinessVerificationAccess
{
    public static async Task<bool> CanSeeBusinessAsync(IBusinessVerificationStore store, IProfileService profiles,
        string tenantId, string userId, string businessId, CancellationToken cancellationToken)
    {
        var applicants = await store.ListApplicantsForBusinessAsync(tenantId, businessId, cancellationToken);
        if (applicants.Any(a => a.SubjectId == userId)) return true;
        var permissions = await profiles.GetUserPermissionsAsync(userId, tenantId, cancellationToken);
        return permissions.Contains(VerificationPermissions.Read, StringComparer.Ordinal);
    }

    public static async Task<bool> CanSeeApplicantAsync(IBusinessVerificationStore store, IProfileService profiles,
        string tenantId, string userId, string applicantId, CancellationToken cancellationToken)
    {
        var applicant = await store.GetApplicantAsync(tenantId, applicantId, cancellationToken);
        return applicant != null && await CanSeeBusinessAsync(store, profiles, tenantId, userId, applicant.BusinessId, cancellationToken);
    }
}
