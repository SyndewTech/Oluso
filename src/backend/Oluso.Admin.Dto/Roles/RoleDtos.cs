namespace Oluso.Admin.Dto.Roles;

public class RoleDto
{
    public string Id { get; set; } = null!;
    public string Name { get; set; } = null!;
    public string DisplayName { get; set; } = null!;
    public string? Description { get; set; }
    public bool IsSystemRole { get; set; }
    public string? TenantId { get; set; }
    public bool IsGlobal { get; set; }

    /// <summary>
    /// Whether the caller may delete this role: a tenant-owned custom role, or
    /// (SuperAdmin only) a platform-wide row that is neither reserved nor still
    /// seeded from the registry.
    /// </summary>
    public bool IsDeletable { get; set; }
    public List<string> Permissions { get; set; } = new();

    /// <summary>
    /// The tenant's own application entitlements granted by this role. Separate
    /// from <see cref="Permissions"/>: those are Oluso's vocabulary and gate the
    /// platform, these are the tenant's and are only delivered to their apps.
    /// </summary>
    public List<string> Entitlements { get; set; } = new();

    public DateTime CreatedAt { get; set; }
    public DateTime? UpdatedAt { get; set; }
}

public class RoleDetailDto : RoleDto
{
    public List<RoleClaimDto> Claims { get; set; } = new();
}

public class RoleClaimDto
{
    public string Type { get; set; } = null!;
    public string Value { get; set; } = null!;
}

public class CreateRoleRequest
{
    public string Name { get; set; } = null!;
    public string? DisplayName { get; set; }
    public string? Description { get; set; }
    public List<string>? Permissions { get; set; }

    /// <summary>
    /// Entitlement names from the tenant's catalog. Validated against it, so an
    /// unknown name fails the save rather than becoming a claim nothing matches.
    /// </summary>
    public List<string>? Entitlements { get; set; }

    public List<RoleClaimDto>? Claims { get; set; }
}

public class RoleUserDto
{
    public string Id { get; set; } = null!;
    public string UserName { get; set; } = null!;
    public string Email { get; set; } = null!;
    public string? DisplayName { get; set; }
}
