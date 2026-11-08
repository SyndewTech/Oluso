using Oluso.Core.Storage;

namespace Oluso.Enterprise.Verification;

/// <summary>How the step checks the business.</summary>
public enum BusinessVerificationMode
{
    /// <summary>A synchronous registry lookup (e.g. CAC through Dojah or Youverify).</summary>
    Lookup = 0,

    /// <summary>The provider's own hosted or embedded flow; the outcome arrives later.</summary>
    Hosted = 1,
}

/// <summary>A user (or a journey that has no user yet) asking to act for a business.</summary>
public sealed record BusinessVerificationRequest
{
    public required string TenantId { get; init; }

    /// <summary>The applicant's user id, when they have an account.</summary>
    public string? SubjectId { get; init; }

    /// <summary>The journey and step submitting; links made before the account exists are found again through the journey.</summary>
    public string? JourneyId { get; init; }
    public string? StepId { get; init; }

    /// <summary>ISO 3166-1 alpha-2.</summary>
    public required string Country { get; init; }

    /// <summary>As typed; normalised by <see cref="BusinessRegistrationNumber"/>.</summary>
    public required string RegistrationNumber { get; init; }

    public string? RegistrationType { get; init; }
    public string? BusinessName { get; init; }

    public BusinessVerificationMode Mode { get; init; }

    /// <summary>Hosted mode: the absolute journey callback the provider returns the applicant to.</summary>
    public string? ReturnUrl { get; init; }

    /// <summary>The registered name must match <see cref="BusinessName"/> (see <see cref="BusinessNameMatcher"/>).</summary>
    public bool RequireNameMatch { get; init; }

    /// <summary>A business the registry lists as not trading is rejected for this applicant.</summary>
    public bool RequireActive { get; init; } = true;

    public ApplicantEvidenceRequirement Evidence { get; init; }

    /// <summary>The applicant's name, for a director match.</summary>
    public ApplicantName? Applicant { get; init; }

    /// <summary>Role words (case-insensitive, matched within the registry's role text) that count as a director.</summary>
    public IReadOnlyList<string> DirectorRoles { get; init; } = Array.Empty<string>();

    /// <summary>Evidence files, already accepted by the step's upload policy.</summary>
    public IReadOnlyList<AcceptedJourneyFile> Documents { get; init; } = Array.Empty<AcceptedJourneyFile>();
}

public sealed record ApplicantName(string? FirstName, string? LastName);

public enum BusinessVerificationAttemptOutcome
{
    /// <summary>The applicant may act for the business.</summary>
    Verified,

    /// <summary>Not found, not trading, a mismatch, or the provider or a reviewer said no.</summary>
    Rejected,

    /// <summary>Waiting on a reviewer or the provider's asynchronous decision.</summary>
    InReview,

    /// <summary>The provider-hosted session was opened; send the applicant to <see cref="BusinessVerificationAttempt.Launch"/>.</summary>
    HostedSessionStarted,

    /// <summary>The registry doesn't list the applicant, so they must upload documents.</summary>
    DocumentsRequired,

    /// <summary>The provider couldn't answer; the link is Pending and a retry (or a reviewer) may settle it.</summary>
    ProviderUnavailable,

    /// <summary>The tenant's configuration or credentials can't serve the request.</summary>
    Misconfigured,

    /// <summary>The number couldn't be parsed, or the provider refused it as malformed.</summary>
    InvalidInput,
}

/// <summary>
/// What an attempt came to. <see cref="Business"/> and <see cref="Applicant"/> are null
/// only for <see cref="BusinessVerificationAttemptOutcome.InvalidInput"/> and
/// <see cref="BusinessVerificationAttemptOutcome.Misconfigured"/>, which record a check
/// but no business.
/// </summary>
public sealed record BusinessVerificationAttempt(
    BusinessVerificationAttemptOutcome Outcome,
    VerifiedBusiness? Business,
    BusinessApplicant? Applicant,
    string? InputError = null,
    HostedSessionStart? Session = null,
    string? SessionProviderId = null)
{
    public HostedSessionLaunch? Launch => Session?.Launch;
}

/// <summary>A business and the requesting user's link to it.</summary>
public sealed record BusinessApplicantView(VerifiedBusiness Business, BusinessApplicant Applicant);

/// <summary>
/// Verifies businesses and the people acting for them: normalises the number, reuses a
/// fresh registry snapshot, otherwise routes to the country's provider (lookup or hosted
/// session), applies the step's matches and evidence rules, keeps every check, and raises
/// <c>business.verification_completed</c> on final outcomes.
/// </summary>
public interface IBusinessVerificationService
{
    Task<BusinessVerificationAttempt> SubmitAsync(BusinessVerificationRequest request, CancellationToken cancellationToken = default);

    /// <summary>The link this user (or, before sign-up, this journey) has to the business with that number, if any.</summary>
    Task<BusinessApplicantView?> FindAsync(string tenantId, string? subjectId, string? journeyId, string country, string registrationNumber, string? registrationType, CancellationToken cancellationToken = default);

    Task<BusinessApplicantView?> GetApplicantAsync(string tenantId, string applicantId, CancellationToken cancellationToken = default);

    /// <summary>
    /// Asks the provider how a hosted session ended and applies it. Idempotent and safe to race
    /// (webhook, browser callback, reviewer refresh): one caller decides, raising the event and
    /// sending the email; the rest, and later calls, return the link as that decision left it.
    /// </summary>
    Task<BusinessApplicantView?> CompleteHostedSessionAsync(string tenantId, string providerId, string sessionReference, CancellationToken cancellationToken = default);

    /// <summary>The launch for the applicant's open hosted session, to reopen it; null when none can be reopened.</summary>
    Task<HostedSessionLaunch?> ResumeHostedSessionAsync(string tenantId, BusinessApplicantView view, string returnUrl, CancellationToken cancellationToken = default);

    /// <summary>
    /// A reviewer's decision on a business that's Pending or InReview; open links follow it unless
    /// something else decides them meanwhile. Throws <see cref="VerificationDecisionConflictException"/>
    /// when the business was already decided, changing nothing.
    /// </summary>
    Task<VerifiedBusiness> ReviewBusinessAsync(string tenantId, string businessId, bool approve, string reviewerId, string? note, CancellationToken cancellationToken = default);

    /// <summary>
    /// A reviewer's decision on an applicant that's Pending or InReview (e.g. after checking their
    /// documents). Throws <see cref="VerificationDecisionConflictException"/> when the applicant was
    /// already decided (by the provider, its browser callback or another reviewer), changing nothing.
    /// </summary>
    Task<BusinessApplicant> ReviewApplicantAsync(string tenantId, string applicantId, bool approve, string reviewerId, string? note, CancellationToken cancellationToken = default);

    /// <summary>Asks for the outcome to be emailed when the applicant leaves before it's decided.</summary>
    Task RequestOutcomeEmailAsync(string tenantId, string applicantId, string email, string? resumeUrl, CancellationToken cancellationToken = default);

    /// <summary>Ties links made earlier in a journey to the user it signed up or in.</summary>
    Task ClaimJourneyApplicantsAsync(string tenantId, string journeyId, string subjectId, CancellationToken cancellationToken = default);
}
