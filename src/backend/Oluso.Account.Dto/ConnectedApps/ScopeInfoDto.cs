namespace Oluso.Account.Dto.ConnectedApps;

public class ScopeInfoDto
{
    public string Name { get; set; } = null!;
    public string? DisplayName { get; set; }
    public string? Description { get; set; }
    public bool Required { get; set; }
}
