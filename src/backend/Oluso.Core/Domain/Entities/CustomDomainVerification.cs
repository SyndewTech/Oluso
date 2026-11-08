using System.Text.Json.Serialization;

namespace Oluso.Core.Domain.Entities;

/// <summary>
/// Tracks the verification workflow for a tenant's custom domain.
/// A tenant may have one active custom domain at a time, but can have
/// multiple verification records (pending, failed, removed).
/// </summary>
public class CustomDomainVerification
{
    public string Id { get; set; } = Guid.NewGuid().ToString();

    /// <summary>
    /// The tenant requesting the custom domain.
    /// </summary>
    public string TenantId { get; set; } = string.Empty;
    public Tenant? Tenant { get; set; }

    /// <summary>
    /// The custom domain being verified (e.g., "login.customer.com").
    /// </summary>
    public string Domain { get; set; } = string.Empty;

    /// <summary>
    /// Current status of the verification workflow.
    /// </summary>
    public CustomDomainStatus Status { get; set; } = CustomDomainStatus.Pending;

    /// <summary>
    /// Random verification token generated at creation.
    /// </summary>
    public string VerificationToken { get; set; } = string.Empty;

    /// <summary>
    /// The DNS TXT record name the tenant must create (e.g., "_oluso-verify.login.customer.com").
    /// </summary>
    public string TxtRecordName { get; set; } = string.Empty;

    /// <summary>
    /// The DNS TXT record value to match (e.g., "oluso-verify=abc123").
    /// </summary>
    public string TxtRecordValue { get; set; } = string.Empty;

    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public DateTime? VerifiedAt { get; set; }
    public DateTime? ActivatedAt { get; set; }
    public DateTime? LastCheckedAt { get; set; }
    public int CheckAttempts { get; set; }
    public string? FailureReason { get; set; }

    /// <summary>
    /// Last time the TXT record was revalidated for an active domain.
    /// </summary>
    public DateTime? LastRevalidatedAt { get; set; }

    /// <summary>
    /// When the grace period started (TXT record missing during revalidation).
    /// Null if TXT is present. Deactivation after 7 days.
    /// </summary>
    public DateTime? GracePeriodStartedAt { get; set; }

    /// <summary>
    /// External ID from the edge provisioning provider (e.g. Cloudflare Custom Hostname ID).
    /// </summary>
    public string? ExternalProvisioningId { get; set; }
}

[JsonConverter(typeof(JsonStringEnumConverter))]
public enum CustomDomainStatus
{
    /// <summary>Created, awaiting DNS record creation by the tenant admin.</summary>
    Pending = 0,

    /// <summary>TXT record found and verified. Domain is ready to activate.</summary>
    Verified = 1,

    /// <summary>Domain is live and serving traffic. Tenant.CustomDomain is set.</summary>
    Active = 2,

    /// <summary>Verification failed after maximum attempts.</summary>
    Failed = 3,

    /// <summary>Domain was removed by the tenant admin.</summary>
    Removed = 4
}
