namespace Oluso.Account.Dto.Teams;

public sealed record MyTeamDto(
    string Id,
    string Code,
    string Name,
    string? Description,
    string Type,
    string? ParentTeamId,
    string Role,
    DateTime JoinedAt);
