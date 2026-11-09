using Microsoft.AspNetCore.Authorization;

namespace Oluso.Core.Authorization;

/// <summary>
/// Policy name constants for permission-based authorization
/// </summary>
public static class PermissionPolicyConstants
{
    public const string PolicyPrefix = "Permission:";
    public const string AnyPolicyPrefix = "AnyPermission:";
}

/// <summary>
/// Authorization requirement for a single permission
/// </summary>
public class PermissionRequirement : IAuthorizationRequirement
{
    public string Permission { get; }

    public PermissionRequirement(string permission)
    {
        Permission = permission;
    }
}

/// <summary>
/// Authorization requirement for any of multiple permissions
/// </summary>
public class AnyPermissionRequirement : IAuthorizationRequirement
{
    public string[] Permissions { get; }

    public AnyPermissionRequirement(params string[] permissions)
    {
        Permissions = permissions;
    }
}
