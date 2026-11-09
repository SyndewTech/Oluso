using Oluso.Core.Domain.Entities;

namespace Oluso.Core.Services;

/// <summary>
/// Result of a DNS verification attempt for a custom domain.
/// </summary>
public class DnsVerificationResult
{
    public bool Success { get; init; }
    public bool TxtRecordFound { get; init; }
    public string? FailureReason { get; init; }

    public static DnsVerificationResult Verified() => new() { Success = true, TxtRecordFound = true };
    public static DnsVerificationResult Failed(string reason) => new() { Success = false, FailureReason = reason };
    public static DnsVerificationResult TxtNotFound() => new() { Success = false, TxtRecordFound = false, FailureReason = "TXT record not found" };
}

/// <summary>
/// Verifies DNS records for custom domain ownership.
/// </summary>
public interface IDnsVerificationService
{
    Task<DnsVerificationResult> VerifyDomainAsync(CustomDomainVerification verification, CancellationToken ct = default);

    /// <summary>Whether a TXT record named <paramref name="recordName"/> carries <paramref name="expectedValue"/>.</summary>
    Task<DnsVerificationResult> VerifyTxtRecordAsync(string recordName, string expectedValue, CancellationToken ct = default);
}
