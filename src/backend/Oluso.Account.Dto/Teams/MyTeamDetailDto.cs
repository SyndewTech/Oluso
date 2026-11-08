namespace Oluso.Account.Dto.Teams;

public sealed record MyTeamDetailDto(
    string Id,
    string Code,
    string Name,
    string? Description,
    string Type,
    string? ParentTeamId,
    string? OwnerUserId,
    string Visibility,
    bool IsActive,
    string? MyRole,
    bool IsMember,
    IReadOnlyList<MyTeamMemberDto> Members);
