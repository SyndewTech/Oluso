using Microsoft.AspNetCore.Mvc;
using Oluso.Admin.Authorization;
using Oluso.Admin.Controllers.Service;
using Oluso.Admin.Dto.Scopes;
using Oluso.Core.Api;
using Oluso.Core.Domain.Interfaces;
using Oluso.Core.Licensing;
using Oluso.Core.Protocols.Models;

namespace Oluso.Admin.Controllers;

/// <summary>
/// Admin API describing the Service API surface — the permissions a client can
/// be granted for machine-to-machine access.
///
/// Read-only: the catalog comes from <see cref="ServiceScopes"/>, so the checkbox
/// list in the admin UI can never drift from what the endpoints actually enforce.
/// A module's scopes are marked unavailable while the organization doesn't have
/// the module, the same check the module's service endpoints make.
/// </summary>
[Route("api/admin/service-api")]
public class ServiceApiController : AdminBaseController
{
    private readonly IFeatureGate _featureGate;

    public ServiceApiController(ITenantContext tenantContext, IFeatureGate featureGate)
        : base(tenantContext)
    {
        _featureGate = featureGate;
    }

    /// <summary>
    /// Get the grantable Service API permissions
    /// </summary>
    [HttpGet("scopes")]
    [RequirePermission(AdminPermissions.ClientsRead)]
    public async Task<ActionResult<ServiceApiCatalogDto>> GetScopes(CancellationToken cancellationToken)
    {
        var catalog = ServiceScopes.Catalog;
        var unavailable = await ServiceScopes.UnavailableFeaturesAsync(_featureGate, catalog, cancellationToken);
        return Ok(new ServiceApiCatalogDto
        {
            BaseScope = ServiceScopes.ServiceApi,
            RequiredGrantTypes = new List<string> { OidcConstants.GrantTypes.ClientCredentials },
            Scopes = catalog.Select(d => new ServiceApiScopeDto
            {
                Name = d.Name,
                DisplayName = d.DisplayName,
                Description = d.Description,
                Group = d.Group,
                RequiresExplicitGrant = d.RequiresExplicitGrant,
                RequiredAdminPermission = d.RequiredAdminPermission,
                Available = d.FeatureKey == null || !unavailable.Contains(d.FeatureKey),
            }).ToList()
        });
    }
}
