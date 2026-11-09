using Oluso.Core.Domain.Entities;

namespace Oluso.Admin.Dto.Teams;

public class TeamDto
{
    public string Id { get; set; } = string.Empty;
    public string Code { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
    public string? Description { get; set; }
    public TeamType Type { get; set; }
    public string? ParentTeamId { get; set; }
    public string? OwnerUserId { get; set; }
    public TeamVisibility Visibility { get; set; }
    public bool IsActive { get; set; }
    public int DisplayOrder { get; set; }
    public int MemberCount { get; set; }

    /// <summary>Feature key of the module that maintains this team; null when managed by hand.</summary>
    public string? ManagedBy { get; set; }

    /// <summary>Display name of that module (e.g. "Education"), for "Managed by ..." labels.</summary>
    public string? ManagedByName { get; set; }

    public DateTime CreatedAt { get; set; }
    public string? CreatedBy { get; set; }
    public DateTime? UpdatedAt { get; set; }
    public string? UpdatedBy { get; set; }
}

public sealed record TeamSummaryDto(
    string Id,
    string Code,
    string Name,
    TeamType Type,
    string? ParentTeamId,
    bool IsActive);

public class TeamMembershipDto
{
    public string Id { get; set; } = string.Empty;
    public string TeamId { get; set; } = string.Empty;
    public string UserId { get; set; } = string.Empty;
    public TeamMemberRole Role { get; set; }
    public bool IsActive { get; set; }
    public DateTime JoinedAt { get; set; }
    public DateTime? LeftAt { get; set; }
}

public class CreateTeamRequest
{
    public required string Code { get; init; }
    public required string Name { get; init; }
    public string? Description { get; init; }
    public TeamType Type { get; init; } = TeamType.Functional;
    public string? ParentTeamId { get; init; }
    public string? OwnerUserId { get; init; }
    public TeamVisibility Visibility { get; init; } = TeamVisibility.TenantWide;
    public bool IsActive { get; init; } = true;
    public int DisplayOrder { get; init; }
}

public class UpdateTeamRequest
{
    public string? Code { get; init; }
    public string? Name { get; init; }
    public string? Description { get; init; }
    public TeamType? Type { get; init; }
    public string? ParentTeamId { get; init; }
    public string? OwnerUserId { get; init; }
    public TeamVisibility? Visibility { get; init; }
    public int? DisplayOrder { get; init; }
}

public class AddMemberRequest
{
    public required string UserId { get; init; }
    public TeamMemberRole Role { get; init; } = TeamMemberRole.Member;
}

public class ChangeMemberRoleRequest
{
    public TeamMemberRole Role { get; init; }
}
