using Oluso.Core.Domain.Entities;

namespace Oluso.Enterprise.Scim.Entities;

/// <summary>
/// Links an internal resource (user or role) to the id the downstream
/// provider assigned to it. The mirror image of <see cref="ScimResourceMapping"/>,
/// which links an upstream IdP's external id to our internal id.
/// </summary>
public class ScimProviderResourceMapping : TenantEntity
{
    public string Id { get; set; } = Guid.NewGuid().ToString();

    public string ScimProviderId { get; set; } = string.Empty;

    /// <summary>
    /// User or Group. See <see cref="ScimResourceTypes"/>.
    /// </summary>
    public string ResourceType { get; set; } = string.Empty;

    /// <summary>
    /// Our user id or role id.
    /// </summary>
    public string InternalId { get; set; } = string.Empty;

    /// <summary>
    /// The provider's resource id (the <c>id</c> attribute it returned).
    /// </summary>
    public string RemoteId { get; set; } = string.Empty;

    /// <summary>
    /// The provider's <c>meta.version</c> from the last response, when it sends one.
    /// </summary>
    public string? RemoteVersion { get; set; }

    /// <summary>
    /// Hash of the last payload pushed, so an unchanged user is not re-sent.
    /// </summary>
    public string? PayloadHash { get; set; }

    /// <summary>
    /// True after a deactivate-mode deprovision; the remote record still
    /// exists but is inactive.
    /// </summary>
    public bool IsDeprovisioned { get; set; }

    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

    public DateTime? UpdatedAt { get; set; }

    public DateTime? LastSyncedAt { get; set; }
}
