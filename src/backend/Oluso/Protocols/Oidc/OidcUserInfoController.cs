using System.IdentityModel.Tokens.Jwt;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Logging;
using Oluso.Core.Domain.Interfaces;
using Oluso.Core.Protocols;
using Oluso.Core.Protocols.Models;
using Oluso.Core.Services;

namespace Oluso.Protocols.Oidc;

/// <summary>
/// OpenID Connect UserInfo Endpoint.
/// Route is configured via OidcEndpointRouteConvention.
/// </summary>

public class OidcUserInfoController : OidcControllerBase
{
    private readonly IProfileService _profileService;
    private readonly IClientStore _clientStore;
    private readonly ITenantContext _tenantContext;
    private readonly ILogger<OidcUserInfoController> _logger;

    public OidcUserInfoController(
        IProfileService profileService,
        IClientStore clientStore,
        ITenantContext tenantContext,
        ILogger<OidcUserInfoController> logger)
    {
        _profileService = profileService;
        _clientStore = clientStore;
        _tenantContext = tenantContext;
        _logger = logger;
    }

    [HttpGet]
    [HttpPost]
    [Authorize(AuthenticationSchemes = OidcConstants.AccessTokenAuthenticationScheme)]
    // Scope-restricted clients may read the profile their openid token grants.
    [Oluso.Core.Authorization.RequireScope("openid")]
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

        // The sub is the one the client was issued in its ID token (OpenID Connect Core 1.0 section 5.3.2: they must
        // match), so pairwise for a pairwise client. The access token carries the user's own id, which is what the
        // profile is looked up by and what Oluso's APIs use; a pairwise client must not learn it here.
        var clientId = User.FindFirst(OidcConstants.StandardClaims.ClientId)?.Value;
        var client = string.IsNullOrEmpty(clientId) ? null : await _clientStore.FindClientByIdAsync(clientId, cancellationToken);
        if (client == null)
        {
            return Unauthorized(new TokenErrorResponse
            {
                Error = OidcConstants.Errors.InvalidToken,
                ErrorDescription = "The client the token was issued to is not known"
            });
        }
        claims[OidcConstants.StandardClaims.Subject] =
            SubjectIdentifiers.ForClient(subjectClaim.Value, client.ClientId, client.PairWiseSubjectSalt);

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
