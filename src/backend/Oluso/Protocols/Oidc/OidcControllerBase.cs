using Microsoft.AspNetCore.Mvc;

namespace Oluso.Protocols.Oidc;

/// <summary>
/// Base controller for all OIDC protocol endpoints.
/// Excludes these endpoints from Swagger/OpenAPI documentation since they
/// follow the OpenID Connect specification and don't need API docs.
/// </summary>
[ApiExplorerSettings(IgnoreApi = true)]
public abstract class OidcControllerBase : ControllerBase
{
}
