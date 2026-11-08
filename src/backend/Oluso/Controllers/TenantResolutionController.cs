using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Oluso.Core.Services;

namespace Oluso.Controllers;

/// <summary>
/// Anonymous endpoint for resolving a tenant from a hostname or identifier.
/// Used by host portals (admin/workspace/account) to determine tenant context
/// — and the right OIDC issuer — before kicking off an OIDC authorize call.
/// </summary>
[ApiController]
[Route("api/tenant")]
[AllowAnonymous]
public class TenantResolutionController : ControllerBase
{
    private readonly ITenantDiscoveryService _discovery;

    public TenantResolutionController(ITenantDiscoveryService discovery)
    {
        _discovery = discovery;
    }

    /// <summary>
    /// Resolve a tenant from a hostname (subdomain or custom domain) or its
    /// identifier. Returns the canonical tenant info plus the OIDC issuer URI
    /// the host app should authorize against, and the tenant's default IdP
    /// scheme (if any) for use as <c>domain_hint</c>.
    /// </summary>
    [HttpGet("resolve")]
    public async Task<IActionResult> Resolve(
        [FromQuery] string? host,
        [FromQuery] string? identifier,
        CancellationToken cancellationToken)
    {
        if (!_discovery.IsAvailable)
            return NotFound(new { error = "Multi-tenancy is not enabled" });

        if (string.IsNullOrWhiteSpace(host) && string.IsNullOrWhiteSpace(identifier))
            return BadRequest(new { error = "host or identifier parameter is required" });

        var discovered = !string.IsNullOrWhiteSpace(identifier)
            ? await _discovery.FindByIdentifierAsync(identifier, cancellationToken)
            : await _discovery.FindByHostAsync(host!, cancellationToken);

        if (discovered is null)
            return NotFound(new { error = "Tenant not found", host, identifier });

        var tenant = discovered.Tenant;
        return Ok(new
        {
            tenantId = tenant.Id,
            identifier = tenant.Identifier,
            name = tenant.Name,
            environment = tenant.Environment.ToString(),
            issuerUri = discovered.IssuerUri,
            defaultIdpScheme = tenant.DefaultIdpScheme,
            defaultSignInPolicyId = tenant.DefaultSignInPolicyId,
            defaultSsoUiMode = tenant.DefaultSsoUiMode,
        });
    }
}
