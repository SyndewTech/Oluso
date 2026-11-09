namespace Oluso.Account.Dto.Sessions;

public class SessionDto
{
    public string Id { get; set; } = null!;
    public string? SessionId { get; set; }
    public string? ClientId { get; set; }
    public string? ClientName { get; set; }
    /// <summary>Applications with live tokens in this session, by display name.</summary>
    public List<string> Clients { get; set; } = new();
    public DateTime CreatedAt { get; set; }
    public DateTime? ExpiresAt { get; set; }
    public DateTime? LastActivityAt { get; set; }
    public string? IpAddress { get; set; }
    public string? UserAgent { get; set; }
    public string? Location { get; set; }
    public bool IsCurrent { get; set; }
}
