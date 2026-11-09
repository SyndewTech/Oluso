using Microsoft.AspNetCore.Authorization;
using Microsoft.Extensions.Logging;
using Oluso.Core.Authorization;
using Oluso.Admin.Controllers.Service;

namespace Oluso.Admin.Authorization;

/// <summary>
/// Requirement for Service API access (machine-to-machine).
/// Validates that the client has the base service scope.
/// </summary>
public class ServiceApiRequirement : IAuthorizationRequirement
{
    /// <summary>
    /// The base scope required for service API access
    /// </summary>
    public string RequiredScope { get; }

    public ServiceApiRequirement(string? requiredScope = null)
    {
        RequiredScope = requiredScope ?? ServiceScopes.ServiceApi;
    }
}

/// <summary>
/// Handler for Service API authorization.
/// Validates that:
/// 1. Request is from a client (has client_id claim)
/// 2. Client has the required base service scope
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

        if (!context.User.HasGrantedScope(requirement.RequiredScope))
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
