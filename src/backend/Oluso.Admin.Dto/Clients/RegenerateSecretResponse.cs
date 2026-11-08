namespace Oluso.Admin.Dto.Clients;

public class RegenerateSecretResponse
{
    public string ClientId { get; set; } = null!;
    public string ClientSecret { get; set; } = null!;
    public int SecretId { get; set; }
}
