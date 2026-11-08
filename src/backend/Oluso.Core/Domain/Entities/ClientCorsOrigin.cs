namespace Oluso.Core.Domain.Entities;

public class ClientCorsOrigin : TenantEntity
{
    public int Id { get; set; }
    public int ClientId { get; set; }
    public Client Client { get; set; } = default!;
    public string Origin { get; set; } = default!;
}
