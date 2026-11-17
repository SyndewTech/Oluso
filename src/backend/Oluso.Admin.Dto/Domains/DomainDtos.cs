namespace Oluso.Admin.Dto.Domains;

public record DomainVerificationDto(
    string Id, string TenantId, string Domain, string Status,
    string TxtRecordName, string TxtRecordValue,
    string? CnameTarget,
    string? ExternalProvisioningId,
    DateTime CreatedAt, DateTime? VerifiedAt, DateTime? ActivatedAt,
    DateTime? LastCheckedAt, int CheckAttempts, string? FailureReason);

public record AddDomainRequest
{
    public string Domain { get; init; } = string.Empty;
}

/// <summary>
/// One custom domain record in the platform view. <see cref="IsServing"/> is true when the tenant's custom domain is this
/// one, which is what serves its sign-in pages.
/// </summary>
public record PlatformDomainDto(
    string Id, string Domain, string Status,
    string TenantId, string? TenantName, string? TenantIdentifier, bool IsServing,
    string? ExternalProvisioningId,
    DateTime CreatedAt, DateTime? VerifiedAt, DateTime? ActivatedAt,
    DateTime? LastCheckedAt, DateTime? LastRevalidatedAt, DateTime? GracePeriodStartedAt,
    int CheckAttempts, string? FailureReason);
