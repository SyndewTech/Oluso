namespace Oluso.Admin.Dto.Context;

public class AdminContextDto
{
    public string? UserId { get; set; }
    public string? Email { get; set; }
    public bool IsSuperAdmin { get; set; }
    public bool IsOrgAdmin { get; set; }
    public TenantSummaryDto? CurrentTenant { get; set; }
    public OrganizationSummaryDto? CurrentOrganization { get; set; }
    public List<TenantSwitchDto> AvailableTenants { get; set; } = new();
    public List<OrganizationMembershipSummaryDto> Organizations { get; set; } = new();
}

public class TenantSummaryDto
{
    public string Id { get; set; } = null!;
    public string? Name { get; set; }
    public string? Identifier { get; set; }
}

public class OrganizationSummaryDto
{
    public string Id { get; set; } = null!;
    public string? Name { get; set; }
    public string? Role { get; set; }
}

public class OrganizationMembershipSummaryDto
{
    public string OrganizationId { get; set; } = null!;
    public string? OrganizationName { get; set; }
    public string? OrganizationSlug { get; set; }
    public string Role { get; set; } = null!;
}

public class TenantSwitchDto
{
    public string Id { get; set; } = null!;
    public string Name { get; set; } = null!;
    public string? DisplayName { get; set; }
    public string Identifier { get; set; } = null!;
    public string Environment { get; set; } = null!;
    public string? OrganizationId { get; set; }
    public string? UserRole { get; set; }
    public bool Enabled { get; set; }

    /// <summary>The user's home tenant: where a sign-in lands, and where leaving a switched tenant returns to.</summary>
    public bool IsHome { get; set; }
}

public class OrganizationSwitchDto
{
    public string Id { get; set; } = null!;
    public string Name { get; set; } = null!;
    public string Slug { get; set; } = null!;
    public string Role { get; set; } = null!;
    public int TenantCount { get; set; }
    public bool Enabled { get; set; }
}

public class TenantDetailDto
{
    public string Id { get; set; } = null!;
    public string Name { get; set; } = null!;
    public string? DisplayName { get; set; }
    public string Identifier { get; set; } = null!;
    public string? Description { get; set; }
    public string Environment { get; set; } = null!;
    public bool Enabled { get; set; }
    public string? OrganizationId { get; set; }
    public string? OrganizationName { get; set; }
    public DateTime Created { get; set; }
    public DateTime? Updated { get; set; }
}
