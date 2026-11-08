using System.Text.Json.Serialization;

namespace Oluso.Core.Domain.Entities;

/// <summary>
/// An email domain a tenant has claimed, and whether it has proven it controls the domain's DNS. Addresses at a
/// verified domain (or a subdomain of it) can be trusted as the tenant's own without a code being sent to each
/// inbox: provisioning, an identity provider the tenant configured, or an admin can mark them verified, and an
/// external sign-in at that address can join the existing account.
/// </summary>
/// <remarks>
/// Separate from <see cref="CustomDomainVerification"/>, which proves a host name the tenant's sign-in pages are
/// served on (usually a subdomain). This one is the domain people's addresses are at (usually the apex).
/// </remarks>
public class TenantEmailDomain
{
    public const string TxtRecordPrefix = "_oluso-verify.";
    public const string TxtValuePrefix = "oluso-email-verify=";

    public string Id { get; set; } = Guid.NewGuid().ToString();

    public string TenantId { get; set; } = string.Empty;
    public Tenant? Tenant { get; set; }

    /// <summary>The domain, lower case (e.g. "acme.com"). Its subdomains are covered too.</summary>
    public string Domain { get; set; } = string.Empty;

    public TenantEmailDomainStatus Status { get; set; } = TenantEmailDomainStatus.Pending;

    public string VerificationToken { get; set; } = string.Empty;

    /// <summary>The TXT record name to create (e.g. "_oluso-verify.acme.com").</summary>
    public string TxtRecordName { get; set; } = string.Empty;

    /// <summary>The TXT record value to publish (e.g. "oluso-email-verify=abc123").</summary>
    public string TxtRecordValue { get; set; } = string.Empty;

    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public DateTime? VerifiedAt { get; set; }
    public DateTime? LastCheckedAt { get; set; }
    public int CheckAttempts { get; set; }
    public string? FailureReason { get; set; }

    /// <summary>Last time the TXT record of a verified domain was checked again.</summary>
    public DateTime? LastRevalidatedAt { get; set; }

    /// <summary>When a revalidation first found the TXT record missing; null while it is present.</summary>
    public DateTime? GracePeriodStartedAt { get; set; }

    /// <summary>
    /// The domain and each parent of it an address at <paramref name="email"/> is covered by, most specific first
    /// ("x@eu.acme.com" gives "eu.acme.com", "acme.com"). Empty when the address has no domain.
    /// </summary>
    public static IReadOnlyList<string> CoveringDomains(string? email)
    {
        if (string.IsNullOrWhiteSpace(email))
        {
            return Array.Empty<string>();
        }

        var at = email.LastIndexOf('@');
        if (at < 0 || at == email.Length - 1)
        {
            return Array.Empty<string>();
        }

        var domain = email[(at + 1)..].Trim().TrimEnd('.').ToLowerInvariant();
        var labels = domain.Split('.', StringSplitOptions.RemoveEmptyEntries);
        var domains = new List<string>();
        for (var i = 0; i < labels.Length - 1; i++)
        {
            domains.Add(string.Join('.', labels[i..]));
        }
        return domains;
    }
}

[JsonConverter(typeof(JsonStringEnumConverter))]
public enum TenantEmailDomainStatus
{
    /// <summary>Claimed; waiting for the TXT record.</summary>
    Pending = 0,

    /// <summary>TXT record found: the tenant controls the domain.</summary>
    Verified = 1,

    /// <summary>Never verified in time, or the TXT record stayed missing past the grace period.</summary>
    Failed = 2,

    /// <summary>Removed by a tenant admin.</summary>
    Removed = 3
}
