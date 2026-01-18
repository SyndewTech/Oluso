namespace Oluso.Core.Domain.Entities;

public class ClientAllowedRole
{
    public int Id { get; set; }
    public int ClientId { get; set; }
    public Client Client { get; set; } = default!;
    public string Role { get; set; } = default!;
}
