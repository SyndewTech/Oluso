namespace Oluso.Core.Domain.Entities;

public class ClientClaim
{
    public int Id { get; set; }
    public int ClientId { get; set; }
    public Client Client { get; set; } = default!;
    public string Type { get; set; } = default!;
    public string Value { get; set; } = default!;
}
