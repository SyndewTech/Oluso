using Microsoft.AspNetCore.Authorization;
using Microsoft.Extensions.Options;
using Oluso.Core.Authorization;

namespace Oluso.Admin.Authorization;

/// <summary>
/// Dynamic policy provider that creates authorization policies for permission-based authorization.
/// This allows using [RequirePermission("users.read")] without pre-registering every policy.
/// Every policy it builds, and the default policy behind a bare [Authorize], also carries
/// <see cref="ScopeRestrictedClientRequirement"/>, so a scope-restricted client's token can't reach
/// an endpoint just because it isn't behind one of the named API policies.
/// </summary>
public class PermissionPolicyProvider : IAuthorizationPolicyProvider
{
    private readonly DefaultAuthorizationPolicyProvider _fallbackPolicyProvider;

    public PermissionPolicyProvider(IOptions<AuthorizationOptions> options)
    {
        _fallbackPolicyProvider = new DefaultAuthorizationPolicyProvider(options);
    }

    public async Task<AuthorizationPolicy?> GetPolicyAsync(string policyName)
    {
        // Handle single permission policies
        if (policyName.StartsWith(PermissionPolicyConstants.PolicyPrefix))
        {
            var permission = policyName[PermissionPolicyConstants.PolicyPrefix.Length..];

            var policy = new AuthorizationPolicyBuilder()
                .RequireAuthenticatedUser()
                .AddRequirements(new PermissionRequirement(permission))
                .AddRequirements(new ScopeRestrictedClientRequirement())
                .Build();

            return policy;
        }

        // Handle "any permission" policies
        if (policyName.StartsWith(PermissionPolicyConstants.AnyPolicyPrefix))
        {
            var permissionsStr = policyName[PermissionPolicyConstants.AnyPolicyPrefix.Length..];
            var permissions = permissionsStr.Split(',', StringSplitOptions.RemoveEmptyEntries);

            var policy = new AuthorizationPolicyBuilder()
                .RequireAuthenticatedUser()
                .AddRequirements(new AnyPermissionRequirement(permissions))
                .AddRequirements(new ScopeRestrictedClientRequirement())
                .Build();

            return policy;
        }

        // Fall back to the default provider for other policies (like "AdminApi")
        return await _fallbackPolicyProvider.GetPolicyAsync(policyName);
    }

    public async Task<AuthorizationPolicy> GetDefaultPolicyAsync()
    {
        var policy = await _fallbackPolicyProvider.GetDefaultPolicyAsync();
        return policy.Requirements.OfType<ScopeRestrictedClientRequirement>().Any()
            ? policy
            : new AuthorizationPolicyBuilder(policy).AddRequirements(new ScopeRestrictedClientRequirement()).Build();
    }

    public Task<AuthorizationPolicy?> GetFallbackPolicyAsync()
    {
        return _fallbackPolicyProvider.GetFallbackPolicyAsync();
    }
}
