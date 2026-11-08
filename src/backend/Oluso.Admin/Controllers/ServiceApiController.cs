using Microsoft.AspNetCore.Mvc;
using Oluso.Admin.Authorization;
using Oluso.Admin.Controllers.Service;
using Oluso.Admin.Dto.Scopes;
using Oluso.Core.Api;
using Oluso.Core.Domain.Interfaces;
using Oluso.Core.Protocols.Models;

namespace Oluso.Admin.Controllers;

/// <summary>
/// Admin API describing the Service API surface — the permissions a client can
/// be granted for machine-to-machine access.
///
/// Read-only and static: the catalog comes from <see cref="ServiceScopes"/>, so
/// the checkbox list in the admin UI can never drift from what the endpoints
/// actually enforce.
/// </summary>
[Route("api/admin/service-api")]
public class ServiceApiController : AdminBaseController
{
    public ServiceApiController(ITenantContext tenantContext)
        : base(tenantContext)
    {
    }

    /// <summary>
    /// Get the grantable Service API permissions
    /// </summary>
    [HttpGet("scopes")]
    [RequirePermission(AdminPermissions.ClientsRead)]
    public ActionResult<ServiceApiCatalogDto> GetScopes()
    {
        return Ok(new ServiceApiCatalogDto
        {
            BaseScope = ServiceScopes.ServiceApi,
            RequiredGrantTypes = new List<string> { OidcConstants.GrantTypes.ClientCredentials },
            Scopes = ServiceScopes.Catalog.Select(d => new ServiceApiScopeDto
            {
                Name = d.Name,
                DisplayName = d.DisplayName,
                Description = d.Description,
                Group = d.Group,
                RequiresExplicitGrant = d.RequiresExplicitGrant,
                RequiredAdminPermission = d.RequiredAdminPermission
            }).ToList()
        });
    }
}
