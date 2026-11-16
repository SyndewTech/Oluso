namespace Oluso.Admin.Dto.Clients;

public class ClientSecretDto
{
    public int Id { get; set; }
    public string? Description { get; set; }
    public DateTime? Expiration { get; set; }
    public string Type { get; set; } = "SharedSecret";
    public DateTime Created { get; set; }
    public string? LastThreeChars { get; set; }

    /// <summary>A public key's identifier: a JWK's kid, a certificate's SHA-256 thumbprint. Null for a shared secret.</summary>
    public string? KeyId { get; set; }
}
