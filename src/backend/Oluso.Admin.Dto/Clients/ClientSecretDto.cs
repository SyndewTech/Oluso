namespace Oluso.Admin.Dto.Clients;

public class ClientSecretDto
{
    public int Id { get; set; }
    public string? Description { get; set; }
    public DateTime? Expiration { get; set; }
    public string Type { get; set; } = "SharedSecret";
    public DateTime Created { get; set; }
    public string? LastThreeChars { get; set; }
}
