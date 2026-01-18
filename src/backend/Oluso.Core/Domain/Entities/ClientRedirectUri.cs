namespace Oluso.Core.Domain.Entities;

public class ClientRedirectUri
{
    public int Id { get; set; }
    public int ClientId { get; set; }
    public Client Client { get; set; } = default!;
    public string RedirectUri { get; set; } = default!;
}
