using Oluso.Enterprise.Scim.Entities;

namespace Oluso.Enterprise.Scim.Stores;

public interface IScimProviderStore
{
    Task<ScimProvider?> GetByIdAsync(string id, CancellationToken cancellationToken = default);
    Task<IReadOnlyList<ScimProvider>> GetByTenantAsync(string tenantId, CancellationToken cancellationToken = default);
    Task<IReadOnlyList<ScimProvider>> GetEnabledByTenantAsync(string tenantId, CancellationToken cancellationToken = default);

    /// <summary>
    /// Enabled providers whose scheduled reconcile is due. Cross-tenant; used by the worker.
    /// </summary>
    Task<IReadOnlyList<ScimProvider>> GetReconcileDueAsync(DateTime now, CancellationToken cancellationToken = default);

    Task<ScimProvider> CreateAsync(ScimProvider provider, CancellationToken cancellationToken = default);
    Task UpdateAsync(ScimProvider provider, CancellationToken cancellationToken = default);

    /// <summary>
    /// Deletes the provider and everything keyed to it: resource mappings,
    /// attribute mappings, queue items and logs.
    /// </summary>
    Task DeleteAsync(string id, CancellationToken cancellationToken = default);

    Task RecordOutcomeAsync(string id, bool success, string? error, CancellationToken cancellationToken = default);
}

public interface IScimProviderResourceMappingStore
{
    Task<ScimProviderResourceMapping?> GetByInternalIdAsync(string scimProviderId, string resourceType, string internalId, CancellationToken cancellationToken = default);
    Task<IReadOnlyDictionary<string, ScimProviderResourceMapping>> GetByInternalIdsAsync(string scimProviderId, string resourceType, IReadOnlyCollection<string> internalIds, CancellationToken cancellationToken = default);
    Task<ScimProviderResourceMapping?> GetByRemoteIdAsync(string scimProviderId, string resourceType, string remoteId, CancellationToken cancellationToken = default);
    Task<IReadOnlyList<ScimProviderResourceMapping>> GetByProviderAsync(string scimProviderId, string? resourceType, int skip, int take, CancellationToken cancellationToken = default);
    Task<int> GetCountByProviderAsync(string scimProviderId, string? resourceType, CancellationToken cancellationToken = default);
    Task<IReadOnlyList<ScimProviderResourceMapping>> GetAllByProviderAsync(string scimProviderId, string resourceType, CancellationToken cancellationToken = default);
    Task<ScimProviderResourceMapping> CreateAsync(ScimProviderResourceMapping mapping, CancellationToken cancellationToken = default);
    Task UpdateAsync(ScimProviderResourceMapping mapping, CancellationToken cancellationToken = default);
    Task DeleteAsync(string id, CancellationToken cancellationToken = default);
}

public interface IScimProviderAttributeMappingStore
{
    Task<ScimProviderAttributeMapping?> GetByIdAsync(string id, CancellationToken cancellationToken = default);
    Task<IReadOnlyList<ScimProviderAttributeMapping>> GetByProviderAsync(string scimProviderId, CancellationToken cancellationToken = default);
    Task<ScimProviderAttributeMapping> CreateAsync(ScimProviderAttributeMapping mapping, CancellationToken cancellationToken = default);
    Task UpdateAsync(ScimProviderAttributeMapping mapping, CancellationToken cancellationToken = default);
    Task DeleteAsync(string id, CancellationToken cancellationToken = default);
}

public sealed class ScimOutboxSummary
{
    public int Pending { get; init; }
    public int Processing { get; init; }
    public int Failed { get; init; }
    public int Dead { get; init; }
    public int SucceededLast24h { get; init; }
    public DateTime? OldestPendingAt { get; init; }
}

public interface IScimProviderOutboxStore
{
    /// <summary>
    /// Adds an item unless an open item for the same provider/resource/operation
    /// already exists. An open item with a different operation is superseded.
    /// Returns the item that now represents the work (new or existing).
    /// </summary>
    Task<ScimProviderOutboxItem> EnqueueAsync(ScimProviderOutboxItem item, CancellationToken cancellationToken = default);

    /// <summary>
    /// Provider ids that have at least one runnable item.
    /// </summary>
    Task<IReadOnlyList<string>> GetDueProviderIdsAsync(DateTime now, int limit, CancellationToken cancellationToken = default);

    /// <summary>
    /// Atomically takes the next runnable item for a provider. Null when none.
    /// Safe across multiple worker instances: the claim is a conditional update.
    /// </summary>
    Task<ScimProviderOutboxItem?> ClaimNextAsync(string scimProviderId, string workerId, DateTime now, TimeSpan lockDuration, CancellationToken cancellationToken = default);

    Task MarkSucceededAsync(string id, int? statusCode, CancellationToken cancellationToken = default);
    Task MarkFailedAsync(string id, int? statusCode, string error, DateTime nextAttemptAt, CancellationToken cancellationToken = default);
    Task MarkDeadAsync(string id, int? statusCode, string error, CancellationToken cancellationToken = default);

    /// <summary>
    /// Puts items whose lock expired back to pending. Returns how many.
    /// </summary>
    Task<int> ReleaseExpiredLocksAsync(DateTime now, CancellationToken cancellationToken = default);

    Task<ScimOutboxSummary> GetSummaryAsync(string scimProviderId, CancellationToken cancellationToken = default);
    Task<IReadOnlyList<ScimProviderOutboxItem>> GetByProviderAsync(string scimProviderId, string[]? statuses, int skip, int take, CancellationToken cancellationToken = default);
    Task<int> GetCountByProviderAsync(string scimProviderId, string[]? statuses, CancellationToken cancellationToken = default);

    /// <summary>
    /// Resets failed and dead items of a provider to pending, due now. Returns how many.
    /// </summary>
    Task<int> RetryFailedAsync(string scimProviderId, CancellationToken cancellationToken = default);

    Task<int> DeleteCompletedOlderThanAsync(DateTime cutoff, CancellationToken cancellationToken = default);
}

public interface IScimProviderLogStore
{
    Task<ScimProviderLog> CreateAsync(ScimProviderLog log, CancellationToken cancellationToken = default);
    Task<IReadOnlyList<ScimProviderLog>> GetByProviderAsync(string scimProviderId, bool? success, int skip, int take, CancellationToken cancellationToken = default);
    Task<int> GetCountByProviderAsync(string scimProviderId, bool? success, CancellationToken cancellationToken = default);
    Task DeleteOlderThanAsync(DateTime cutoff, CancellationToken cancellationToken = default);
}
