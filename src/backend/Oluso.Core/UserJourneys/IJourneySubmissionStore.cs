namespace Oluso.Core.UserJourneys;

/// <summary>
/// Store for journey submissions (data collected from data collection journeys)
/// </summary>
public interface IJourneySubmissionStore
{
    /// <summary>
    /// Gets a submission by ID
    /// </summary>
    Task<JourneySubmission?> GetAsync(string submissionId, CancellationToken cancellationToken = default);

    /// <summary>
    /// Gets all submissions for a policy
    /// </summary>
    Task<IEnumerable<JourneySubmission>> GetByPolicyAsync(
        string policyId,
        int skip = 0,
        int take = 50,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Gets submission count for a policy
    /// </summary>
    Task<int> CountByPolicyAsync(string policyId, CancellationToken cancellationToken = default);

    /// <summary>
    /// Checks if a duplicate submission exists
    /// </summary>
    Task<bool> HasDuplicateAsync(
        string policyId,
        string field,
        string value,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Saves a submission
    /// </summary>
    Task<string> SaveAsync(JourneySubmission submission, CancellationToken cancellationToken = default);

    /// <summary>
    /// Deletes a submission
    /// </summary>
    Task DeleteAsync(string submissionId, CancellationToken cancellationToken = default);

    /// <summary>
    /// Exports submissions for a policy
    /// </summary>
    Task<IEnumerable<JourneySubmission>> ExportAsync(
        string policyId,
        DateTime? from = null,
        DateTime? to = null,
        CancellationToken cancellationToken = default);
}

/// <summary>
/// A submission from a data collection journey
/// </summary>
public class JourneySubmission
{
    /// <summary>
    /// Unique submission ID
    /// </summary>
    public string Id { get; set; } = Guid.NewGuid().ToString();

    /// <summary>
    /// Policy ID that created this submission
    /// </summary>
    public required string PolicyId { get; init; }

    /// <summary>
    /// Policy name at time of submission
    /// </summary>
    public string? PolicyName { get; init; }

    /// <summary>
    /// Tenant ID (if multi-tenant)
    /// </summary>
    public string? TenantId { get; init; }

    /// <summary>
    /// Journey ID that created this submission
    /// </summary>
    public string? JourneyId { get; init; }

    /// <summary>
    /// Collected data as key-value pairs
    /// </summary>
    public IDictionary<string, object> Data { get; set; } = new Dictionary<string, object>();

    /// <summary>
    /// Metadata about the submission (IP, user agent, etc.)
    /// </summary>
    public SubmissionMetadata Metadata { get; set; } = new();

    /// <summary>
    /// Status of the submission
    /// </summary>
    public SubmissionStatus Status { get; set; } = SubmissionStatus.New;

    /// <summary>
    /// Notes or comments on the submission
    /// </summary>
    public string? Notes { get; set; }

    /// <summary>
    /// Tags for categorization
    /// </summary>
    public IList<string>? Tags { get; set; }

    public DateTime CreatedAt { get; init; } = DateTime.UtcNow;
    public DateTime? ReviewedAt { get; set; }
    public string? ReviewedBy { get; set; }
}

/// <summary>
/// Metadata about a submission
/// </summary>
public class SubmissionMetadata
{
    /// <summary>
    /// IP address of submitter
    /// </summary>
    public string? IpAddress { get; set; }

    /// <summary>
    /// User agent string
    /// </summary>
    public string? UserAgent { get; set; }

    /// <summary>
    /// Referrer URL
    /// </summary>
    public string? Referrer { get; set; }

    /// <summary>
    /// UTM parameters
    /// </summary>
    public IDictionary<string, string>? UtmParameters { get; set; }

    /// <summary>
    /// Country detected from IP
    /// </summary>
    public string? Country { get; set; }

    /// <summary>
    /// Browser locale
    /// </summary>
    public string? Locale { get; set; }
}

/// <summary>
/// Status of a submission
/// </summary>
public enum SubmissionStatus
{
    /// <summary>New unreviewed submission</summary>
    New,

    /// <summary>Submission has been reviewed</summary>
    Reviewed,

    /// <summary>Submission is being processed</summary>
    Processing,

    /// <summary>Submission has been approved/accepted</summary>
    Approved,

    /// <summary>Submission has been rejected</summary>
    Rejected,

    /// <summary>Submission requires follow-up</summary>
    FollowUp,

    /// <summary>Submission has been archived</summary>
    Archived
}
