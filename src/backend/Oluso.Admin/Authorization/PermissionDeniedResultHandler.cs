using System.Text.Json;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Authorization.Policy;
using Microsoft.AspNetCore.Http;
using Oluso.Core.Authorization;

namespace Oluso.Admin.Authorization;

/// <summary>
/// Intercepts authorization failures to include the required permission name in 403 responses.
/// Falls back to the default handler for non-permission failures.
/// </summary>
public class PermissionDeniedResultHandler : IAuthorizationMiddlewareResultHandler
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
    };

    private readonly AuthorizationMiddlewareResultHandler _defaultHandler = new();

    public async Task HandleAsync(
        RequestDelegate next,
        HttpContext httpContext,
        AuthorizationPolicy policy,
        PolicyAuthorizationResult authorizeResult)
    {
        if (authorizeResult.Forbidden
            && httpContext.Request.Path.StartsWithSegments("/api")
            && authorizeResult.AuthorizationFailure is { } failure)
        {
            var requiredPermissions = new List<string>();

            foreach (var req in failure.FailedRequirements)
            {
                if (req is PermissionRequirement pr)
                    requiredPermissions.Add(pr.Permission);
                else if (req is AnyPermissionRequirement apr)
                    requiredPermissions.AddRange(apr.Permissions);
            }

            if (requiredPermissions.Count > 0)
            {
                httpContext.Response.StatusCode = StatusCodes.Status403Forbidden;
                httpContext.Response.ContentType = "application/json";

                object body = requiredPermissions.Count == 1
                    ? new
                    {
                        error = "forbidden",
                        message = "You do not have the required permission.",
                        requiredPermission = requiredPermissions[0],
                    }
                    : new
                    {
                        error = "forbidden",
                        message = "You do not have any of the required permissions.",
                        requiredPermissions,
                    };

                await httpContext.Response.WriteAsync(JsonSerializer.Serialize(body, JsonOptions));
                return;
            }
        }

        // Non-permission failures (policy-based, tenant access, etc.) → default handler → OnForbidden
        await _defaultHandler.HandleAsync(next, httpContext, policy, authorizeResult);
    }
}
