namespace Oluso.Core.Domain.Entities;

public class ClientScope
{
    public int Id { get; set; }
    public int ClientId { get; set; }
    public Client Client { get; set; } = default!;
    public string Scope { get; set; } = default!;
}
