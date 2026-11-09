using System.Text.Json;
using Oluso.Core.Domain.Entities;

namespace Oluso.Enterprise.Verification;

/// <summary>Where a business, or a person's claim to act for it, stands.</summary>
public enum BusinessVerificationStatus
{
    /// <summary>Nothing decided yet: the provider couldn't answer, or documents are still to come.</summary>
    Pending = 0,

    /// <summary>Waiting on someone else: the provider's hosted session or a reviewer.</summary>
    InReview = 1,

    Verified = 2,

    /// <summary>Decided against (not found, not trading, a mismatch, the provider or a reviewer said no).</summary>
    Rejected = 3,

    /// <summary>The check couldn't be completed (the applicant abandoned the provider's flow).</summary>
    Failed = 4,
}

public static class BusinessVerificationStatusNames
{
    public static string ToWire(this BusinessVerificationStatus status) => status switch
    {
        BusinessVerificationStatus.Pending => "pending",
        BusinessVerificationStatus.InReview => "in_review",
        BusinessVerificationStatus.Verified => "verified",
        BusinessVerificationStatus.Rejected => "rejected",
        BusinessVerificationStatus.Failed => "failed",
        _ => throw new ArgumentOutOfRangeException(nameof(status), status, null),
    };

    public static bool TryParse(string? wire, out BusinessVerificationStatus status)
    {
        foreach (var candidate in Enum.GetValues<BusinessVerificationStatus>())
        {
            if (string.Equals(candidate.ToWire(), wire, StringComparison.OrdinalIgnoreCase)
                || string.Equals(candidate.ToString(), wire, StringComparison.OrdinalIgnoreCase))
            {
                status = candidate;
                return true;
            }
        }
        status = default;
        return false;
    }

    /// <summary>Verified, Rejected and Failed: nothing further will happen without a new attempt.</summary>
    public static bool IsFinal(this BusinessVerificationStatus status) =>
        status is BusinessVerificationStatus.Verified or BusinessVerificationStatus.Rejected or BusinessVerificationStatus.Failed;
}

/// <summary>Why a business or an applicant has the status it has.</summary>
public static class BusinessVerificationReasons
{
    public const string NotFound = "not_found";
    public const string Inactive = "inactive";
    public const string NameMismatch = "name_mismatch";
    public const string DirectorMismatch = "director_mismatch";
    public const string ProviderUnavailable = "provider_unavailable";
    public const string AwaitingProvider = "awaiting_provider";
    public const string AwaitingReview = "awaiting_review";
    public const string DocumentsRequired = "documents_required";
    public const string RejectedByProvider = "rejected_by_provider";
    public const string RejectedByReviewer = "rejected_by_reviewer";
    public const string Abandoned = "abandoned";
}

/// <summary>On what grounds a user acts for a business.</summary>
public static class BusinessApplicantRelationships
{
    /// <summary>The registry lists the user among the business's directors (or proprietors, partners, trustees).</summary>
    public const string RegistryDirectorMatch = "registry_director_match";

    /// <summary>The user says they represent the business; documents and a reviewer back it when the step asks for them.</summary>
    public const string AuthorizedRepresentativeDeclared = "authorized_representative_declared";

    /// <summary>The provider's hosted flow verified the business with the user present.</summary>
    public const string ProviderVerified = "provider_verified";
}

/// <summary>What a journey step demands before an applicant may act for a business.</summary>
public enum ApplicantEvidenceRequirement
{
    /// <summary>The business checks out; the applicant's word is enough.</summary>
    None = 0,

    /// <summary>The applicant must be a director on the registry.</summary>
    DirectorMatch = 1,

    /// <summary>The applicant uploads documents (e.g. a letter of authority) a reviewer accepts.</summary>
    Documents = 2,

    /// <summary>A director goes straight through; anyone else uploads documents for review.</summary>
    DirectorOrDocuments = 3,
}

/// <summary>
/// A business an organisation has checked, one per registration number. Holds the latest
/// registry snapshot (reused within the organisation's reuse window instead of buying a
/// new lookup) and the business-level decision. People who act for it are
/// <see cref="BusinessApplicant"/>s; every lookup, reuse, hosted session and review is a
/// <see cref="BusinessVerificationCheck"/>.
/// <para>PII held: the registry's public company record and the names and roles of the
/// people it lists. Contact details providers return are not stored.</para>
/// </summary>
public class VerifiedBusiness : TenantEntity
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    public string Id { get; set; } = Guid.NewGuid().ToString("N");

    /// <summary>ISO 3166-1 alpha-2.</summary>
    public string Country { get; set; } = null!;

    /// <summary>Canonical form (see <see cref="BusinessRegistrationNumber"/>).</summary>
    public string RegistrationNumber { get; set; } = null!;

    public string? RegistrationType { get; set; }

    public string? RegisteredName { get; set; }
    public string? EntityType { get; set; }
    public string? RegistrationDate { get; set; }
    public string? RegisteredAddress { get; set; }
    public BusinessActivityStatus ActivityStatus { get; set; }
    public string? RawStatus { get; set; }

    /// <summary>Read and written through <see cref="People"/>.</summary>
    public string? PeopleJson { get; set; }

    /// <summary>When the registry snapshot was taken; null until a lookup found the business.</summary>
    public DateTime? RegistryCheckedAt { get; set; }

    /// <summary>The check that produced the snapshot.</summary>
    public string? RegistryCheckId { get; set; }
    public string? RegistryProviderId { get; set; }
    public VerificationEnvironment? RegistryEnvironment { get; set; }

    public BusinessVerificationStatus Status { get; set; }

    /// <summary>One of <see cref="BusinessVerificationReasons"/>.</summary>
    public string? StatusReason { get; set; }

    /// <summary>Operator-facing detail.</summary>
    public string? StatusMessage { get; set; }

    public string? LatestCheckId { get; set; }

    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public DateTime UpdatedAt { get; set; } = DateTime.UtcNow;
    public DateTime? DecidedAt { get; set; }

    public string? ReviewedBy { get; set; }
    public DateTime? ReviewedAt { get; set; }
    public string? ReviewNote { get; set; }

    public IReadOnlyList<BusinessPerson> People
    {
        get => string.IsNullOrEmpty(PeopleJson)
            ? Array.Empty<BusinessPerson>()
            : JsonSerializer.Deserialize<List<BusinessPerson>>(PeopleJson, JsonOptions) ?? new List<BusinessPerson>();
        set => PeopleJson = value.Count == 0 ? null : JsonSerializer.Serialize(value, JsonOptions);
    }

    public bool HasRegistrySnapshot => RegistryCheckedAt != null && RegisteredName != null;

    public BusinessRegistryRecord? Snapshot() => !HasRegistrySnapshot ? null : new BusinessRegistryRecord
    {
        RegisteredName = RegisteredName!,
        RegistrationNumber = RegistrationNumber,
        EntityType = EntityType,
        RegistrationDate = RegistrationDate,
        Address = RegisteredAddress,
        ActivityStatus = ActivityStatus,
        RawStatus = RawStatus,
        People = People,
    };
}

/// <summary>
/// A user acting for a <see cref="VerifiedBusiness"/>: on what grounds, with what evidence,
/// and whether that's been accepted. A business can have several; a user can act for
/// several businesses. Made before the user has an account, it carries the journey and
/// is tied to the user when the same journey signs them up or in.
/// </summary>
public class BusinessApplicant : TenantEntity
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    public string Id { get; set; } = Guid.NewGuid().ToString("N");

    public string BusinessId { get; set; } = null!;

    /// <summary>The user; null until the journey that made the link has one.</summary>
    public string? SubjectId { get; set; }

    /// <summary>The journey that last submitted for this link, and its step.</summary>
    public string? JourneyId { get; set; }
    public string? StepId { get; set; }

    /// <summary>One of <see cref="BusinessApplicantRelationships"/>.</summary>
    public string Relationship { get; set; } = BusinessApplicantRelationships.AuthorizedRepresentativeDeclared;

    public ApplicantEvidenceRequirement EvidenceRequirement { get; set; }

    /// <summary>The business name the applicant typed.</summary>
    public string? ProvidedBusinessName { get; set; }

    /// <summary>Null when the step didn't ask.</summary>
    public bool? NameMatched { get; set; }
    public bool? DirectorMatched { get; set; }

    /// <summary>The registry person the applicant matched, as the registry lists them.</summary>
    public string? MatchedPersonName { get; set; }
    public string? MatchedPersonRole { get; set; }

    /// <summary>Attachment ids of uploaded evidence; read and written through <see cref="DocumentIds"/>.</summary>
    public string? DocumentIdsJson { get; set; }

    public BusinessVerificationStatus Status { get; set; }

    /// <summary>One of <see cref="BusinessVerificationReasons"/>.</summary>
    public string? StatusReason { get; set; }
    public string? StatusMessage { get; set; }

    /// <summary>Where to email the outcome when the applicant left before it was decided.</summary>
    public string? NotifyEmail { get; set; }

    /// <summary>Absolute URL the outcome email points back to (the journey's start page).</summary>
    public string? ResumeUrl { get; set; }

    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public DateTime UpdatedAt { get; set; } = DateTime.UtcNow;
    public DateTime? DecidedAt { get; set; }

    public string? ReviewedBy { get; set; }
    public DateTime? ReviewedAt { get; set; }
    public string? ReviewNote { get; set; }

    public IReadOnlyList<string> DocumentIds
    {
        get => string.IsNullOrEmpty(DocumentIdsJson)
            ? Array.Empty<string>()
            : JsonSerializer.Deserialize<List<string>>(DocumentIdsJson, JsonOptions) ?? new List<string>();
        set => DocumentIdsJson = value.Count == 0 ? null : JsonSerializer.Serialize(value, JsonOptions);
    }
}

/// <summary>Kinds of <see cref="BusinessVerificationCheck"/>.</summary>
public static class BusinessCheckKinds
{
    public const string Lookup = "lookup";
    public const string Reuse = "reuse";
    public const string HostedSession = "hosted_session";
    public const string Review = "review";
}

/// <summary>
/// One thing that happened in verifying a business, kept for audit: a registry lookup
/// (and what it answered), a reuse of an earlier snapshot, a provider-hosted session, or a
/// reviewer's decision. Never overwritten except to close a hosted session.
/// </summary>
public class BusinessVerificationCheck : TenantEntity
{
    public string Id { get; set; } = Guid.NewGuid().ToString("N");

    /// <summary>Null when the check failed before a business could be recorded (bad credentials, misconfiguration).</summary>
    public string? BusinessId { get; set; }
    public string? ApplicantId { get; set; }

    public string Country { get; set; } = null!;
    public string RegistrationNumber { get; set; } = null!;

    /// <summary>One of <see cref="BusinessCheckKinds"/>.</summary>
    public string Kind { get; set; } = null!;

    public string? ProviderId { get; set; }
    public VerificationEnvironment? Environment { get; set; }

    /// <summary>
    /// Lookups: found, not_found, provider_unavailable, invalid_request, unauthorized, misconfigured.
    /// Hosted sessions: open, approved, rejected, abandoned. Reviews: approved, rejected.
    /// </summary>
    public string Outcome { get; set; } = null!;

    /// <summary>Hosted sessions: the provider's reference for the session; unique per provider.</summary>
    public string? SessionReference { get; set; }

    /// <summary>Hosted sessions: the journey and step waiting on it.</summary>
    public string? JourneyId { get; set; }
    public string? StepId { get; set; }

    public string? ProviderReference { get; set; }

    /// <summary>Operator-facing detail.</summary>
    public string? Message { get; set; }

    /// <summary>Reuse: the check whose snapshot was reused.</summary>
    public string? ReusedFromCheckId { get; set; }

    /// <summary><see cref="Oluso.Core.Events.EventOriginKinds"/> of what caused it.</summary>
    public string? OriginKind { get; set; }
    public string? OriginId { get; set; }
    public string? OriginName { get; set; }

    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public DateTime? CompletedAt { get; set; }
}

/// <summary>Values of <see cref="BusinessVerificationCheck.Outcome"/>.</summary>
public static class BusinessCheckOutcomes
{
    public const string Found = "found";
    public const string NotFound = "not_found";
    public const string ProviderUnavailable = "provider_unavailable";
    public const string InvalidRequest = "invalid_request";
    public const string Unauthorized = "unauthorized";
    public const string Misconfigured = "misconfigured";
    public const string Open = "open";
    public const string Approved = "approved";
    public const string Rejected = "rejected";
    public const string Abandoned = "abandoned";

    public static string From(BusinessLookupOutcome outcome) => outcome switch
    {
        BusinessLookupOutcome.Found => Found,
        BusinessLookupOutcome.NotFound => NotFound,
        BusinessLookupOutcome.ProviderUnavailable => ProviderUnavailable,
        BusinessLookupOutcome.InvalidRequest => InvalidRequest,
        BusinessLookupOutcome.Unauthorized => Unauthorized,
        _ => throw new ArgumentOutOfRangeException(nameof(outcome), outcome, null),
    };
}

/// <summary>Entity type of attachments uploaded as an applicant's evidence; the entity id is the applicant id.</summary>
public static class VerificationFileEntityTypes
{
    public const string BusinessApplicant = "business_applicant";
}
