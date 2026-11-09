namespace Oluso.Enterprise.Verification.Dto;

/// <summary>A verification vendor the platform supports, described for the settings form.</summary>
public class VerificationProviderInfoDto
{
    public string Id { get; set; } = null!;
    public string DisplayName { get; set; } = null!;

    /// <summary>ISO 3166-1 alpha-2 codes of the company registries it can search.</summary>
    public List<string> BusinessCountries { get; set; } = new();

    /// <summary>Countries its hosted (provider-run) business verification covers; empty when it has none.</summary>
    public List<string> HostedCountries { get; set; } = new();

    /// <summary>The URL to give the provider for webhooks, on this organisation's host; null when it sends none.</summary>
    public string? WebhookUrl { get; set; }
    public List<VerificationCredentialFieldDto> CredentialFields { get; set; } = new();
    public List<VerificationProviderOptionDto> Options { get; set; } = new();
}

public class VerificationCredentialFieldDto
{
    public string Name { get; set; } = null!;
    public string Label { get; set; } = null!;
    public bool Secret { get; set; }
    public string? Description { get; set; }

    /// <summary>Needed to enable the provider; the others serve one capability (e.g. hosted sessions).</summary>
    public bool Required { get; set; }
}

public class VerificationProviderOptionDto
{
    public string Name { get; set; } = null!;
    public string Label { get; set; } = null!;
    public List<string> Choices { get; set; } = new();
    public string Default { get; set; } = null!;
    public string? Description { get; set; }
}

/// <summary>A country businesses can be verified in, and the provider the organisation routes it to.</summary>
public class BusinessVerificationCountryDto
{
    public string Code { get; set; } = null!;
    public string Name { get; set; } = null!;
    public List<string> ProviderIds { get; set; } = new();

    /// <summary>The routed provider's id, or null when the organisation hasn't routed the country.</summary>
    public string? RoutedProviderId { get; set; }

    /// <summary>The routed provider's name, or "Not routed".</summary>
    public string ProviderName { get; set; } = null!;
}

/// <summary>The organisation's verification settings as shown to administrators. Secret values are never returned, only whether they're set.</summary>
public class IdentityVerificationSettingsDto
{
    public List<ConfiguredVerificationProviderDto> Providers { get; set; } = new();

    /// <summary>Country code → provider id.</summary>
    public Dictionary<string, string> BusinessRouting { get; set; } = new();
    public int BusinessReuseMaxAgeDays { get; set; }
}

public class ConfiguredVerificationProviderDto
{
    public string ProviderId { get; set; } = null!;
    public bool Enabled { get; set; }

    /// <summary>Sandbox or Live.</summary>
    public string Environment { get; set; } = null!;

    /// <summary>Non-secret credential values.</summary>
    public Dictionary<string, string> Credentials { get; set; } = new();

    /// <summary>Names of the secret credentials that are stored.</summary>
    public List<string> StoredSecrets { get; set; } = new();
    public Dictionary<string, string> Options { get; set; } = new();
}

public class UpdateIdentityVerificationSettingsRequest
{
    public List<UpdateVerificationProviderRequest> Providers { get; set; } = new();
    public Dictionary<string, string> BusinessRouting { get; set; } = new();
    public int BusinessReuseMaxAgeDays { get; set; }
}

public class UpdateVerificationProviderRequest
{
    public string ProviderId { get; set; } = null!;
    public bool Enabled { get; set; }
    public string Environment { get; set; } = null!;

    /// <summary>A blank secret keeps the stored value.</summary>
    public Dictionary<string, string?> Credentials { get; set; } = new();
    public Dictionary<string, string> Options { get; set; } = new();
}

public class VerificationConnectionTestResultDto
{
    public bool Success { get; set; }
    public string Message { get; set; } = null!;
}

/// <summary>A business in the admin list.</summary>
public class VerifiedBusinessSummaryDto
{
    public string Id { get; set; } = null!;

    /// <summary>pending, in_review, verified, rejected or failed.</summary>
    public string Status { get; set; } = null!;
    public string? StatusReason { get; set; }
    public string Country { get; set; } = null!;
    public string RegistrationNumber { get; set; } = null!;
    public string? RegisteredName { get; set; }
    public string? RegistryProviderId { get; set; }
    public DateTime? RegistryCheckedAt { get; set; }
    public int ApplicantCount { get; set; }
    public DateTime CreatedAt { get; set; }
    public DateTime UpdatedAt { get; set; }
}

public class VerifiedBusinessDetailDto : VerifiedBusinessSummaryDto
{
    public string? RegistrationType { get; set; }
    public string? StatusMessage { get; set; }
    public string? EntityType { get; set; }
    public string? RegistrationDate { get; set; }
    public string? RegisteredAddress { get; set; }

    /// <summary>Unknown, Active or Inactive.</summary>
    public string ActivityStatus { get; set; } = null!;
    public string? RawStatus { get; set; }
    public string? RegistryEnvironment { get; set; }
    public List<BusinessPersonDto> People { get; set; } = new();
    public DateTime? DecidedAt { get; set; }
    public string? ReviewedBy { get; set; }
    public DateTime? ReviewedAt { get; set; }
    public string? ReviewNote { get; set; }
    public List<BusinessApplicantDto> Applicants { get; set; } = new();
    public List<BusinessCheckDto> Checks { get; set; } = new();
}

public class BusinessPersonDto
{
    public string Name { get; set; } = null!;
    public string? Role { get; set; }
}

/// <summary>A person acting for a business.</summary>
public class BusinessApplicantDto
{
    public string Id { get; set; } = null!;
    public string BusinessId { get; set; } = null!;

    /// <summary>The user; null while the journey that made the link has none.</summary>
    public string? SubjectId { get; set; }

    /// <summary>The user's display name or email, when the user exists.</summary>
    public string? SubjectName { get; set; }
    public string Relationship { get; set; } = null!;

    /// <summary>None, DirectorMatch, Documents or DirectorOrDocuments.</summary>
    public string EvidenceRequirement { get; set; } = null!;
    public string Status { get; set; } = null!;
    public string? StatusReason { get; set; }
    public string? StatusMessage { get; set; }
    public string? ProvidedBusinessName { get; set; }
    public bool? NameMatched { get; set; }
    public bool? DirectorMatched { get; set; }
    public string? MatchedPersonName { get; set; }
    public string? MatchedPersonRole { get; set; }
    public List<BusinessDocumentDto> Documents { get; set; } = new();
    public bool OutcomeEmailRequested { get; set; }
    public DateTime CreatedAt { get; set; }
    public DateTime UpdatedAt { get; set; }
    public DateTime? DecidedAt { get; set; }
    public string? ReviewedBy { get; set; }
    public DateTime? ReviewedAt { get; set; }
    public string? ReviewNote { get; set; }
}

/// <summary>An uploaded piece of evidence; opened through the download endpoint, never a public URL.</summary>
public class BusinessDocumentDto
{
    public string Id { get; set; } = null!;
    public string FileName { get; set; } = null!;
    public string ContentType { get; set; } = null!;
    public long SizeBytes { get; set; }
    public DateTime CreatedAt { get; set; }
}

/// <summary>One lookup, reuse, hosted session or review.</summary>
public class BusinessCheckDto
{
    public string Id { get; set; } = null!;
    public string? ApplicantId { get; set; }
    public string Kind { get; set; } = null!;
    public string Outcome { get; set; } = null!;
    public string? ProviderId { get; set; }
    public string? Environment { get; set; }
    public string? ProviderReference { get; set; }
    public string? SessionReference { get; set; }
    public string? Message { get; set; }
    public string? ReusedFromCheckId { get; set; }
    public string? OriginKind { get; set; }
    public string? OriginId { get; set; }
    public string? OriginName { get; set; }
    public DateTime CreatedAt { get; set; }
    public DateTime? CompletedAt { get; set; }
}

public class VerifiedBusinessListResponse
{
    public List<VerifiedBusinessSummaryDto> Items { get; set; } = new();
    public int TotalCount { get; set; }
    public int Page { get; set; }
    public int PageSize { get; set; }
}

/// <summary>An applicant waiting on a decision, with the business they act for.</summary>
public class BusinessReviewQueueItemDto
{
    public BusinessApplicantDto Applicant { get; set; } = null!;
    public VerifiedBusinessSummaryDto Business { get; set; } = null!;
}

public class BusinessReviewQueueResponse
{
    public List<BusinessReviewQueueItemDto> Items { get; set; } = new();
    public int TotalCount { get; set; }
    public int Page { get; set; }
    public int PageSize { get; set; }
}

public class ReviewBusinessVerificationRequest
{
    /// <summary>"approve" or "reject".</summary>
    public string Decision { get; set; } = null!;
    public string? Note { get; set; }
}

/// <summary>A business as one of the people acting for it sees it.</summary>
public class MyBusinessDto
{
    public string BusinessId { get; set; } = null!;
    public string ApplicantId { get; set; } = null!;
    public string Country { get; set; } = null!;
    public string RegistrationNumber { get; set; } = null!;
    public string? RegisteredName { get; set; }
    public string? RegisteredAddress { get; set; }
    public string BusinessStatus { get; set; } = null!;
    public string Status { get; set; } = null!;
    public string? StatusReason { get; set; }
    public string Relationship { get; set; } = null!;
    public List<BusinessDocumentDto> Documents { get; set; } = new();
    public DateTime UpdatedAt { get; set; }
}
