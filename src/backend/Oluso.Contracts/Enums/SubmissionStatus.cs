using System.Text.Json.Serialization;

namespace Oluso.Core.UserJourneys;

/// <summary>
/// Status of a submission
/// </summary>
[JsonConverter(typeof(JsonStringEnumConverter))]
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
