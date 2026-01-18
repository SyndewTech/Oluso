namespace Oluso.Admin.Controllers.Service;

/// <summary>
/// Scopes required for Service API access.
/// These scopes should be assigned to clients that need M2M access to user/role management.
/// </summary>
public static class ServiceScopes
{
    // User management scopes
    public const string UsersRead = "oluso:users:read";
    public const string UsersWrite = "oluso:users:write";
    public const string UsersDelete = "oluso:users:delete";
    public const string UsersManageRoles = "oluso:users:manage-roles";

    // Role management scopes
    public const string RolesRead = "oluso:roles:read";
    public const string RolesWrite = "oluso:roles:write";
    public const string RolesDelete = "oluso:roles:delete";

    // Client management scopes (future)
    public const string ClientsRead = "oluso:clients:read";
    public const string ClientsWrite = "oluso:clients:write";

    // Audit log scopes (future)
    public const string AuditRead = "oluso:audit:read";

    /// <summary>
    /// All user management scopes
    /// </summary>
    public static readonly string[] AllUserScopes = new[]
    {
        UsersRead,
        UsersWrite,
        UsersDelete,
        UsersManageRoles
    };

    /// <summary>
    /// All role management scopes
    /// </summary>
    public static readonly string[] AllRoleScopes = new[]
    {
        RolesRead,
        RolesWrite,
        RolesDelete
    };

    /// <summary>
    /// All available service API scopes
    /// </summary>
    public static readonly string[] AllScopes = new[]
    {
        UsersRead,
        UsersWrite,
        UsersDelete,
        UsersManageRoles,
        RolesRead,
        RolesWrite,
        RolesDelete,
        ClientsRead,
        ClientsWrite,
        AuditRead
    };
}
