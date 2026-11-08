namespace Oluso.Core.Domain.Entities;

public class ClientGrantType : TenantEntity
{
    public int Id { get; set; }
    public int ClientId { get; set; }
    public Client Client { get; set; } = default!;
    public string GrantType { get; set; } = default!;
}
