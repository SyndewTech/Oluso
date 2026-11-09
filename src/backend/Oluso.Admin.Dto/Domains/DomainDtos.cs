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
