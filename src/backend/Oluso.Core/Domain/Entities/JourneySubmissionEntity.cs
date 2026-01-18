namespace Oluso.Core.Domain.Entities;

/// <summary>
/// Entity for storing journey submissions (data collection journeys like waitlists, surveys)
/// </summary>
public class JourneySubmissionEntity : TenantEntity
{
    /// <summary>
    /// Unique submission ID
    /// </summary>
    public string Id { get; set; } = Guid.NewGuid().ToString();

    /// <summary>
    /// Policy ID that created this submission
    /// </summary>
    public string PolicyId { get; set; } = default!;

    /// <summary>
    /// Policy name at time of submission
    /// </summary>
    public string? PolicyName { get; set; }

    /// <summary>
    /// Journey ID that created this submission
    /// </summary>
    public string? JourneyId { get; set; }

    /// <summary>
    /// JSON-serialized collected data
    /// </summary>
    public string Data { get; set; } = "{}";

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
    /// JSON-serialized UTM parameters
    /// </summary>
    public string? UtmParameters { get; set; }

    /// <summary>
    /// Country detected from IP
    /// </summary>
    public string? Country { get; set; }

    /// <summary>
    /// Browser locale
    /// </summary>
    public string? Locale { get; set; }

    /// <summary>
    /// Status of the submission
    /// </summary>
    public string Status { get; set; } = "New";

    /// <summary>
    /// Notes or comments on the submission
    /// </summary>
    public string? Notes { get; set; }

    /// <summary>
    /// Comma-separated tags for categorization
    /// </summary>
    public string? Tags { get; set; }

    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public DateTime? ReviewedAt { get; set; }
    public string? ReviewedBy { get; set; }
}
