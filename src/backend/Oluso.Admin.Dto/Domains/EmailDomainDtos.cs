namespace Oluso.Admin.Dto.Domains;

public record EmailDomainDto(
    string Id, string TenantId, string Domain, string Status,
    string TxtRecordName, string TxtRecordValue,
    DateTime CreatedAt, DateTime? VerifiedAt, DateTime? LastCheckedAt, DateTime? LastRevalidatedAt,
    int CheckAttempts, string? FailureReason);

public record AddEmailDomainRequest
{
    public string Domain { get; init; } = string.Empty;
}
