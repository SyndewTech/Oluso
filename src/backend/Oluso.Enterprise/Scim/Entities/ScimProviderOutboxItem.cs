using Oluso.Core.Domain.Entities;

namespace Oluso.Enterprise.Scim.Entities;

/// <summary>
/// A unit of outbound provisioning work: "make the provider's copy of this
/// resource match ours" or "remove it". One row per provider, so a slow or
/// failing provider never blocks another. Processed by the outbound worker
/// with per-item retry and backoff.
/// </summary>
public class ScimProviderOutboxItem : TenantEntity
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
    /// See <see cref="ScimOutboxOperations"/>.
    /// </summary>
    public string Operation { get; set; } = ScimOutboxOperations.Upsert;

    /// <summary>
    /// See <see cref="ScimOutboxStatuses"/>.
    /// </summary>
    public string Status { get; set; } = ScimOutboxStatuses.Pending;

    /// <summary>
    /// Lower runs first within a provider. Users are 0, groups are 1 so a
    /// group's members already have remote ids when the group is pushed.
    /// </summary>
    public int Priority { get; set; }

    public int Attempts { get; set; }

    public DateTime NextAttemptAt { get; set; } = DateTime.UtcNow;

    /// <summary>
    /// Set while a worker holds the item. A lock past its expiry is treated
    /// as abandoned and the item goes back to pending.
    /// </summary>
    public DateTime? LockedUntil { get; set; }

    public string? LockedBy { get; set; }

    public int? LastStatusCode { get; set; }

    public string? LastError { get; set; }

    /// <summary>
    /// What produced this item, e.g. <c>event:user.updated</c> or <c>reconcile</c>.
    /// </summary>
    public string? Reason { get; set; }

    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

    public DateTime? UpdatedAt { get; set; }

    public DateTime? CompletedAt { get; set; }
}

public static class ScimOutboxOperations
{
    /// <summary>Create or update the remote resource so it matches ours.</summary>
    public const string Upsert = "upsert";

    /// <summary>
    /// Remove the resource from the provider according to its deprovision
    /// mode (deactivate or delete). Groups are always deleted.
    /// </summary>
    public const string Remove = "remove";
}

public static class ScimOutboxStatuses
{
    public const string Pending = "pending";
    public const string Processing = "processing";
    public const string Succeeded = "succeeded";

    /// <summary>Failed but will be retried at <see cref="ScimProviderOutboxItem.NextAttemptAt"/>.</summary>
    public const string Failed = "failed";

    /// <summary>Gave up: attempts exhausted or a non-retryable error.</summary>
    public const string Dead = "dead";

    /// <summary>Replaced by a newer item for the same resource before it ran.</summary>
    public const string Superseded = "superseded";

    public static readonly string[] Open = { Pending, Processing, Failed };
}
