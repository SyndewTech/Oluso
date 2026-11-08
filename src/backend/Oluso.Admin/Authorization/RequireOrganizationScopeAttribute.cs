using System.Security.Claims;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Filters;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Oluso.Core.Domain.Interfaces;

namespace Oluso.Admin.Authorization;

/// <summary>
/// Confines an action carrying an <c>{organizationId}</c> route value to that
/// organization.
/// <para>
/// Permissions alone are not enough here: <c>organizations.manage_members</c>
/// says the caller may manage members somewhere, not that they may manage
/// <em>this</em> organization's. Without this, an owner of one organization
/// could read and rewrite the membership of every other.
/// </para>
/// <para>
/// Reads require membership; writes require Owner or Admin. Actions with no
/// <c>{organizationId}</c> route value are untouched, so list/create endpoints
/// keep doing their own scoping.
/// </para>
/// </summary>
[AttributeUsage(AttributeTargets.Class | AttributeTargets.Method)]
public sealed class RequireOrganizationScopeAttribute : Attribute, IAsyncActionFilter
{
    public async Task OnActionExecutionAsync(ActionExecutingContext context, ActionExecutionDelegate next)
    {
        if (!context.RouteData.Values.TryGetValue("organizationId", out var routeValue)
            || routeValue is not string organizationId
            || string.IsNullOrEmpty(organizationId))
        {
            await next();
            return;
        }

        var isWrite = !HttpMethods.IsGet(context.HttpContext.Request.Method);
        if (!await IsAllowedAsync(context.HttpContext, organizationId, isWrite))
        {
            context.Result = new ForbidResult();
            return;
        }

        await next();
    }

    /// <summary>
    /// The organization-scope rule, for actions that only learn the organization
    /// after loading it (e.g. by slug): reads need membership, writes need Owner
    /// or Admin; SuperAdmins pass.
    /// </summary>
    public static async Task<bool> IsAllowedAsync(HttpContext httpContext, string organizationId, bool isWrite)
    {
        var user = httpContext.User;

        // SuperAdmins are cross-organization by definition. The role rather than
        // the super_admin claim, because TenantResolutionMiddleware strips that
        // claim while they operate inside a non-platform tenant.
        if (HasRole(user, "SuperAdmin") || HasRole(user, "SystemAdmin"))
        {
            return true;
        }

        var userId = user.FindFirst("sub")?.Value
            ?? user.FindFirst(ClaimTypes.NameIdentifier)?.Value;

        if (string.IsNullOrEmpty(userId))
        {
            return false;
        }

        var services = httpContext.RequestServices;
        var memberships = services.GetService<IOrganizationMembershipStore>();
        if (memberships == null)
        {
            return true;
        }

        var allowed = isWrite
            ? await memberships.IsAdminAsync(userId, organizationId)
            : await memberships.IsMemberAsync(userId, organizationId);

        if (!allowed)
        {
            services.GetService<ILoggerFactory>()?
                .CreateLogger<RequireOrganizationScopeAttribute>()
                .LogWarning(
                    "User {UserId} denied {Method} on organization {OrganizationId} — not {Required} of it",
                    userId, httpContext.Request.Method, organizationId, isWrite ? "an admin" : "a member");
        }

        return allowed;
    }

    private static bool HasRole(ClaimsPrincipal user, string role) =>
        user.FindAll("role").Concat(user.FindAll(ClaimTypes.Role))
            .Any(c => c.Value.Equals(role, StringComparison.OrdinalIgnoreCase));
}
