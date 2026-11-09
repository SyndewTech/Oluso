using System.Security.Cryptography;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Logging;
using Oluso.Admin.Authorization;
using Oluso.Admin.Dto.Domains;
using Oluso.Core;
using Oluso.Core.Api;
using Oluso.Core.Authorization;
using Oluso.Core.Domain.Entities;
using Oluso.Core.Domain.Interfaces;
using Oluso.Core.Services;

namespace Oluso.Admin.Controllers;

/// <summary>
/// Email domains of the tenant in context (api/admin/email-domains), or of a tenant named in the route
/// (api/admin/tenants/{tenantId}/email-domains) for admins managing it from Organizations. A tenant proves it
/// controls a domain with a TXT record; addresses there can then be trusted as the tenant's own.
/// </summary>
[Route("api/admin/email-domains")]
[Route("api/admin/tenants/{tenantId}/email-domains")]
public class TenantEmailDomainsController : AdminBaseController
{
    private readonly ITenantContext _tenantContext;
    private readonly ITenantEmailDomainStore _store;
    private readonly ITenantStore _tenantStore;
    private readonly IDnsVerificationService _dns;
    private readonly IAuthorizationService _authorization;
    private readonly ILogger<TenantEmailDomainsController> _logger;

    public TenantEmailDomainsController(
        ITenantContext tenantContext,
        ITenantEmailDomainStore store,
        ITenantStore tenantStore,
        IDnsVerificationService dns,
        IAuthorizationService authorization,
        ILogger<TenantEmailDomainsController> logger) : base(tenantContext)
    {
        _tenantContext = tenantContext;
        _store = store;
        _tenantStore = tenantStore;
        _dns = dns;
        _authorization = authorization;
        _logger = logger;
    }

    /// <summary>
    /// The tenant an action works on, with the permission that applies to how it was reached.
    /// </summary>
    private async Task<(string? TenantId, ActionResult? Error)> ResolveTenantAsync(bool write, CancellationToken cancellationToken)
    {
        var routeTenantId = RouteData.Values["tenantId"] as string;
        if (!string.IsNullOrEmpty(routeTenantId))
        {
            if (!await HasPermissionAsync(write ? AdminPermissions.TenantsManageSettings : AdminPermissions.TenantsRead))
                return (null, Forbid());
            var tenant = await _tenantStore.GetByIdAsync(routeTenantId, cancellationToken);
            if (tenant == null)
                return (null, NotFound());
            if (!CanAccessTenant(tenant))
                return (null, Forbid());
            return (tenant.Id, null);
        }

        if (!await HasPermissionAsync(write ? AdminPermissions.SettingsWrite : AdminPermissions.SettingsRead))
            return (null, Forbid());
        if (!_tenantContext.HasTenant || string.IsNullOrEmpty(_tenantContext.TenantId)
            || _tenantContext.TenantId == PlatformConstants.TenantId)
            return (null, BadRequest(new { error = "no_tenant", error_description = "Email domains belong to a tenant; choose one first." }));
        return (_tenantContext.TenantId, null);
    }

    private async Task<bool> HasPermissionAsync(string permission)
    {
        var result = await _authorization.AuthorizeAsync(User, null, PermissionPolicyConstants.PolicyPrefix + permission);
        return result.Succeeded;
    }

    [HttpGet]
    [RequireAnyPermission(AdminPermissions.SettingsRead, AdminPermissions.TenantsRead)]
    public async Task<ActionResult<IEnumerable<EmailDomainDto>>> List(CancellationToken cancellationToken)
    {
        var (tenantId, denied) = await ResolveTenantAsync(write: false, cancellationToken);
        if (denied != null) return denied;

        var domains = await _store.GetByTenantIdAsync(tenantId!, cancellationToken);
        return Ok(domains.Select(MapToDto));
    }

    /// <summary>
    /// Claims a domain and returns the TXT record to publish. Claiming again a domain the tenant removed or that
    /// failed starts it over with a new record.
    /// </summary>
    [HttpPost]
    [RequireAnyPermission(AdminPermissions.SettingsWrite, AdminPermissions.TenantsManageSettings)]
    public async Task<ActionResult<EmailDomainDto>> Add([FromBody] AddEmailDomainRequest request, CancellationToken cancellationToken)
    {
        var (tenantId, denied) = await ResolveTenantAsync(write: true, cancellationToken);
        if (denied != null) return denied;

        var domain = NormalizeDomain(request.Domain);
        if (domain == null)
        {
            return BadRequest(new { error = "invalid_domain", error_description = "Enter a domain name such as example.com." });
        }

        var holder = await _store.GetVerifiedByDomainAsync(domain, cancellationToken);
        if (holder != null && holder.TenantId != tenantId)
        {
            return Conflict(new { error = "domain_taken", error_description = TenantEmailDomainVerification.HeldByAnotherTenantReason });
        }

        var token = Convert.ToHexString(RandomNumberGenerator.GetBytes(16)).ToLowerInvariant();
        var existing = await _store.GetByTenantAndDomainAsync(tenantId!, domain, cancellationToken);
        if (existing != null)
        {
            if (existing.Status is TenantEmailDomainStatus.Pending or TenantEmailDomainStatus.Verified)
            {
                return Conflict(new { error = "domain_exists", error_description = "This domain is already listed for this organization." });
            }

            existing.Status = TenantEmailDomainStatus.Pending;
            existing.VerificationToken = token;
            existing.TxtRecordValue = TenantEmailDomain.TxtValuePrefix + token;
            existing.CreatedAt = DateTime.UtcNow;
            existing.VerifiedAt = null;
            existing.LastCheckedAt = null;
            existing.LastRevalidatedAt = null;
            existing.GracePeriodStartedAt = null;
            existing.CheckAttempts = 0;
            existing.FailureReason = null;
            await _store.UpdateAsync(existing, cancellationToken);
            _logger.LogInformation("Email domain {Domain} claimed again by tenant {TenantId}", domain, tenantId);
            return Ok(MapToDto(existing));
        }

        var created = await _store.CreateAsync(new TenantEmailDomain
        {
            TenantId = tenantId!,
            Domain = domain,
            Status = TenantEmailDomainStatus.Pending,
            VerificationToken = token,
            TxtRecordName = TenantEmailDomain.TxtRecordPrefix + domain,
            TxtRecordValue = TenantEmailDomain.TxtValuePrefix + token
        }, cancellationToken);

        _logger.LogInformation("Email domain {Domain} claimed by tenant {TenantId}", domain, tenantId);
        return Ok(MapToDto(created));
    }

    /// <summary>Checks the TXT record now instead of waiting for the background check.</summary>
    [HttpPost("{id}/verify")]
    [RequireAnyPermission(AdminPermissions.SettingsWrite, AdminPermissions.TenantsManageSettings)]
    public async Task<ActionResult<EmailDomainDto>> Verify(string id, CancellationToken cancellationToken)
    {
        var (tenantId, denied) = await ResolveTenantAsync(write: true, cancellationToken);
        if (denied != null) return denied;

        var claim = await _store.GetByIdAsync(id, cancellationToken);
        if (claim == null || claim.TenantId != tenantId || claim.Status == TenantEmailDomainStatus.Removed)
        {
            return NotFound();
        }

        if (claim.Status == TenantEmailDomainStatus.Verified)
        {
            return Ok(MapToDto(claim));
        }

        // A failed claim is checked again on request; its record hasn't changed.
        claim.Status = TenantEmailDomainStatus.Pending;
        await TenantEmailDomainVerification.CheckAsync(claim, _store, _dns, cancellationToken);
        await _store.UpdateAsync(claim, cancellationToken);

        if (claim.Status == TenantEmailDomainStatus.Verified)
        {
            _logger.LogInformation("Email domain {Domain} verified for tenant {TenantId}", claim.Domain, tenantId);
        }
        return Ok(MapToDto(claim));
    }

    /// <summary>
    /// Removes the domain. Addresses already marked verified stay verified; new ones there are no longer trusted
    /// without a code to the inbox.
    /// </summary>
    [HttpDelete("{id}")]
    [RequireAnyPermission(AdminPermissions.SettingsWrite, AdminPermissions.TenantsManageSettings)]
    public async Task<IActionResult> Remove(string id, CancellationToken cancellationToken)
    {
        var (tenantId, denied) = await ResolveTenantAsync(write: true, cancellationToken);
        if (denied != null) return denied;

        var claim = await _store.GetByIdAsync(id, cancellationToken);
        if (claim == null || claim.TenantId != tenantId || claim.Status == TenantEmailDomainStatus.Removed)
        {
            return NotFound();
        }

        claim.Status = TenantEmailDomainStatus.Removed;
        await _store.UpdateAsync(claim, cancellationToken);
        _logger.LogInformation("Email domain {Domain} removed from tenant {TenantId}", claim.Domain, tenantId);
        return NoContent();
    }

    /// <summary>A lower-case host name of at least two labels, or null.</summary>
    private static string? NormalizeDomain(string? input)
    {
        var domain = input?.Trim().TrimEnd('.').ToLowerInvariant();
        if (string.IsNullOrEmpty(domain) || domain.Length > 253 || !domain.Contains('.'))
        {
            return null;
        }

        if (domain.Contains('@') || Uri.CheckHostName(domain) != UriHostNameType.Dns)
        {
            return null;
        }

        return domain.Split('.').All(label => label.Length is > 0 and <= 63) ? domain : null;
    }

    private static EmailDomainDto MapToDto(TenantEmailDomain d) => new(
        d.Id, d.TenantId, d.Domain, d.Status.ToString(),
        d.TxtRecordName, d.TxtRecordValue,
        d.CreatedAt, d.VerifiedAt, d.LastCheckedAt, d.LastRevalidatedAt,
        d.CheckAttempts, d.FailureReason);
}
