using Oluso.Core.UserJourneys;

namespace Oluso.Admin.Dto.Journeys;

public class PolicyListItemDto
{
    public string Id { get; set; } = null!;
    public string Slug { get; set; } = null!;
    public string Name { get; set; } = null!;
    public string? Description { get; set; }
    public string Type { get; set; } = null!;
    public bool Enabled { get; set; }
    public int Priority { get; set; }
    public bool IsGlobal { get; set; }

    /// <summary>Feature key of the module that owns the journey; null for a platform journey.</summary>
    public string? OwnerModule { get; set; }
    public string? OwnerModuleName { get; set; }

    /// <summary><c>Private</c> or <c>Shared</c>.</summary>
    public string Sharing { get; set; } = "Private";
    public int StepCount { get; set; }
    public int Version { get; set; }
    public DateTime CreatedAt { get; set; }
    public DateTime? UpdatedAt { get; set; }
}

public class PolicyDetailDto
{
    public string Id { get; set; } = null!;
    public string Slug { get; set; } = null!;
    public string Name { get; set; } = null!;
    public string? Description { get; set; }
    public string Type { get; set; } = null!;
    public string? TenantId { get; set; }
    public bool Enabled { get; set; }
    public int Priority { get; set; }

    /// <summary>Feature key of the module that owns the journey; null for a platform journey.</summary>
    public string? OwnerModule { get; set; }
    public string? OwnerModuleName { get; set; }

    /// <summary><c>Private</c> or <c>Shared</c>.</summary>
    public string Sharing { get; set; } = "Private";
    public List<JourneyPolicyStepDto> Steps { get; set; } = new();
    public IList<JourneyPolicyCondition>? Conditions { get; set; }
    public IList<ClaimMapping>? OutputClaims { get; set; }
    public SessionConfiguration? Session { get; set; }
    public JourneyUiConfiguration? Ui { get; set; }
    public int Version { get; set; }
    public DateTime CreatedAt { get; set; }
    public DateTime? UpdatedAt { get; set; }
    public bool RequiresAuthentication { get; set; } = true;
    public bool PersistSubmissions { get; set; }
    public string? SubmissionCollection { get; set; }
    public int MaxSubmissions { get; set; }
    public bool AllowDuplicates { get; set; }
    public List<string>? DuplicateCheckFields { get; set; }
    public string? SuccessRedirectUrl { get; set; }
    public string? SuccessMessage { get; set; }
    public string? AllowedOrigins { get; set; }
    public string? ApiKey { get; set; }
    public bool RequireApiKey { get; set; }
}

public class CreatePolicyRequest
{
    /// <summary>
    /// The handle clients use (<c>?policy=</c>, share links, the public API). Optional on
    /// create: a free slug is derived from the name. Unique within the tenant and against
    /// global policies.
    /// </summary>
    public string? Slug { get; set; }
    public string Name { get; set; } = null!;
    public string? Description { get; set; }
    public string Type { get; set; } = null!;
    public bool Enabled { get; set; } = true;
    public int Priority { get; set; } = 100;

    /// <summary>Feature key of the owning module; omit for a platform journey. Needs the module's admin permission.</summary>
    public string? OwnerModule { get; set; }

    /// <summary><c>Private</c> (default) or <c>Shared</c>; only meaningful with <see cref="OwnerModule"/>.</summary>
    public string? Sharing { get; set; }
    public List<JourneyPolicyStepDto> Steps { get; set; } = new();
    public IList<JourneyPolicyCondition>? Conditions { get; set; }
    public IList<ClaimMapping>? OutputClaims { get; set; }
    public SessionConfiguration? Session { get; set; }
    public JourneyUiConfiguration? Ui { get; set; }
    public bool? RequiresAuthentication { get; set; }
    public bool? PersistSubmissions { get; set; }
    public string? SubmissionCollection { get; set; }
    public int? MaxSubmissions { get; set; }
    public bool? AllowDuplicates { get; set; }
    public List<string>? DuplicateCheckFields { get; set; }
    public string? SuccessRedirectUrl { get; set; }
    public string? SuccessMessage { get; set; }
    public string? AllowedOrigins { get; set; }
    public string? ApiKey { get; set; }
    public bool? RequireApiKey { get; set; }
}

public class UpdatePolicyRequest
{
    public string? Name { get; set; }

    /// <summary>A new slug. Clients still using the old one stop reaching this policy.</summary>
    public string? Slug { get; set; }
    public string? Description { get; set; }
    public bool? Enabled { get; set; }
    public int? Priority { get; set; }

    /// <summary>A new owning module's feature key, or an empty string to make it a platform journey; null leaves
    /// it unchanged. Changing the owner needs the admin permission of every module involved.</summary>
    public string? OwnerModule { get; set; }

    /// <summary><c>Private</c> or <c>Shared</c>; null leaves it unchanged.</summary>
    public string? Sharing { get; set; }
    public List<JourneyPolicyStepDto>? Steps { get; set; }
    public IList<JourneyPolicyCondition>? Conditions { get; set; }
    public IList<ClaimMapping>? OutputClaims { get; set; }
    public SessionConfiguration? Session { get; set; }
    public JourneyUiConfiguration? Ui { get; set; }
    public bool? RequiresAuthentication { get; set; }
    public bool? PersistSubmissions { get; set; }
    public string? SubmissionCollection { get; set; }
    public int? MaxSubmissions { get; set; }
    public bool? AllowDuplicates { get; set; }
    public List<string>? DuplicateCheckFields { get; set; }
    public string? SuccessRedirectUrl { get; set; }
    public string? SuccessMessage { get; set; }
    public string? AllowedOrigins { get; set; }
    public string? ApiKey { get; set; }
    public bool? RequireApiKey { get; set; }
}

public class ClonePolicyRequest
{
    /// <summary>Slug for the copy; a free one is derived from its name when omitted.</summary>
    public string? NewSlug { get; set; }
    public string? NewName { get; set; }
}

public class SetStatusRequest
{
    public bool Enabled { get; set; }
}

public class JourneyShareUrlDto
{
    public string Url { get; set; } = null!;
}

public class ValidatePolicyRequest
{
    public List<JourneyPolicyStepDto> Steps { get; set; } = new();
}

public class JourneyPolicyStepDto
{
    public required string Id { get; init; }
    public required string Type { get; init; }
    public string? DisplayName { get; set; }
    public bool Optional { get; set; }
    public int Order { get; set; }
    public IDictionary<string, object>? Configuration { get; set; }
    public IDictionary<string, string>? Branches { get; set; }
    public IList<StepCondition>? Conditions { get; set; }
    public string? Layout { get; set; }
    public string? OnSuccess { get; set; }
    public string? OnFailure { get; set; }
    public bool SkipIfCompleted { get; set; }
    public int MaxRetries { get; set; }
    public int TimeoutSeconds { get; set; }
    public string? ErrorMessageTemplate { get; set; }
    public IList<string>? RequiredClaims { get; set; }
    public IList<string>? OutputClaims { get; set; }
    public string? PluginName { get; set; }
}

public class ValidationResultDto
{
    public bool IsValid { get; set; }
    public List<string> Errors { get; set; } = new();
    public List<string> Warnings { get; set; } = new();
}

public class ValidateStepRequest
{
    public string Type { get; set; } = null!;
    public IDictionary<string, object>? Configuration { get; set; }
}

public class StepValidationResultDto
{
    public bool IsValid { get; set; }
    public List<string> Errors { get; set; } = new();
    public List<string> Warnings { get; set; } = new();
}

public class TestStepRequest
{
    public string Type { get; set; } = null!;
    public IDictionary<string, object>? Configuration { get; set; }

    /// <summary>The saved journey and step, so secrets the builder shows redacted are tested with their stored values.</summary>
    public string? PolicyId { get; set; }
    public string? StepId { get; set; }
}

public class StepTestResultDto
{
    public bool Success { get; set; }
    public string Message { get; set; } = null!;
    public List<string>? Details { get; set; }
}

public class StepTypeInfoDto
{
    public string Type { get; set; } = null!;
    public string? TypeId { get; set; }
    public string Category { get; set; } = null!;
    public string DisplayName { get; set; } = null!;
    public string Description { get; set; } = null!;
    public string? Icon { get; set; }
    public string? Module { get; set; }
    public bool IsAvailable { get; set; }

    /// <summary>The step can check its settings from the builder (<c>test-step</c>).</summary>
    public bool Testable { get; set; }
    public Dictionary<string, object>? ConfigurationSchema { get; set; }
    public List<StepBranchInfoDto>? AvailableBranches { get; set; }
}

public class StepBranchInfoDto
{
    public string Id { get; set; } = null!;
    public string? DisplayName { get; set; }
    public string? Description { get; set; }
}

/// <summary>
/// A custom-page Razor template the host has compiled in under
/// <c>Views/Shared/Journey/Templates/</c>. Drives the <c>templatePath</c>
/// dropdown for the <c>custom_page</c> step.
/// </summary>
public class JourneyTemplateDto
{
    /// <summary>Bare name a step's <c>templatePath</c> expects (e.g. "Sample" for _Sample.cshtml).</summary>
    public string Name { get; set; } = null!;
    public string DisplayName { get; set; } = null!;
    public string? Description { get; set; }
}

public class AvailablePluginDto
{
    public string Name { get; set; } = null!;
    public string DisplayName { get; set; } = null!;
    public string? Description { get; set; }
    public string? Version { get; set; }
    public string? Author { get; set; }
    public string Scope { get; set; } = null!;
    public bool IsGlobal { get; set; }
    public List<string>? RequiredClaims { get; set; }
    public List<string>? OutputClaims { get; set; }
    public Dictionary<string, object>? ConfigSchema { get; set; }
}

/// <summary>A module a journey can be owned by.</summary>
public class JourneyOwnerModuleDto
{
    public string Key { get; set; } = null!;
    public string Name { get; set; } = null!;
}
