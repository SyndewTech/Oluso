using Microsoft.AspNetCore.Authorization;

namespace Oluso.Admin.Authorization;

/// <summary>
/// Requires the user to have a specific permission to access the endpoint.
/// Permissions are checked against the user's role permissions.
/// </summary>
[AttributeUsage(AttributeTargets.Method | AttributeTargets.Class, AllowMultiple = true)]
public class RequirePermissionAttribute : AuthorizeAttribute
{
    /// <summary>
    /// The permission required to access the endpoint
    /// </summary>
    public string Permission { get; }

    /// <summary>
    /// Creates a new RequirePermissionAttribute
    /// </summary>
    /// <param name="permission">The permission required (e.g., "users.read", "clients.write")</param>
    public RequirePermissionAttribute(string permission)
    {
        Permission = permission;
        // Use a policy name that the handler will recognize
        Policy = $"{PermissionAuthorizationHandler.PolicyPrefix}{permission}";
    }
}

/// <summary>
/// Requires the user to have ANY of the specified permissions
/// </summary>
[AttributeUsage(AttributeTargets.Method | AttributeTargets.Class)]
public class RequireAnyPermissionAttribute : AuthorizeAttribute
{
    /// <summary>
    /// The permissions, any one of which grants access
    /// </summary>
    public string[] Permissions { get; }

    /// <summary>
    /// Creates a new RequireAnyPermissionAttribute
    /// </summary>
    /// <param name="permissions">The permissions, any one of which grants access</param>
    public RequireAnyPermissionAttribute(params string[] permissions)
    {
        Permissions = permissions;
        // Create a compound policy name
        Policy = $"{PermissionAuthorizationHandler.AnyPolicyPrefix}{string.Join(",", permissions)}";
    }
}
