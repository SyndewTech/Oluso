namespace Oluso.Admin.Dto.Entitlements;

/// <summary>
/// An entitlement in the tenant's catalog — a permission the tenant's own
/// applications enforce, assignable to roles and delivered in the
/// <c>entitlements</c> claim.
/// </summary>
public class EntitlementDto
{
    public string Id { get; set; } = null!;

    /// <summary>Value carried in the token (e.g. <c>invoices.approve</c>).</summary>
    public string Name { get; set; } = null!;

    public string DisplayName { get; set; } = null!;
    public string? Description { get; set; }
    public string? Group { get; set; }
    public bool Enabled { get; set; }
    public DateTime CreatedAt { get; set; }
    public DateTime? UpdatedAt { get; set; }
}

public class CreateEntitlementRequest
{
    public string Name { get; set; } = null!;
    public string? DisplayName { get; set; }
    public string? Description { get; set; }
    public string? Group { get; set; }
    public bool? Enabled { get; set; }
}

public class UpdateEntitlementRequest
{
    /// <summary>
    /// Renaming changes the value already issued in live tokens and stored on
    /// roles, so the endpoint rejects it — delete and recreate instead.
    /// </summary>
    public string? DisplayName { get; set; }
    public string? Description { get; set; }
    public string? Group { get; set; }
    public bool? Enabled { get; set; }
}
