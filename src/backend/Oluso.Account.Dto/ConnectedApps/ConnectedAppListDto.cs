namespace Oluso.Account.Dto.ConnectedApps;

public class ConnectedAppListDto
{
    public List<ConnectedAppDto> Apps { get; set; } = new();
    public int TotalCount { get; set; }
}
