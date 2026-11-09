namespace Oluso.Account.Dto.Sessions;

public class SessionListDto
{
    public List<SessionDto> Sessions { get; set; } = new();
    public int TotalCount { get; set; }
}
