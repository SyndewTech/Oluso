using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc.Filters;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Oluso.Core.Domain.Interfaces;

namespace Oluso.Core.Authorization;

/// <summary>
/// Scope-restricted clients: a client whose <see cref="Domain.Entities.Client.Properties"/> carry
/// <see cref="PropertyKey"/> = <c>true</c> gets user tokens that the user-facing APIs accept only on
/// endpoints declaring (<see cref="RequireScopeAttribute"/>, <see cref="IScopeRequirementMetadata"/>)
/// a scope the token carries. Everywhere else the token is refused with 403. Used for apps that act
/// for a user on a narrow surface (for example a mobile app deciding approvals), so a leaked token can do only that.
/// </summary>
public static class ScopeRestrictedClients
{
    /// <summary>Client property that turns the restriction on (value <c>true</c>).</summary>
    public const string PropertyKey = "restrict_to_scoped_endpoints";

    /// <summary>
    /// Registers the requirement's handler and the client lookup. Add
    /// <see cref="ScopeRestrictedClientRequirement"/> to each policy user tokens pass through.
    /// </summary>
    public static IServiceCollection AddScopeRestrictedClients(this IServiceCollection services)
    {
        services.AddMemoryCache();
        services.TryAddScoped<IScopeRestrictedClientResolver, ScopeRestrictedClientResolver>();
        services.TryAddEnumerable(ServiceDescriptor.Scoped<IAuthorizationHandler, ScopeRestrictedClientHandler>());
        return services;
    }
}

/// <summary>
/// Policy requirement: passes for every token except one issued to a scope-restricted client,
/// which passes only when the endpoint declares scopes and the token carries one of each.
/// </summary>
public sealed class ScopeRestrictedClientRequirement : IAuthorizationRequirement;

/// <summary>Says whether a client is scope-restricted.</summary>
public interface IScopeRestrictedClientResolver
{
    Task<bool> IsRestrictedAsync(string clientId, CancellationToken cancellationToken = default);
}

/// <inheritdoc />
/// <remarks>
/// Runs on every user-API call, so the answer is cached per tenant and client for a minute; turning
/// the restriction on or off takes effect within that time.
/// </remarks>
public sealed class ScopeRestrictedClientResolver : IScopeRestrictedClientResolver
{
    private static readonly TimeSpan CacheFor = TimeSpan.FromMinutes(1);

    private readonly IClientStore _clients;
    private readonly ITenantContext _tenantContext;
    private readonly IMemoryCache _cache;

    public ScopeRestrictedClientResolver(IClientStore clients, ITenantContext tenantContext, IMemoryCache cache)
    {
        _clients = clients;
        _tenantContext = tenantContext;
        _cache = cache;
    }

    public async Task<bool> IsRestrictedAsync(string clientId, CancellationToken cancellationToken = default)
    {
        var key = $"oluso:scope-restricted-client:{_tenantContext.TenantId}:{clientId}";
        if (_cache.TryGetValue(key, out bool restricted)) return restricted;

        var client = await _clients.FindClientByIdAsync(clientId, cancellationToken);
        restricted = client?.Properties.Any(p =>
            string.Equals(p.Key, ScopeRestrictedClients.PropertyKey, StringComparison.OrdinalIgnoreCase)
            && string.Equals(p.Value?.Trim(), "true", StringComparison.OrdinalIgnoreCase)) == true;

        _cache.Set(key, restricted, CacheFor);
        return restricted;
    }
}

/// <summary>Enforces <see cref="ScopeRestrictedClientRequirement"/>.</summary>
public sealed class ScopeRestrictedClientHandler : AuthorizationHandler<ScopeRestrictedClientRequirement>
{
    private readonly IScopeRestrictedClientResolver _resolver;

    public ScopeRestrictedClientHandler(IScopeRestrictedClientResolver resolver)
    {
        _resolver = resolver;
    }

    protected override async Task HandleRequirementAsync(
        AuthorizationHandlerContext context, ScopeRestrictedClientRequirement requirement)
    {
        var clientId = context.User.FindFirst("client_id")?.Value;
        if (string.IsNullOrEmpty(clientId))
        {
            context.Succeed(requirement);
            return;
        }

        var httpContext = context.Resource switch
        {
            HttpContext http => http,
            AuthorizationFilterContext filter => filter.HttpContext,
            _ => null,
        };

        if (!await _resolver.IsRestrictedAsync(clientId, httpContext?.RequestAborted ?? CancellationToken.None))
        {
            context.Succeed(requirement);
            return;
        }

        if (httpContext != null && IsAllowed(httpContext, context.User.GetGrantedScopes()))
        {
            context.Succeed(requirement);
            return;
        }

        context.Fail(new AuthorizationFailureReason(this,
            $"Client '{clientId}' is restricted to endpoints that require a scope its token carries."));
    }

    /// <summary>
    /// True when the endpoint declares at least one applicable scope requirement and the token
    /// carries a scope from every one of them.
    /// </summary>
    public static bool IsAllowed(HttpContext httpContext, IReadOnlyCollection<string> grantedScopes)
    {
        var metadata = httpContext.GetEndpoint()?.Metadata.GetOrderedMetadata<IScopeRequirementMetadata>();
        if (metadata == null || metadata.Count == 0) return false;

        var applicable = metadata
            .Select(m => m.GetRequiredScopes(httpContext))
            .Where(s => s != null)
            .Select(s => s!)
            .ToList();
        if (applicable.Count == 0) return false;

        return applicable.All(scopes => scopes.Any(s => grantedScopes.Contains(s, StringComparer.Ordinal)));
    }
}
