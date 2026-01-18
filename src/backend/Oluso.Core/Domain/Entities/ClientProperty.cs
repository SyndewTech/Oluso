namespace Oluso.Core.Domain.Entities;

public class ClientProperty
{
    public int Id { get; set; }
    public int ClientId { get; set; }
    public Client Client { get; set; } = default!;
    public string Key { get; set; } = default!;
    public string Value { get; set; } = default!;
}
