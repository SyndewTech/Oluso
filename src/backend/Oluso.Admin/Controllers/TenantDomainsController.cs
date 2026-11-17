using System.Security.Cryptography;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using Oluso.Core.Api;
using Oluso.Core.Domain.Entities;
using Oluso.Core.Authorization;
using Oluso.Core.Domain.Interfaces;
using Oluso.Core.Services;
using Oluso.Admin.Authorization;
using Oluso.Admin.Dto.Domains;

namespace Oluso.Admin.Controllers;

/// <summary>
/// Manages custom domain verification for tenants.
/// Separate from the main TenantsController — custom domains have their own lifecycle.
/// </summary>
[Authorize(Policy = "AdminApi")]
[ApiController]
[Route("api/admin/tenants/{tenantId}/domains")]
[ApiExplorerSettings(GroupName = "admin")]
public class TenantDomainsController : ControllerBase
{
    private readonly ICustomDomainVerificationStore _store;
    private readonly ITenantStore _tenantStore;
    private readonly IDnsVerificationService _dnsService;
    private readonly ICustomDomainActivationService _activationService;
    private readonly IHostValidationCacheInvalidator? _cacheInvalidator;
    private readonly ICustomDomainProvisioningService? _provisioningService;
    private readonly string? _cnameTarget;
    private readonly ILogger<TenantDomainsController> _logger;

    public TenantDomainsController(
        ICustomDomainVerificationStore store,
        ITenantStore tenantStore,
        IDnsVerificationService dnsService,
        ICustomDomainActivationService activationService,
        IConfiguration configuration,
        ILogger<TenantDomainsController> logger,
        IHostValidationCacheInvalidator? cacheInvalidator = null,
        ICustomDomainProvisioningService? provisioningService = null)
    {
        _store = store;
        _tenantStore = tenantStore;
        _dnsService = dnsService;
        _activationService = activationService;
        _cacheInvalidator = cacheInvalidator;
        _provisioningService = provisioningService;
        _logger = logger;

        // Hostname tenants must CNAME their custom domain at — typically
        // the SaaS apex inside the same Cloudflare zone (e.g.
        // "platform.example.com"). Surfaced to the admin UI so the
        // tenant can copy it into their DNS provider; the TXT verification
        // alone doesn't get traffic to land on Cloudflare's edge.
        // Defaults to the IdP IssuerUri host when not set explicitly —
        // for the standard "single-zone with custom hostnames pointing at
        // the IdP" deployment, that's the right value with zero extra
        // config. Set CnameTarget explicitly only if you front the IdP
        // behind a different hostname (e.g. dedicated SaaS apex like
        // "customers.example.com").
        _cnameTarget = configuration["Oluso:CustomDomains:CnameTarget"]
            ?? DeriveCnameTargetFromIssuer(configuration);
    }

    private static string? DeriveCnameTargetFromIssuer(IConfiguration configuration)
    {
        var issuerUri = configuration["Oluso:IssuerUri"];
        return Uri.TryCreate(issuerUri, UriKind.Absolute, out var uri) ? uri.Host : null;
    }

    /// <summary>
    /// List all domain verifications for a tenant.
    /// </summary>
    [HttpGet]
    [RequirePermission(AdminPermissions.TenantsRead)]
    public async Task<IActionResult> GetDomains(string tenantId, CancellationToken ct)
    {
        var domains = await _store.GetByTenantIdAsync(tenantId, ct);
        return Ok(domains.Select(MapToDto));
    }

    /// <summary>
    /// Get a specific domain verification by ID.
    /// </summary>
    [HttpGet("{id}")]
    [RequirePermission(AdminPermissions.TenantsRead)]
    public async Task<IActionResult> GetDomain(string tenantId, string id, CancellationToken ct)
    {
        var domain = await _store.GetByIdAsync(id, ct);
        if (domain == null || domain.TenantId != tenantId) return NotFound();
        return Ok(MapToDto(domain));
    }

    /// <summary>
    /// Initiate a custom domain verification.
    /// Returns the TXT record the tenant admin must create in DNS.
    /// </summary>
    [HttpPost]
    [RequirePermission(AdminPermissions.TenantsManageSettings)]
    public async Task<IActionResult> AddDomain(string tenantId, [FromBody] AddDomainRequest request, CancellationToken ct)
    {
        // Validate tenant exists
        var tenant = await _tenantStore.GetByIdAsync(tenantId);
        if (tenant == null) return NotFound("Tenant not found");

        // Validate domain format
        if (string.IsNullOrWhiteSpace(request.Domain) || !request.Domain.Contains('.'))
            return BadRequest("Invalid domain format");

        var domain = request.Domain.Trim().ToLowerInvariant();

        // A domain has one record across all tenants (unique in the database). One that is live answers as in use;
        // one that was removed or failed verification is reopened for this tenant rather than added a second time.
        var existing = await _store.GetByDomainAsync(domain, ct);
        if (existing != null)
        {
            if (existing.Status is not (CustomDomainStatus.Removed or CustomDomainStatus.Failed))
            {
                return Conflict(InUse(existing, tenantId));
            }

            Reopen(existing, tenantId);
            await _store.UpdateAsync(existing, ct);
            _logger.LogInformation("Domain verification reopened for {Domain} on tenant {TenantId}", domain, tenantId);
            return CreatedAtAction(nameof(GetDomain), new { tenantId, id = existing.Id }, MapToDto(existing));
        }

        var token = NewToken();
        var verification = new CustomDomainVerification
        {
            TenantId = tenantId,
            Domain = domain,
            Status = CustomDomainStatus.Pending,
            VerificationToken = token,
            TxtRecordName = $"_oluso-verify.{domain}",
            TxtRecordValue = $"oluso-verify={token}"
        };

        try
        {
            var created = await _store.CreateAsync(verification, ct);
            _logger.LogInformation("Domain verification initiated for {Domain} on tenant {TenantId}", domain, tenantId);
            return CreatedAtAction(nameof(GetDomain), new { tenantId, id = created.Id }, MapToDto(created));
        }
        catch (DbUpdateException ex)
        {
            // Added by someone else between the check and the insert; any other failure is not a conflict.
            var added = await _store.GetByDomainAsync(domain, ct);
            if (added == null) throw;
            _logger.LogInformation(ex, "Domain {Domain} was added concurrently for tenant {TenantId}", domain, added.TenantId);
            return Conflict(InUse(added, tenantId));
        }
    }

    private static string InUse(CustomDomainVerification existing, string tenantId) =>
        existing.TenantId == tenantId
            ? "This domain has already been added to this organization."
            : "This domain is in use by another organization.";

    private static string NewToken() => Convert.ToHexString(RandomNumberGenerator.GetBytes(16)).ToLowerInvariant();

    /// <summary>
    /// Starts verification of a removed or failed record over, for <paramref name="tenantId"/>. The same tenant keeps
    /// its token, so a TXT record it already created still matches; another tenant gets a new token. The edge hostname
    /// is the domain's, whichever tenant holds it, so its id stays: activation checks it with the edge provider and
    /// resubmits the hostname when the provider no longer has it.
    /// </summary>
    private static void Reopen(CustomDomainVerification domain, string tenantId)
    {
        if (domain.TenantId != tenantId)
        {
            domain.TenantId = tenantId;
            domain.Tenant = null;
            var token = NewToken();
            domain.VerificationToken = token;
            domain.TxtRecordValue = $"oluso-verify={token}";
        }

        domain.Status = CustomDomainStatus.Pending;
        domain.CreatedAt = DateTime.UtcNow;
        domain.CheckAttempts = 0;
        domain.FailureReason = null;
        domain.VerifiedAt = null;
        domain.ActivatedAt = null;
        domain.LastCheckedAt = null;
        domain.LastRevalidatedAt = null;
        domain.GracePeriodStartedAt = null;
    }

    /// <summary>
    /// Trigger a manual verification check for a pending domain.
    /// </summary>
    [HttpPost("{id}/verify")]
    [RequirePermission(AdminPermissions.TenantsManageSettings)]
    public async Task<IActionResult> VerifyDomain(string tenantId, string id, CancellationToken ct)
    {
        var domain = await _store.GetByIdAsync(id, ct);
        if (domain == null || domain.TenantId != tenantId) return NotFound();
        if (domain.Status == CustomDomainStatus.Active) return Ok(MapToDto(domain));
        if (domain.Status == CustomDomainStatus.Removed) return BadRequest("Domain has been removed");

        // Retrying a domain whose verification timed out starts it over, so the background check picks it up again.
        if (domain.Status == CustomDomainStatus.Failed)
        {
            Reopen(domain, tenantId);
        }

        domain.LastCheckedAt = DateTime.UtcNow;
        domain.CheckAttempts++;

        // Ownership is proven once. Re-running the TXT lookup on an already
        // verified domain just delays the edge poll that's actually holding
        // activation up, so skip straight to it.
        if (domain.Status != CustomDomainStatus.Verified)
        {
            var result = await _dnsService.VerifyDomainAsync(domain, ct);
            if (!result.Success)
            {
                domain.FailureReason = result.FailureReason;
                await _store.UpdateAsync(domain, ct);
                return Ok(MapToDto(domain));
            }

            domain.Status = CustomDomainStatus.Verified;
            domain.VerifiedAt = DateTime.UtcNow;
            domain.FailureReason = null;
        }

        var tenant = await _tenantStore.GetByIdAsync(domain.TenantId);
        await _activationService.TryActivateAsync(domain, tenant, ct);

        await _store.UpdateAsync(domain, ct);
        return Ok(MapToDto(domain));
    }

    /// <summary>
    /// Remove a custom domain from a tenant.
    /// </summary>
    [HttpDelete("{id}")]
    [RequirePermission(AdminPermissions.TenantsManageSettings)]
    public async Task<IActionResult> RemoveDomain(string tenantId, string id, CancellationToken ct)
    {
        var domain = await _store.GetByIdAsync(id, ct);
        if (domain == null || domain.TenantId != tenantId) return NotFound();

        // If this was the active domain, clear it from the tenant and deprovision from edge
        if (domain.Status == CustomDomainStatus.Active)
        {
            if (_provisioningService != null)
            {
                await _provisioningService.DeprovisionAsync(domain.Domain, ct);
            }

            var tenant = await _tenantStore.GetByIdAsync(tenantId);
            if (tenant != null && tenant.CustomDomain == domain.Domain)
            {
                tenant.CustomDomain = null;
                tenant.Updated = DateTime.UtcNow;
                await _tenantStore.UpdateAsync(tenant);
                if (_cacheInvalidator != null) await _cacheInvalidator.InvalidateCacheAsync(ct);
                _logger.LogInformation("Custom domain {Domain} removed from tenant {TenantId}", domain.Domain, tenantId);
            }
        }

        domain.Status = CustomDomainStatus.Removed;
        await _store.UpdateAsync(domain, ct);
        return NoContent();
    }

    private DomainVerificationDto MapToDto(CustomDomainVerification d) => new(
        d.Id, d.TenantId, d.Domain, d.Status.ToString(),
        d.TxtRecordName, d.TxtRecordValue,
        _cnameTarget,
        d.ExternalProvisioningId,
        d.CreatedAt, d.VerifiedAt, d.ActivatedAt,
        d.LastCheckedAt, d.CheckAttempts, d.FailureReason);
}