using Microsoft.AspNetCore.Authorization;
using Microsoft.Extensions.Logging;

namespace Oluso.Admin.Authorization;

/// <summary>
/// Requirement for Service API access (machine-to-machine).
/// Validates that the client has the "service" scope.
/// </summary>
public class ServiceApiRequirement : IAuthorizationRequirement
{
    /// <summary>
    /// The base scope required for service API access
    /// </summary>
    public string RequiredScope { get; }

    public ServiceApiRequirement(string requiredScope = "service")
    {
        RequiredScope = requiredScope;
    }
}

/// <summary>
/// Handler for Service API authorization.
/// Validates that:
/// 1. Request is from a client (has client_id claim)
/// 2. Client has the required "service" scope
/// </summary>
public class ServiceApiAuthorizationHandler : AuthorizationHandler<ServiceApiRequirement>
{
    private readonly ILogger<ServiceApiAuthorizationHandler> _logger;

    public ServiceApiAuthorizationHandler(ILogger<ServiceApiAuthorizationHandler> logger)
    {
        _logger = logger;
    }

    protected override Task HandleRequirementAsync(
        AuthorizationHandlerContext context,
        ServiceApiRequirement requirement)
    {
        // Check for client_id claim (indicates M2M authentication)
        var clientId = context.User.FindFirst("client_id")?.Value;
        if (string.IsNullOrEmpty(clientId))
        {
            _logger.LogDebug("ServiceApi auth failed: no client_id claim");
            return Task.CompletedTask;
        }

        // Check for required scope
        // OAuth2 scopes can be:
        // 1. Multiple "scope" claims with single values
        // 2. Single "scope" claim with space-separated values
        var hasScope = false;

        // Check multiple scope claims
        var scopeClaims = context.User.FindAll("scope").ToList();
        if (scopeClaims.Any())
        {
            foreach (var claim in scopeClaims)
            {
                // Handle space-separated scopes in a single claim
                var scopes = claim.Value.Split(' ', StringSplitOptions.RemoveEmptyEntries);
                if (scopes.Contains(requirement.RequiredScope))
                {
                    hasScope = true;
                    break;
                }
            }
        }

        // Also check "scp" claim (Microsoft Azure AD style)
        if (!hasScope)
        {
            var scpClaim = context.User.FindFirst("scp")?.Value;
            if (!string.IsNullOrEmpty(scpClaim))
            {
                var scopes = scpClaim.Split(' ', StringSplitOptions.RemoveEmptyEntries);
                hasScope = scopes.Contains(requirement.RequiredScope);
            }
        }

        if (!hasScope)
        {
            _logger.LogDebug("ServiceApi auth failed: client {ClientId} missing required scope '{Scope}'",
                clientId, requirement.RequiredScope);
            return Task.CompletedTask;
        }

        _logger.LogDebug("ServiceApi auth succeeded for client {ClientId}", clientId);
        context.Succeed(requirement);

        return Task.CompletedTask;
    }
}
