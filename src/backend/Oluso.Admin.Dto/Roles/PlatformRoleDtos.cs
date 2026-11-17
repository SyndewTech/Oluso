namespace Oluso.Admin.Dto.Roles;

/// <summary>A platform-wide role (no owning tenant) with how many people hold it.</summary>
public class PlatformRoleDto
{
    public string Id { get; set; } = null!;
    public string Name { get; set; } = null!;
    public string DisplayName { get; set; } = null!;
    public string? Description { get; set; }
    public bool IsSystemRole { get; set; }

    /// <summary>SuperAdmin and SystemAdmin: held anywhere, they apply in every tenant.</summary>
    public bool AppliesEverywhere { get; set; }

    /// <summary>Follows organization membership (OrgAdmin), so it is neither assigned nor removed by hand.</summary>
    public bool IsDerived { get; set; }

    /// <summary>Distinct people holding the role in any tenant.</summary>
    public int HolderCount { get; set; }

    /// <summary>Assignments across all tenants; one person can hold the role in several.</summary>
    public int AssignmentCount { get; set; }
}

/// <summary>One assignment of a platform role.</summary>
public class PlatformRoleHolderDto
{
    public string UserId { get; set; } = null!;
    public string UserName { get; set; } = null!;
    public string Email { get; set; } = null!;
    public string? DisplayName { get; set; }
    public bool IsActive { get; set; }
    public string? HomeTenantId { get; set; }
    public string? HomeTenantName { get; set; }

    /// <summary>The tenant the assignment applies in.</summary>
    public string TenantId { get; set; } = null!;
    public string? TenantName { get; set; }
    public DateTime AssignedAt { get; set; }
    public string? AssignedBy { get; set; }
    public string? AssignedByName { get; set; }
}

public class AssignPlatformRoleRequest
{
    public string UserId { get; set; } = null!;
    public string TenantId { get; set; } = null!;
}

/// <summary>A tenant to pick when assigning a platform role.</summary>
public record PlatformRoleTenantOptionDto(string Id, string Name, string? Identifier);

/// <summary>A person in the chosen tenant to pick when assigning a platform role.</summary>
public record PlatformRoleCandidateDto(string Id, string Email, string? DisplayName, string UserName);
