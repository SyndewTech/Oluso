using Microsoft.AspNetCore.Authorization;
using Microsoft.Extensions.Options;

namespace Oluso.Admin.Authorization;

/// <summary>
/// Dynamic policy provider that creates authorization policies for permission-based authorization.
/// This allows using [RequirePermission("users.read")] without pre-registering every policy.
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
        if (policyName.StartsWith(PermissionAuthorizationHandler.PolicyPrefix))
        {
            var permission = policyName[PermissionAuthorizationHandler.PolicyPrefix.Length..];

            var policy = new AuthorizationPolicyBuilder()
                .RequireAuthenticatedUser()
                .AddRequirements(new PermissionRequirement(permission))
                .Build();

            return policy;
        }

        // Handle "any permission" policies
        if (policyName.StartsWith(PermissionAuthorizationHandler.AnyPolicyPrefix))
        {
            var permissionsStr = policyName[PermissionAuthorizationHandler.AnyPolicyPrefix.Length..];
            var permissions = permissionsStr.Split(',', StringSplitOptions.RemoveEmptyEntries);

            var policy = new AuthorizationPolicyBuilder()
                .RequireAuthenticatedUser()
                .AddRequirements(new AnyPermissionRequirement(permissions))
                .Build();

            return policy;
        }

        // Fall back to the default provider for other policies (like "AdminApi")
        return await _fallbackPolicyProvider.GetPolicyAsync(policyName);
    }

    public Task<AuthorizationPolicy> GetDefaultPolicyAsync()
    {
        return _fallbackPolicyProvider.GetDefaultPolicyAsync();
    }

    public Task<AuthorizationPolicy?> GetFallbackPolicyAsync()
    {
        return _fallbackPolicyProvider.GetFallbackPolicyAsync();
    }
}
