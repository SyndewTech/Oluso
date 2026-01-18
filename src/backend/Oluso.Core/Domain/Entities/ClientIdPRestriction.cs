namespace Oluso.Core.Domain.Entities;

public class ClientIdPRestriction
{
    public int Id { get; set; }
    public int ClientId { get; set; }
    public Client Client { get; set; } = default!;
    public string Provider { get; set; } = default!;
}
