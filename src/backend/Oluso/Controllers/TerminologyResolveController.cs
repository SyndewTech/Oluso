using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Oluso.Core.Terminology;

namespace Oluso.Controllers;

/// <summary>
/// The single runtime terminology endpoint. Returns the flat, fully-merged
/// <c>key → label</c> map (generic default ◁ industry pack ◁ tenant override)
/// for the current tenant + locale — exactly what the Transloco loader in
/// every app (workspace, admin, account) consumes.
///
/// <para>Authenticated users only; labels are not sensitive and every module's
/// UI needs them, so there is deliberately <b>no</b> module feature gate here:
/// a tenant with one module gets its labels the same as a tenant with every module.
/// (Authoring — keys/overrides — stays behind the admin Terminology Center
/// endpoints and their settings permissions.)</para>
/// </summary>
[ApiController]
[Route("api/terminology")]
[Authorize]
public class TerminologyResolveController : ControllerBase
{
    private readonly ITerminologyService _terminology;

    public TerminologyResolveController(ITerminologyService terminology)
    {
        _terminology = terminology;
    }

    private string? TenantId => User.FindFirst("tenant_id")?.Value;

    /// <summary>The resolved label map for the current tenant + locale.</summary>
    [HttpGet]
    public async Task<ActionResult<IReadOnlyDictionary<string, string>>> Get(
        [FromQuery] string? locale = null,
        CancellationToken cancellationToken = default)
    {
        var tenantId = TenantId;
        if (string.IsNullOrEmpty(tenantId))
            return BadRequest(new { Message = "No tenant context." });

        var normalized = string.IsNullOrWhiteSpace(locale) ? "en-US" : locale.Trim();
        var map = await _terminology.ResolveAsync(tenantId, normalized, cancellationToken);
        return Ok(map);
    }
}
