using Microsoft.AspNetCore.Mvc;
using Oluso.Enterprise.Scim.Services;

namespace Oluso.Enterprise.Scim.Endpoints;

/// <summary>
/// Translates a transport-agnostic <see cref="ScimOperationResult"/> into the
/// appropriate MVC <see cref="IActionResult"/>.
/// </summary>
public static class ScimControllerExtensions
{
    public static IActionResult ScimResult(this ControllerBase controller, ScimOperationResult result)
    {
        if (result.Status == 204)
            return controller.NoContent();

        if (result.Status == 201 && result.Location != null)
            return controller.Created(result.Location, result.Body);

        return controller.StatusCode(result.Status, result.Body);
    }
}
