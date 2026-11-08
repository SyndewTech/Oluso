namespace Oluso.Core.Domain.Entities;

/// <summary>
/// A permission defined by a tenant for their own applications.
///
/// Oluso attaches no meaning to these — they never satisfy a platform gate and
/// are never consulted by PermissionAuthorizationHandler. They exist so a tenant
/// can model their app's authorization in the same place they model their roles,
/// and have it delivered in the token their app already validates.
///
/// The catalog is what makes the role editor a picker rather than a free-text
/// box: assigning an entitlement to a role is validated against these rows, so a
/// typo fails at save instead of silently never matching in the tenant's app.
/// </summary>
public class Entitlement : AuditableTenantEntity
{
    public string Id { get; set; } = Guid.NewGuid().ToString();

    /// <summary>
    /// The value carried in the token's <c>entitlements</c> claim
    /// (e.g. <c>invoices.approve</c>). Unique per tenant.
    /// </summary>
    public string Name { get; set; } = string.Empty;

    /// <summary>
    /// Label shown in the role editor's picker.
    /// </summary>
    public string DisplayName { get; set; } = string.Empty;

    /// <summary>
    /// What the entitlement lets a user do, shown under the label.
    /// </summary>
    public string? Description { get; set; }

    /// <summary>
    /// Optional grouping for the picker (e.g. "Invoices", "Reports"). Entries
    /// with no group are shown together under a default heading.
    /// </summary>
    public string? Group { get; set; }

    /// <summary>
    /// Disabled entitlements stay assigned to roles but are withheld from tokens,
    /// so a tenant can retire one without editing every role first.
    /// </summary>
    public bool Enabled { get; set; } = true;
}
