using Microsoft.AspNetCore.Mvc;
using Oluso.Core.Api;
using Oluso.Core.Authorization;
using Oluso.Core.Domain.Interfaces;
using Oluso.Core.Terminology;
using Oluso.Admin.Authorization;

namespace Oluso.Admin.Controllers;

/// <summary>
/// The Terminology Center backend — one
/// central, module-agnostic place for a tenant administrator to view every
/// overridable term across their licensed modules and rename any of them for
/// their tenant. Depends only on the Core <see cref="ITerminologyService"/>
/// facade, so it has no knowledge of any specific module.
///
/// <para>The tenant's industry/type (which pack is inherited) is a separate
/// central setting — see <see cref="TenantProfileController"/>.</para>
/// </summary>
[Route("api/admin/terminology")]
public class TerminologyController : AdminBaseController
{
    private readonly ITerminologyService _terminology;

    public TerminologyController(
        ITenantContext tenantContext,
        ITerminologyService terminology)
        : base(tenantContext)
    {
        _terminology = terminology;
    }

    // Runtime resolution (the flat merged map) is served by the single shared
    // platform endpoint — GET api/terminology — which every app's Transloco
    // loader consumes. This controller is authoring-only.

    /// <summary>The authoring grid for the tenant's licensed modules.</summary>
    [HttpGet("keys")]
    [RequirePermission(AdminPermissions.SettingsRead)]
    public async Task<ActionResult<IReadOnlyList<ResolvedTerminologyKey>>> GetKeys(
        [FromQuery] string? locale = null,
        CancellationToken cancellationToken = default)
    {
        var keys = await _terminology.GetAuthoringKeysAsync(
            TenantId, NormalizeLocale(locale), AdminUserId, cancellationToken);
        return Ok(keys);
    }

    /// <summary>This tenant's overrides for a locale.</summary>
    [HttpGet("overrides")]
    [RequirePermission(AdminPermissions.SettingsRead)]
    public async Task<ActionResult<IReadOnlyList<TerminologyOverrideEntry>>> GetOverrides(
        [FromQuery] string? locale = null,
        CancellationToken cancellationToken = default)
    {
        var overrides = await _terminology.GetOverridesAsync(TenantId, NormalizeLocale(locale), cancellationToken);
        return Ok(overrides);
    }

    /// <summary>Set (create/update) a tenant override for one key.</summary>
    [HttpPut("overrides")]
    [RequirePermission(AdminPermissions.SettingsWrite)]
    public async Task<ActionResult<TerminologyOverrideEntry>> UpsertOverride(
        [FromBody] UpsertTerminologyOverrideRequest request,
        CancellationToken cancellationToken = default)
    {
        var result = await _terminology.SetOverrideAsync(
            TenantId, NormalizeLocale(request.Locale), request.Key, request.Value, Actor(), cancellationToken);

        return result is null
            ? BadRequest(new { Message = $"Key '{request.Key}' is missing, unknown, or not overridable." })
            : Ok(result);
    }

    /// <summary>Reset a key to its inherited value.</summary>
    [HttpDelete("overrides/{key}")]
    [RequirePermission(AdminPermissions.SettingsWrite)]
    public async Task<IActionResult> DeleteOverride(
        string key,
        [FromQuery] string? locale = null,
        CancellationToken cancellationToken = default)
    {
        var removed = await _terminology.ResetOverrideAsync(
            TenantId, NormalizeLocale(locale), key, Actor(), cancellationToken);
        return removed ? NoContent() : NotFound();
    }

    private TerminologyActor Actor() => new(RequireAdminUserId, AdminUserName, ClientIp);

    private static string NormalizeLocale(string? locale) =>
        string.IsNullOrWhiteSpace(locale) ? "en-US" : locale.Trim();
}

/// <summary>Request body to set a terminology override.</summary>
public sealed class UpsertTerminologyOverrideRequest
{
    public required string Key { get; set; }
    public string? Locale { get; set; }
    public required string Value { get; set; }
}
