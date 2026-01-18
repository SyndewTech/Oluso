namespace Oluso.Core.Domain.Entities;

/// <summary>
/// Persisted journey policy for authentication flows
/// </summary>
public class JourneyPolicyEntity : TenantEntity
{
    public string Id { get; set; } = default!;
    public string Name { get; set; } = default!;
    public string Type { get; set; } = default!;
    public bool Enabled { get; set; } = true;
    public int Priority { get; set; } = 100;
    public string? Description { get; set; }
    public int Version { get; set; } = 1;

    /// <summary>
    /// JSON-serialized steps array
    /// </summary>
    public string Steps { get; set; } = "[]";

    /// <summary>
    /// JSON-serialized conditions array
    /// </summary>
    public string? Conditions { get; set; }

    /// <summary>
    /// JSON-serialized output claims array
    /// </summary>
    public string? OutputClaims { get; set; }

    /// <summary>
    /// JSON-serialized session configuration
    /// </summary>
    public string? SessionConfig { get; set; }

    /// <summary>
    /// JSON-serialized UI configuration
    /// </summary>
    public string? UiConfig { get; set; }

    public int DefaultStepTimeoutSeconds { get; set; } = 300;
    public int MaxJourneyDurationMinutes { get; set; } = 30;

    /// <summary>
    /// Comma-separated tags
    /// </summary>
    public string? Tags { get; set; }

    /// <summary>
    /// Whether this journey requires authentication
    /// </summary>
    public bool RequiresAuthentication { get; set; } = true;

    /// <summary>
    /// Whether to persist collected data as submissions
    /// </summary>
    public bool PersistSubmissions { get; set; } = false;

    /// <summary>
    /// Collection name for storing submissions
    /// </summary>
    public string? SubmissionCollection { get; set; }

    /// <summary>
    /// Maximum submissions allowed (0 = unlimited)
    /// </summary>
    public int MaxSubmissions { get; set; } = 0;

    /// <summary>
    /// Whether to allow duplicate submissions
    /// </summary>
    public bool AllowDuplicates { get; set; } = false;

    /// <summary>
    /// Comma-separated fields for duplicate detection
    /// </summary>
    public string? DuplicateCheckFields { get; set; }

    /// <summary>
    /// Redirect URL after successful submission
    /// </summary>
    public string? SuccessRedirectUrl { get; set; }

    /// <summary>
    /// Success message to display after submission
    /// </summary>
    public string? SuccessMessage { get; set; }

    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public DateTime UpdatedAt { get; set; } = DateTime.UtcNow;
}
