using Microsoft.AspNetCore.Mvc;
using Oluso.Core.Api;
using Oluso.Core.Authorization;
using Oluso.Core.Domain.Interfaces;
using Oluso.Core.Events;
using Oluso.Core.Terminology;
using Oluso.Admin.Authorization;

namespace Oluso.Admin.Controllers;

/// <summary>
/// The tenant's industry/type — a central platform setting that drives which
/// terminology pack a tenant inherits. Lives in core admin (not in any module)
/// and is backed by the Core <see cref="ITenantProfileService"/>, so the
/// setting has a single, leak-free home. Changing it is a deliberate settings
/// action (separate from renaming individual labels in the Terminology Center).
/// </summary>
[Route("api/admin/tenant-profile")]
public class TenantProfileController : AdminBaseController
{
    private readonly ITenantProfileService _profile;
    private readonly IOlusoEventService _events;

    public TenantProfileController(
        ITenantContext tenantContext,
        ITenantProfileService profile,
        IOlusoEventService events)
        : base(tenantContext)
    {
        _profile = profile;
        _events = events;
    }

    /// <summary>The tenant's current industry/type and the available choices.</summary>
    [HttpGet]
    [RequirePermission(AdminPermissions.SettingsRead)]
    public async Task<ActionResult<TenantTerminologyProfile>> Get(CancellationToken cancellationToken = default)
    {
        var profile = await _profile.GetAsync(TenantId, cancellationToken);
        return Ok(profile);
    }

    /// <summary>Set the tenant's industry/type.</summary>
    [HttpPut]
    [RequirePermission(AdminPermissions.SettingsWrite)]
    public async Task<IActionResult> Set(
        [FromBody] SetTenantIndustryRequest request,
        CancellationToken cancellationToken = default)
    {
        await _profile.SetIndustryAsync(TenantId, request.Industry, cancellationToken);

        // An industry change swaps the inherited pack for every key, so raise
        // the terminology-updated event with no specific key — subscribers
        // (including the realtime sink) treat that as "re-resolve everything".
        await _events.RaiseAsync(new TerminologyUpdatedEvent
        {
            TenantId = TenantId,
            AdminUserId = AdminUserId,
            AdminUserName = AdminUserName
        }, cancellationToken);

        return NoContent();
    }
}

/// <summary>Request body to set the tenant's industry/type.</summary>
public sealed class SetTenantIndustryRequest
{
    public string? Industry { get; set; }
}
