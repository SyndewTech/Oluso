namespace Oluso.Account.Dto.ConnectedApps;

public class ConnectedAppDto
{
    public string ClientId { get; set; } = null!;
    public string ClientName { get; set; } = null!;
    public string? ClientUri { get; set; }
    public string? LogoUri { get; set; }
    public string? Description { get; set; }
    public DateTime FirstConnectedAt { get; set; }
    public DateTime LastUsedAt { get; set; }
    public List<string> GrantTypes { get; set; } = new();
    public List<string> Scopes { get; set; } = new();
    public bool HasActiveTokens { get; set; }
}
