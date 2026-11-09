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
    /// The internal user property to write (e.g., "Email", "LastName",
    /// "custom:department"). See <see cref="Services.ScimInboundTargets"/>.
    /// </summary>
    public string InternalProperty { get; set; } = string.Empty;

    /// <summary>
    /// Scriban template that computes the value instead of reading
    /// <see cref="ScimAttribute"/> directly, e.g.
    /// <c>{{ scim.name.givenName }} {{ scim.name.familyName }}</c> or
    /// <c>{{ attr "urn:ietf:params:scim:schemas:extension:enterprise:2.0:User:department" }}</c>.
    /// The model exposes <c>scim</c>, <c>enterprise</c>, <c>attr</c> and <c>client</c>.
    /// </summary>
    public string? Expression { get; set; }

    /// <summary>
    /// Scriban boolean expression; when it evaluates false this mapping is
    /// skipped, e.g. <c>scim.userType == "Employee"</c>.
    /// </summary>
    public string? Condition { get; set; }

    /// <summary>
    /// Whether this mapping must resolve to a value when a user is created.
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
    /// When several mappings write the same internal property, they are
    /// tried from the highest priority down and the first that yields a
    /// value wins. Lets a fallback (e.g. name parts) sit behind a preferred
    /// attribute (displayName).
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
