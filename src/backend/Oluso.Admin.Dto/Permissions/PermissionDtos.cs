namespace Oluso.Admin.Dto.Permissions;

public class PermissionsResponse
{
    public List<PermissionCategoryDto> Categories { get; set; } = new();
    public int TotalCount { get; set; }
}

public class PermissionCategoryDto
{
    public string Name { get; set; } = null!;
    public List<PermissionDto> Permissions { get; set; } = new();
}

public class PermissionDto
{
    public string Name { get; set; } = null!;
    public string DisplayName { get; set; } = null!;
    public string Description { get; set; } = null!;
    public bool RequiresSuperAdmin { get; set; }
}

public class PermissionCheckResult
{
    public string Permission { get; set; } = null!;
    public bool HasPermission { get; set; }
    public string? GrantedBy { get; set; }
}

public class MyPermissionsResponse
{
    public List<string> EffectivePermissions { get; set; } = new();
    public Dictionary<string, List<string>> RolePermissions { get; set; } = new();
    public bool IsSuperAdmin { get; set; }
    public List<string> ExplicitGrantPermissions { get; set; } = new();
}
