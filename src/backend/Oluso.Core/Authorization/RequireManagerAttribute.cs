using Microsoft.AspNetCore.Authorization;

namespace Oluso.Core.Authorization;

/// <summary>
/// Requires the user to be a manager (has the is_manager claim set to true).
/// The is_manager claim is set by a module's claims provider when the user has direct reports.
/// </summary>
[AttributeUsage(AttributeTargets.Method | AttributeTargets.Class, AllowMultiple = false)]
public class RequireManagerAttribute : AuthorizeAttribute
{
    public RequireManagerAttribute()
    {
        Policy = "IsManager";
    }
}

/// <summary>
/// Authorization requirement for manager access
/// </summary>
public class ManagerRequirement : IAuthorizationRequirement
{
}

/// <summary>
/// Handler for the manager requirement - checks for is_manager claim
/// </summary>
public class ManagerAuthorizationHandler : AuthorizationHandler<ManagerRequirement>
{
    protected override Task HandleRequirementAsync(
        AuthorizationHandlerContext context,
        ManagerRequirement requirement)
    {
        // Check for is_manager claim (set by HRClaimsProvider)
        var isManagerClaim = context.User.FindFirst("is_manager")?.Value;

        if (isManagerClaim != null &&
            (isManagerClaim.Equals("true", StringComparison.OrdinalIgnoreCase) ||
             isManagerClaim == "1" ||
             isManagerClaim.Equals("True", StringComparison.Ordinal)))
        {
            context.Succeed(requirement);
        }

        return Task.CompletedTask;
    }
}
