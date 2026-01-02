using Oluso.Core.Domain.Entities;

namespace Oluso.Enterprise.Scim.Entities;

/// <summary>
/// Maps SCIM attributes to internal user properties for a specific client
/// </summary>
public class ScimAttributeMapping : TenantEntity
{
    public string Id { get; set; } = Guid.NewGuid().ToString();

    public string ScimClientId { get; set; } = string.Empty;

    /// <summary>
    /// The SCIM attribute path (e.g., "userName", "emails[type eq \"work\"].value", "name.familyName")
    /// </summary>
    public string ScimAttribute { get; set; } = string.Empty;

    /// <summary>
    /// The internal user property to map to (e.g., "Email", "LastName", "Department")
    /// </summary>
    public string InternalProperty { get; set; } = string.Empty;

    /// <summary>
    /// Direction of the mapping: inbound (SCIM -> internal), outbound (internal -> SCIM), or both
    /// </summary>
    public string Direction { get; set; } = "inbound";

    /// <summary>
    /// Whether this mapping is required for user creation
    /// </summary>
    public bool IsRequired { get; set; }

    /// <summary>
    /// Default value if the SCIM attribute is not provided
    /// </summary>
    public string? DefaultValue { get; set; }

    /// <summary>
    /// Optional transformation function name to apply
    /// </summary>
    public string? Transformation { get; set; }

    /// <summary>
    /// Priority for conflict resolution (higher = more priority)
    /// </summary>
    public int Priority { get; set; }

    /// <summary>
    /// Whether this mapping is enabled
    /// </summary>
    public bool IsEnabled { get; set; } = true;

    /// <summary>
    /// When this mapping was created
    /// </summary>
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

    /// <summary>
    /// When this mapping was last updated
    /// </summary>
    public DateTime? UpdatedAt { get; set; }
}
