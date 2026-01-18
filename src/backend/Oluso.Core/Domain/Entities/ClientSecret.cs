using System.Text.Json.Serialization;

namespace Oluso.Core.Domain.Entities;

// Client-related entities
public class ClientSecret
{
    public int Id { get; set; }
    public int ClientId { get; set; }
    public Client Client { get; set; } = default!;
    public string? Description { get; set; }

    /// <summary>
    /// Hashed secret value (SHA-256).
    /// SECURITY: Never expose this in API responses.
    /// </summary>
    [JsonIgnore]
    public string Value { get; set; } = default!;

    /// <summary>
    /// Last 3 characters of the original secret for identification purposes.
    /// Set when the secret is created, before hashing.
    /// </summary>
    public string? LastThreeChars { get; set; }

    public DateTime? Expiration { get; set; }
    public string Type { get; set; } = "SharedSecret";
    public DateTime Created { get; set; } = DateTime.UtcNow;
}
