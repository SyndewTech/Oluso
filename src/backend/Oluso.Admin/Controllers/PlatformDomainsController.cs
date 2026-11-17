using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Oluso.Admin.Authorization;
using Oluso.Admin.Dto.Domains;
using Oluso.Core.Api;
using Oluso.Core.Authorization;
using Oluso.Core.Domain.Interfaces;

namespace Oluso.Admin.Controllers;

/// <summary>
/// Every tenant's custom domains in one list, removed and failed records included, so the platform can see which
/// tenant holds a domain and why it isn't serving. Verifying, removing and restoring a domain go through the tenant's
/// own domain endpoints, which this list links to by tenant id.
/// </summary>
[Route("api/admin/platform/domains")]
[Authorize(Policy = "SuperAdmin")]
public class PlatformDomainsController : AdminBaseController
{
    private readonly ICustomDomainVerificationStore _store;

    public PlatformDomainsController(ITenantContext tenantContext, ICustomDomainVerificationStore store)
        : base(tenantContext)
    {
        _store = store;
    }

    [HttpGet]
    [RequirePermission(AdminPermissions.PlatformDomainsRead)]
    public async Task<ActionResult<IEnumerable<PlatformDomainDto>>> GetAll(CancellationToken cancellationToken)
    {
        var domains = await _store.GetAllAsync(cancellationToken);
        return Ok(domains.Select(d => new PlatformDomainDto(
            d.Id, d.Domain, d.Status.ToString(),
            d.TenantId, d.Tenant?.DisplayName ?? d.Tenant?.Name, d.Tenant?.Identifier,
            d.Tenant != null && string.Equals(d.Tenant.CustomDomain, d.Domain, StringComparison.OrdinalIgnoreCase),
            d.ExternalProvisioningId,
            d.CreatedAt, d.VerifiedAt, d.ActivatedAt,
            d.LastCheckedAt, d.LastRevalidatedAt, d.GracePeriodStartedAt,
            d.CheckAttempts, d.FailureReason)));
    }
}
