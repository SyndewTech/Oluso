using Oluso.Core.Domain.Entities;

namespace Oluso.Enterprise.Scim.Entities;

/// <summary>
/// Outbound attribute mapping for one provider: which internal user property
/// is written to which SCIM attribute path in the payload we push.
/// </summary>
public class ScimProviderAttributeMapping : TenantEntity
{
    public string Id { get; set; } = Guid.NewGuid().ToString();

    public string ScimProviderId { get; set; } = string.Empty;

    /// <summary>
    /// SCIM attribute path, e.g. <c>displayName</c>, <c>name.givenName</c>,
    /// <c>emails[type eq "work"].value</c>,
    /// <c>urn:ietf:params:scim:schemas:extension:enterprise:2.0:User:department</c>.
    /// </summary>
    public string ScimAttribute { get; set; } = string.Empty;

    /// <summary>
    /// Internal property name. See <see cref="Outbound.ScimOutboundProperties"/>;
    /// <c>custom:&lt;key&gt;</c> reads from the user's custom properties. Empty
    /// when <see cref="Expression"/> computes the value instead.
    /// </summary>
    public string InternalProperty { get; set; } = string.Empty;

    /// <summary>
    /// Scriban template that computes the value, e.g.
    /// <c>{{ user.first_name | string.downcase }}.{{ user.last_name | string.downcase }}</c>.
    /// Takes precedence over <see cref="InternalProperty"/>. The model exposes
    /// <c>user</c>, <c>roles</c>, <c>teams</c> and <c>custom</c>.
    /// </summary>
    public string? Expression { get; set; }

    /// <summary>
    /// Scriban boolean expression; when it evaluates false the attribute is
    /// left out of the payload, e.g. <c>roles | array.contains "Manager"</c>
    /// or <c>user.locale != ""</c>.
    /// </summary>
    public string? Condition { get; set; }

    /// <summary>
    /// Optional transformation applied to the value. See
    /// <see cref="Outbound.ScimOutboundTransformations"/>.
    /// </summary>
    public string? Transformation { get; set; }

    /// <summary>
    /// Sent when the internal value is empty. Null leaves the attribute out.
    /// </summary>
    public string? DefaultValue { get; set; }

    public bool IsEnabled { get; set; } = true;

    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

    public DateTime? UpdatedAt { get; set; }
}
