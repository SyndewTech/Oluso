namespace Oluso.Account.Dto.ConnectedApps;

public class ConnectedAppDetailDto
{
    public string ClientId { get; set; } = null!;
    public string ClientName { get; set; } = null!;
    public string? ClientUri { get; set; }
    public string? LogoUri { get; set; }
    public string? Description { get; set; }
    public DateTime FirstConnectedAt { get; set; }
    public List<GrantDetailDto> Grants { get; set; } = new();
    public List<ScopeInfoDto> Scopes { get; set; } = new();
}
