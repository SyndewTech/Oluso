using Oluso.Core.Domain.Entities;
using Oluso.Core.Domain.Interfaces;

namespace Oluso.Core.Services;

/// <summary>
/// One verification check of a claimed email domain, shared by the admin API's "verify now" and the background job.
/// </summary>
public static class TenantEmailDomainVerification
{
    /// <summary>Checks after which a pending claim fails (24 hours at the job's 5-minute interval).</summary>
    public const int MaxAttempts = 288;

    public const string HeldByAnotherTenantReason = "This domain is verified by another organization";

    /// <summary>
    /// Looks for the claim's TXT record and moves it to Verified when found, unless another tenant already holds the
    /// domain verified. Updates the attempt count and failure reason; the caller saves.
    /// </summary>
    public static async Task CheckAsync(
        TenantEmailDomain claim,
        ITenantEmailDomainStore store,
        IDnsVerificationService dns,
        CancellationToken cancellationToken)
    {
        claim.LastCheckedAt = DateTime.UtcNow;
        claim.CheckAttempts++;

        var holder = await store.GetVerifiedByDomainAsync(claim.Domain, cancellationToken);
        if (holder != null && holder.TenantId != claim.TenantId)
        {
            claim.FailureReason = HeldByAnotherTenantReason;
            return;
        }

        var result = await dns.VerifyTxtRecordAsync(claim.TxtRecordName, claim.TxtRecordValue, cancellationToken);
        if (result.Success)
        {
            claim.Status = TenantEmailDomainStatus.Verified;
            claim.VerifiedAt = DateTime.UtcNow;
            claim.LastRevalidatedAt = claim.VerifiedAt;
            claim.GracePeriodStartedAt = null;
            claim.FailureReason = null;
            return;
        }

        claim.FailureReason = result.FailureReason;
    }
}
