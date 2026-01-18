using System.IdentityModel.Tokens.Jwt;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Logging;
using Oluso.Core.Domain.Interfaces;
using Oluso.Core.Protocols.Models;
using Oluso.Core.Services;

namespace Oluso.Protocols.Oidc;

/// <summary>
/// OpenID Connect UserInfo Endpoint.
/// Route is configured via OidcEndpointRouteConvention.
/// </summary>

public class OidcUserInfoController : ControllerBase
{
    private readonly IProfileService _profileService;
    private readonly ITenantContext _tenantContext;
    private readonly ILogger<OidcUserInfoController> _logger;

    public OidcUserInfoController(
        IProfileService profileService,
        ITenantContext tenantContext,
        ILogger<OidcUserInfoController> logger)
    {
        _profileService = profileService;
        _tenantContext = tenantContext;
        _logger = logger;
    }

    [HttpGet]
    [HttpPost]
    [Authorize(AuthenticationSchemes = OidcConstants.AccessTokenAuthenticationScheme)]
    public async Task<IActionResult> GetUserInfo(CancellationToken cancellationToken)
    {
        // Get subject from token
        var subjectClaim = User.FindFirst(JwtRegisteredClaimNames.Sub)
            ?? User.FindFirst("sub");

        if (subjectClaim == null)
        {
            return Unauthorized(new TokenErrorResponse
            {
                Error = OidcConstants.Errors.InvalidToken,
                ErrorDescription = "Token does not contain a subject claim"
            });
        }

        // Get scopes from token
        var scopeClaims = User.FindAll("scope").Select(c => c.Value).ToList();
        if (!scopeClaims.Any())
        {
            var scopeClaim = User.FindFirst("scope");
            if (scopeClaim != null)
            {
                scopeClaims = scopeClaim.Value.Split(' ').ToList();
            }
        }

        // Use profile service to get claims with OIDC protocol context
        var claims = await _profileService.GetProfileClaimsAsync(
            subjectClaim.Value,
            scopeClaims,
            "oidc",
            cancellationToken);

        // Ensure subject is always included
        if (!claims.ContainsKey(OidcConstants.StandardClaims.Subject))
        {
            claims[OidcConstants.StandardClaims.Subject] = subjectClaim.Value;
        }

        // Include permissions if the "permissions" scope was granted in the token
        if (scopeClaims.Contains(OidcConstants.Scopes.Permissions))
        {
            var tenantId = _tenantContext.HasTenant ? _tenantContext.TenantId : null;
            var permissions = await _profileService.GetUserPermissionsAsync(
                subjectClaim.Value, tenantId, cancellationToken);
            // Ensure distinct (case-insensitive)
            claims["permissions"] = permissions.Distinct(StringComparer.OrdinalIgnoreCase).ToList();
        }

        return Ok(claims);
    }
}
