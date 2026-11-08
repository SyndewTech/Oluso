namespace Oluso.Admin.Dto.Clients;

public class ClientListDto
{
    public int Id { get; set; }
    public string ClientId { get; set; } = null!;
    public string? ClientName { get; set; }
    public string? Description { get; set; }
    public bool Enabled { get; set; }
    public List<string> AllowedGrantTypes { get; set; } = new();
    public bool RequireClientSecret { get; set; }
    public bool RequirePkce { get; set; }
    public DateTime Created { get; set; }
    public DateTime? Updated { get; set; }
}
