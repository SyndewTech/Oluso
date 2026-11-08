namespace Oluso.Admin.Dto.Journeys;

/// <summary>
/// Portable form of a journey policy. Carries everything needed to recreate
/// the policy in another tenant or host; never the source tenant, the embed
/// API key or protected step secrets.
/// </summary>
public class JourneyExportDocument
{
    public const string CurrentFormat = "oluso.journey";
    public const int CurrentVersion = 1;

    public string Format { get; set; } = CurrentFormat;
    public int Version { get; set; } = CurrentVersion;
    public DateTime ExportedAt { get; set; } = DateTime.UtcNow;
    public string? SourceName { get; set; }
    public CreatePolicyRequest Policy { get; set; } = null!;
}

public class ImportPolicyRequest
{
    /// <summary>The exported file's content: the envelope, or its plain JSON document.</summary>
    public string Content { get; set; } = null!;
    public string? NewName { get; set; }

    /// <summary>
    /// Slug for the imported policy instead of the one in the file, e.g. when the file's
    /// slug is already in use.
    /// </summary>
    public string? NewSlug { get; set; }

    /// <summary>Derive a free slug from the policy's name rather than keeping the file's.</summary>
    public bool GenerateSlug { get; set; }
}

/// <summary>Returned with 409 when the slug a create, update, clone or import asks for is in use.</summary>
public class PolicySlugConflictDto
{
    public const string ErrorCode = "policy_slug_taken";

    public string Code { get; set; } = ErrorCode;
    public string Slug { get; set; } = null!;
    public string SuggestedSlug { get; set; } = null!;
    public string[] Errors { get; set; } = Array.Empty<string>();
}

public class ImportPolicyResultDto
{
    public PolicyDetailDto Policy { get; set; } = null!;
    /// <summary>
    /// Step settings that were redacted on export and removed on import, as
    /// "{stepId}.{key}". The admin has to re-enter these before enabling.
    /// </summary>
    public List<string> RedactedSettings { get; set; } = new();
    public List<string> Warnings { get; set; } = new();
}
