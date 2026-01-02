using Oluso.Core.Domain.Entities;

namespace Oluso.Enterprise.Scim.Entities;

/// <summary>
/// Maps external SCIM IDs to internal Oluso IDs
/// </summary>
public class ScimResourceMapping : TenantEntity
{
    public string Id { get; set; } = Guid.NewGuid().ToString();

    public string ScimClientId { get; set; } = string.Empty;

    /// <summary>
    /// Resource type (User, Group)
    /// </summary>
    public string ResourceType { get; set; } = string.Empty;

    /// <summary>
    /// External ID provided by the SCIM client
    /// </summary>
    public string ExternalId { get; set; } = string.Empty;

    /// <summary>
    /// Internal Oluso resource ID
    /// </summary>
    public string InternalId { get; set; } = string.Empty;

    /// <summary>
    /// When this mapping was created
    /// </summary>
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

    /// <summary>
    /// When this mapping was last updated
    /// </summary>
    public DateTime? UpdatedAt { get; set; }
}
