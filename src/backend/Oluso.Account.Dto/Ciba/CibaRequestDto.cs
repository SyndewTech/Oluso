namespace Oluso.Account.Dto.Ciba;

public class CibaRequestDto
{
    public string AuthReqId { get; set; } = null!;
    public string ClientId { get; set; } = null!;
    public string? ClientName { get; set; }
    public string? ClientLogoUri { get; set; }
    public string? BindingMessage { get; set; }
    public List<string> RequestedScopes { get; set; } = new();
    public string Status { get; set; } = null!;
    public DateTime CreatedAt { get; set; }
    public DateTime ExpiresAt { get; set; }
}
