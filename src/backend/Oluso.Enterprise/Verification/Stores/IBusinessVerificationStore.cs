namespace Oluso.Enterprise.Verification.Stores;

/// <summary>Businesses, the people acting for them, and the audit trail of checks. Every query is scoped to one tenant.</summary>
public interface IBusinessVerificationStore
{
    Task<VerifiedBusiness?> FindBusinessAsync(string tenantId, string country, string registrationNumber, CancellationToken cancellationToken = default);
    Task<VerifiedBusiness?> GetBusinessAsync(string tenantId, string businessId, CancellationToken cancellationToken = default);
    Task<IReadOnlyList<VerifiedBusiness>> GetBusinessesAsync(string tenantId, IReadOnlyCollection<string> businessIds, CancellationToken cancellationToken = default);

    /// <summary>Newest activity first; <paramref name="search"/> matches the registered name or number.</summary>
    Task<(IReadOnlyList<VerifiedBusiness> Items, int TotalCount)> ListBusinessesAsync(
        string tenantId, BusinessVerificationStatus? status, string? search, int skip, int take, CancellationToken cancellationToken = default);

    Task AddBusinessAsync(VerifiedBusiness business, CancellationToken cancellationToken = default);
    Task UpdateBusinessAsync(VerifiedBusiness business, CancellationToken cancellationToken = default);

    Task<BusinessApplicant?> GetApplicantAsync(string tenantId, string applicantId, CancellationToken cancellationToken = default);
    Task<BusinessApplicant?> FindApplicantForSubjectAsync(string tenantId, string businessId, string subjectId, CancellationToken cancellationToken = default);

    /// <summary>A link made in this journey before it had a user.</summary>
    Task<BusinessApplicant?> FindUnclaimedApplicantForJourneyAsync(string tenantId, string businessId, string journeyId, CancellationToken cancellationToken = default);

    Task<IReadOnlyList<BusinessApplicant>> ListUnclaimedApplicantsForJourneyAsync(string tenantId, string journeyId, CancellationToken cancellationToken = default);
    Task<IReadOnlyList<BusinessApplicant>> ListApplicantsForBusinessAsync(string tenantId, string businessId, CancellationToken cancellationToken = default);
    Task<IReadOnlyList<BusinessApplicant>> ListApplicantsForSubjectAsync(string tenantId, string subjectId, CancellationToken cancellationToken = default);

    /// <summary>Applicants waiting on a decision (Pending or InReview), oldest first.</summary>
    Task<(IReadOnlyList<BusinessApplicant> Items, int TotalCount)> ListOpenApplicantsAsync(string tenantId, int skip, int take, CancellationToken cancellationToken = default);

    Task AddApplicantAsync(BusinessApplicant applicant, CancellationToken cancellationToken = default);
    Task UpdateApplicantAsync(BusinessApplicant applicant, CancellationToken cancellationToken = default);
    Task RemoveApplicantAsync(BusinessApplicant applicant, CancellationToken cancellationToken = default);

    Task AddCheckAsync(BusinessVerificationCheck check, CancellationToken cancellationToken = default);

    /// <summary>
    /// Writes a decision as one unit, only if nobody else decided first: the hosted session must
    /// still be open, and the business and applicant must still have the status they were loaded
    /// with. When any of them changed, nothing is written and every entity in the write is reloaded
    /// to what won. Followers are applicants a business decision carries to; each is written only
    /// if it too is still as loaded, and otherwise reloaded and left out.
    /// Every entity must have been loaded through this store instance.
    /// </summary>
    Task<VerificationDecisionResult> TryDecideAsync(VerificationDecisionWrite write, CancellationToken cancellationToken = default);
    Task<BusinessVerificationCheck?> GetCheckAsync(string tenantId, string checkId, CancellationToken cancellationToken = default);

    /// <summary>A hosted session by the provider's reference.</summary>
    Task<BusinessVerificationCheck?> FindSessionAsync(string tenantId, string providerId, string sessionReference, CancellationToken cancellationToken = default);

    /// <summary>The applicant's newest hosted session still waiting on the provider.</summary>
    Task<BusinessVerificationCheck?> FindOpenSessionForApplicantAsync(string tenantId, string applicantId, CancellationToken cancellationToken = default);

    /// <summary>Newest first.</summary>
    Task<IReadOnlyList<BusinessVerificationCheck>> ListChecksForBusinessAsync(string tenantId, string businessId, CancellationToken cancellationToken = default);
}

/// <summary>What one decision changes; see <see cref="IBusinessVerificationStore.TryDecideAsync"/>.</summary>
public sealed class VerificationDecisionWrite
{
    /// <summary>A hosted session being closed; it must still be open.</summary>
    public BusinessVerificationCheck? CloseSession { get; init; }

    /// <summary>Must still have the status it was loaded with.</summary>
    public VerifiedBusiness? Business { get; init; }

    /// <summary>Must still have the status it was loaded with.</summary>
    public BusinessApplicant? Applicant { get; init; }

    /// <summary>Written only where still as loaded; the rest are left to whoever changed them.</summary>
    public IReadOnlyList<BusinessApplicant> Followers { get; init; } = Array.Empty<BusinessApplicant>();

    public IReadOnlyList<BusinessVerificationCheck> NewChecks { get; init; } = Array.Empty<BusinessVerificationCheck>();
}

/// <param name="Applied">False when someone else decided first and nothing was written.</param>
/// <param name="AppliedFollowers">The followers that were written.</param>
public sealed record VerificationDecisionResult(bool Applied, IReadOnlyList<BusinessApplicant> AppliedFollowers);
