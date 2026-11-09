using Oluso.Core.UserJourneys;

namespace Oluso.Admin.Dto.Submissions;

public class DataCollectionPolicyDto
{
    public string Id { get; set; } = null!;
    public string Name { get; set; } = null!;
    public string? Description { get; set; }
    public string Type { get; set; } = null!;
    public bool Enabled { get; set; }
    public int SubmissionCount { get; set; }
    public int MaxSubmissions { get; set; }
    public bool AllowDuplicates { get; set; }
    public DateTime CreatedAt { get; set; }

    /// <summary>The module that owns the journey; its answers go only to that module and are never shown here.</summary>
    public string? OwnerModule { get; set; }
    public string? OwnerModuleName { get; set; }

    /// <summary>True when only counts, times and statuses are shown for this journey, never answers.</summary>
    public bool AnswersHidden { get; set; }
}

public class SubmissionDto
{
    public string Id { get; set; } = null!;
    public string PolicyId { get; set; } = null!;
    public string? PolicyName { get; set; }
    public string? TenantId { get; set; }
    public string? JourneyId { get; set; }
    public Dictionary<string, object> Data { get; set; } = new();
    public SubmissionMetadataDto Metadata { get; set; } = new();
    public SubmissionStatus Status { get; set; }
    public string? Notes { get; set; }
    public List<string>? Tags { get; set; }
    public DateTime CreatedAt { get; set; }
    public DateTime? ReviewedAt { get; set; }
    public string? ReviewedBy { get; set; }
}

public class SubmissionMetadataDto
{
    public string? IpAddress { get; set; }
    public string? UserAgent { get; set; }
    public string? Referrer { get; set; }
    public Dictionary<string, string>? UtmParameters { get; set; }
    public string? Country { get; set; }
    public string? Locale { get; set; }
}

public class UpdateSubmissionRequest
{
    public SubmissionStatus? Status { get; set; }
    public string? Notes { get; set; }
    public List<string>? Tags { get; set; }
}

public class BulkUpdateSubmissionsRequest
{
    public List<string> SubmissionIds { get; set; } = new();
    public SubmissionStatus? Status { get; set; }
    public List<string>? Tags { get; set; }
    public List<string>? AddTags { get; set; }
    public List<string>? RemoveTags { get; set; }
}

public class BulkDeleteSubmissionsRequest
{
    public List<string> SubmissionIds { get; set; } = new();
}

public class SubmissionStatisticsDto
{
    public int Total { get; set; }
    public Dictionary<string, int> ByStatus { get; set; } = new();
    public int Today { get; set; }
    public int ThisWeek { get; set; }
    public int ThisMonth { get; set; }
    public double AveragePerDay { get; set; }
    public string RecentTrend { get; set; } = "stable";
    public double? TrendPercentage { get; set; }
}
