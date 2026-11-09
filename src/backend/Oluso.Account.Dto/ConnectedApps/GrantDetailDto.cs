namespace Oluso.Account.Dto.ConnectedApps;

public class GrantDetailDto
{
    public string Type { get; set; } = null!;
    public DateTime CreatedAt { get; set; }
    public DateTime? ExpiresAt { get; set; }
    public string? SessionId { get; set; }
}
