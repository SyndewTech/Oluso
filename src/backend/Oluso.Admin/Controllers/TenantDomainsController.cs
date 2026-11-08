using System.Security.Cryptography;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
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

        // Check if domain is already claimed
        var existing = await _store.GetByDomainAsync(domain, ct);
        if (existing != null && existing.Status != CustomDomainStatus.Failed && existing.Status != CustomDomainStatus.Removed)
        {
            return Conflict(existing.TenantId == tenantId
                ? "This domain already has a pending or active verification for this tenant"
                : "This domain is already claimed by another tenant");
        }

        var token = Convert.ToHexString(RandomNumberGenerator.GetBytes(16)).ToLowerInvariant();

        var verification = new CustomDomainVerification
        {
            TenantId = tenantId,
            Domain = domain,
            Status = CustomDomainStatus.Pending,
            VerificationToken = token,
            TxtRecordName = $"_oluso-verify.{domain}",
            TxtRecordValue = $"oluso-verify={token}"
        };

        var created = await _store.CreateAsync(verification, ct);
        _logger.LogInformation("Domain verification initiated for {Domain} on tenant {TenantId}", domain, tenantId);

        return CreatedAtAction(nameof(GetDomain), new { tenantId, id = created.Id }, MapToDto(created));
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