namespace Oluso.Core.Services;

/// <summary>
/// Provisions custom domains on an edge provider (e.g. Cloudflare Custom Hostnames).
/// Called after a domain is verified/activated and when it's removed.
/// Implementations are optional — if not registered, domain lifecycle proceeds without edge provisioning.
/// </summary>
public interface ICustomDomainProvisioningService
{
    /// <summary>
    /// Provision the custom domain on the edge provider.
    /// Called after DNS verification succeeds and domain is activated.
    /// </summary>
    Task<DomainProvisioningResult> ProvisionAsync(string domain, string tenantId, CancellationToken ct = default);

    /// <summary>
    /// Remove the custom domain from the edge provider.
    /// Called when a tenant admin removes the domain or revalidation fails.
    /// </summary>
    Task DeprovisionAsync(string domain, CancellationToken ct = default);

    /// <summary>
    /// Read the current edge state for a domain.
    /// <para>
    /// <see cref="ProvisionAsync"/> only submits the hostname — providers that
    /// issue a DV certificate (Cloudflare SSL for SaaS, for example) return
    /// immediately with the certificate still pending. A domain must not be
    /// treated as live until this reports <see cref="EdgeProvisioningState.Active"/>.
    /// </para>
    /// </summary>
    Task<DomainProvisioningStatus> GetStatusAsync(string domain, CancellationToken ct = default);
}

/// <summary>
/// Where a domain currently sits on the edge provider.
/// </summary>
public enum EdgeProvisioningState
{
    /// <summary>The provider has no record of this hostname.</summary>
    NotFound = 0,

    /// <summary>Submitted, but the certificate or hostname validation hasn't completed.</summary>
    Pending = 1,

    /// <summary>Hostname validated and certificate issued — safe to serve traffic.</summary>
    Active = 2,

    /// <summary>The provider rejected the hostname or validation failed permanently.</summary>
    Failed = 3
}

public class DomainProvisioningStatus
{
    public EdgeProvisioningState State { get; init; }

    /// <summary>Provider's raw status string, surfaced to admins for diagnosis.</summary>
    public string? Detail { get; init; }

    public string? ExternalId { get; init; }

    public static DomainProvisioningStatus NotFound() => new() { State = EdgeProvisioningState.NotFound };
    public static DomainProvisioningStatus Pending(string? detail = null, string? externalId = null) =>
        new() { State = EdgeProvisioningState.Pending, Detail = detail, ExternalId = externalId };
    public static DomainProvisioningStatus Active(string? externalId = null) =>
        new() { State = EdgeProvisioningState.Active, ExternalId = externalId };
    public static DomainProvisioningStatus Failed(string? detail, string? externalId = null) =>
        new() { State = EdgeProvisioningState.Failed, Detail = detail, ExternalId = externalId };
}

public class DomainProvisioningResult
{
    public bool Success { get; init; }
    public string? ExternalId { get; init; }
    public string? FailureReason { get; init; }

    public static DomainProvisioningResult Ok(string? externalId = null) => new() { Success = true, ExternalId = externalId };
    public static DomainProvisioningResult Failed(string reason) => new() { Success = false, FailureReason = reason };
}
