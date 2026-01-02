using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Oluso.Core.Domain.Interfaces;

namespace Oluso.Core.Api;

/// <summary>
/// Base controller for Service-to-Service API endpoints.
/// Used by machine clients (resource servers, microservices) for automated operations.
///
/// Authentication: Client credentials grant with "service" scope.
/// Tenant isolation: Tenant is determined by client registration (immutable).
///
/// Unlike Admin/Account APIs:
/// - No user context (machine-to-machine)
/// - Tenant comes from client configuration, not headers or tokens
/// - Cannot switch tenants - client is bound to a single tenant
/// </summary>
[ApiController]
[Authorize(Policy = "ServiceApi")]
public abstract class ServiceBaseController : ControllerBase
{
    private readonly ITenantContext _tenantContext;

    protected ServiceBaseController(ITenantContext tenantContext)
    {
        _tenantContext = tenantContext;
    }

    /// <summary>
    /// Gets the client ID making the request (from client_id claim)
    /// </summary>
    protected string ClientId =>
        User.FindFirst("client_id")?.Value
        ?? throw new UnauthorizedAccessException("No client_id in token");

    /// <summary>
    /// Gets the tenant ID from the client's configuration.
    /// This is set during client registration and cannot be changed at runtime.
    /// </summary>
    protected string? TenantId => _tenantContext.TenantId;

    /// <summary>
    /// Gets the tenant context
    /// </summary>
    protected ITenantContext TenantContext => _tenantContext;

    /// <summary>
    /// Gets the client IP address
    /// </summary>
    protected string? ClientIp => HttpContext.Connection.RemoteIpAddress?.ToString();

    /// <summary>
    /// Checks if the client has a specific scope
    /// </summary>
    protected bool HasScope(string scope) =>
        User.FindAll("scope").Any(c => c.Value == scope) ||
        User.FindFirst("scope")?.Value?.Split(' ').Contains(scope) == true;

    /// <summary>
    /// Checks if the client has all of the specified scopes
    /// </summary>
    protected bool HasAllScopes(params string[] scopes) =>
        scopes.All(HasScope);

    /// <summary>
    /// Checks if the client has any of the specified scopes
    /// </summary>
    protected bool HasAnyScope(params string[] scopes) =>
        scopes.Any(HasScope);

    /// <summary>
    /// Gets all scopes granted to the client
    /// </summary>
    protected IEnumerable<string> GetScopes()
    {
        // Handle both space-separated and multiple claims
        var scopeClaims = User.FindAll("scope").Select(c => c.Value).ToList();

        return scopeClaims.SelectMany(s => s.Split(' ', StringSplitOptions.RemoveEmptyEntries)).Distinct();
    }

    /// <summary>
    /// Gets the JWT ID (jti claim) for idempotency checks
    /// </summary>
    protected string? TokenId => User.FindFirst("jti")?.Value;

    /// <summary>
    /// Gets the token expiration time
    /// </summary>
    protected DateTimeOffset? TokenExpiration
    {
        get
        {
            var exp = User.FindFirst("exp")?.Value;
            if (exp != null && long.TryParse(exp, out var expUnix))
            {
                return DateTimeOffset.FromUnixTimeSeconds(expUnix);
            }
            return null;
        }
    }
}
