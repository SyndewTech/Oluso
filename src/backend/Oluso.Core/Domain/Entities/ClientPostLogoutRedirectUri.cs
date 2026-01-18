namespace Oluso.Core.Domain.Entities;

public class ClientPostLogoutRedirectUri
{
    public int Id { get; set; }
    public int ClientId { get; set; }
    public Client Client { get; set; } = default!;
    public string PostLogoutRedirectUri { get; set; } = default!;
}
