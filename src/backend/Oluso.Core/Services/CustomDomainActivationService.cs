using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using Oluso.Core.Domain.Entities;
using Oluso.Core.Domain.Interfaces;

namespace Oluso.Core.Services;

/// <summary>
/// Drives a verified custom domain to <see cref="CustomDomainStatus.Active"/>.
/// <para>
/// Edge provisioning is a two-step affair: submitting the hostname returns
/// immediately, but the DV certificate issues asynchronously. Activation
/// therefore has to be re-entrant — the manual "Verify now" button and the
/// background sweep both call <see cref="TryActivateAsync"/> repeatedly until
/// the provider reports the certificate live.
/// </para>
/// </summary>
public interface ICustomDomainActivationService
{
    /// <summary>
    /// Submits the domain to the edge provider if it hasn't been submitted
    /// yet, then checks whether the edge has finished. Mutates
    /// <paramref name="domain"/> in place; the caller persists it.
    /// </summary>
    Task<CustomDomainActivationOutcome> TryActivateAsync(
        CustomDomainVerification domain,
        Tenant? tenant,
        CancellationToken ct = default);
}

public enum CustomDomainActivationOutcome
{
    /// <summary>Certificate live, status flipped to Active, tenant.CustomDomain set.</summary>
    Activated = 0,

    /// <summary>Submitted but the edge hasn't finished — retry later.</summary>
    AwaitingEdge = 1,

    /// <summary>Edge rejected the hostname, or no provisioner is registered.</summary>
    Blocked = 2
}

public class CustomDomainActivationService : ICustomDomainActivationService
{
    private readonly ITenantStore _tenantStore;
    private readonly ICustomDomainProvisioningService? _provisioning;
    private readonly IHostValidationCacheInvalidator? _cacheInvalidator;
    private readonly string? _cnameTarget;
    private readonly ILogger<CustomDomainActivationService> _logger;

    public CustomDomainActivationService(
        ITenantStore tenantStore,
        IConfiguration configuration,
        ILogger<CustomDomainActivationService> logger,
        ICustomDomainProvisioningService? provisioning = null,
        IHostValidationCacheInvalidator? cacheInvalidator = null)
    {
        _tenantStore = tenantStore;
        _provisioning = provisioning;
        _cacheInvalidator = cacheInvalidator;
        _logger = logger;

        _cnameTarget = configuration["Oluso:CustomDomains:CnameTarget"]
            ?? (Uri.TryCreate(configuration["Oluso:IssuerUri"], UriKind.Absolute, out var issuer) ? issuer.Host : null);
    }

    public async Task<CustomDomainActivationOutcome> TryActivateAsync(
        CustomDomainVerification domain,
        Tenant? tenant,
        CancellationToken ct = default)
    {
        if (_provisioning == null)
        {
            domain.FailureReason =
                "No edge provisioner is registered, so the domain cannot be activated. " +
                "Register an ICustomDomainProvisioningService (e.g. AddCloudflareCustomHostnames()).";
            _logger.LogWarning(
                "Domain {Domain} (tenant {TenantId}) cannot be activated — no edge provisioner registered",
                domain.Domain, domain.TenantId);
            return CustomDomainActivationOutcome.Blocked;
        }

        // Submit only once. On every later pass the hostname already exists,
        // and re-posting it would churn the provider for no benefit.
        if (string.IsNullOrEmpty(domain.ExternalProvisioningId))
        {
            var submitted = await _provisioning.ProvisionAsync(domain.Domain, domain.TenantId, ct);
            if (!submitted.Success)
            {
                domain.FailureReason = $"Edge provisioning failed: {submitted.FailureReason}";
                _logger.LogWarning("Edge provisioning failed for {Domain}: {Reason}",
                    domain.Domain, submitted.FailureReason);
                return CustomDomainActivationOutcome.Blocked;
            }

            domain.ExternalProvisioningId = submitted.ExternalId;
        }

        var status = await _provisioning.GetStatusAsync(domain.Domain, ct);
        if (!string.IsNullOrEmpty(status.ExternalId))
        {
            domain.ExternalProvisioningId = status.ExternalId;
        }

        switch (status.State)
        {
            case EdgeProvisioningState.Active:
                break;

            case EdgeProvisioningState.Failed:
                domain.FailureReason = $"Edge validation failed: {status.Detail}";
                _logger.LogWarning("Edge validation failed for {Domain}: {Detail}", domain.Domain, status.Detail);
                return CustomDomainActivationOutcome.Blocked;

            case EdgeProvisioningState.NotFound:
                // Submitted previously but the provider lost it — clear the id
                // so the next pass resubmits rather than polling forever.
                domain.ExternalProvisioningId = null;
                domain.FailureReason = "Edge provider has no record of this hostname; it will be resubmitted.";
                return CustomDomainActivationOutcome.AwaitingEdge;

            default:
                domain.FailureReason =
                    $"Waiting for edge certificate issuance ({status.Detail}). " +
                    $"Point {domain.Domain} at {_cnameTarget ?? "the platform host"} with a CNAME if you haven't already.";
                return CustomDomainActivationOutcome.AwaitingEdge;
        }

        domain.FailureReason = null;
        domain.Status = CustomDomainStatus.Active;
        domain.ActivatedAt = DateTime.UtcNow;
        domain.LastRevalidatedAt = DateTime.UtcNow;

        tenant ??= await _tenantStore.GetByIdAsync(domain.TenantId);
        if (tenant != null)
        {
            tenant.CustomDomain = domain.Domain;
            tenant.Updated = DateTime.UtcNow;
            await _tenantStore.UpdateAsync(tenant);

            if (_cacheInvalidator != null)
                await _cacheInvalidator.InvalidateCacheAsync(ct);
        }

        _logger.LogInformation("Custom domain {Domain} activated for tenant {TenantId}",
            domain.Domain, domain.TenantId);

        return CustomDomainActivationOutcome.Activated;
    }
}
