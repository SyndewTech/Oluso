using Microsoft.Extensions.Logging;
using Oluso.Core.Events;
using Oluso.Enterprise.Scim.Entities;
using Oluso.Enterprise.Scim.Stores;

namespace Oluso.Enterprise.Scim.Outbound;

/// <summary>
/// Fans a change out to every enabled provider of a tenant as one queue
/// item per provider.
/// </summary>
public interface IScimProviderEnqueuer
{
    /// <summary>
    /// Queues a user for every enabled provider that syncs users, except a provider
    /// linked to the inbound SCIM client <paramref name="origin"/> names. Returns how
    /// many items were queued.
    /// </summary>
    Task<int> EnqueueUserAsync(string tenantId, string userId, string operation, string reason, EventOrigin? origin = null, CancellationToken cancellationToken = default);

    /// <summary>Queues a role (as a group) for every enabled provider that syncs groups, with the same exception.</summary>
    Task<int> EnqueueGroupAsync(string tenantId, string roleId, string operation, string reason, EventOrigin? origin = null, CancellationToken cancellationToken = default);

    Task<ScimProviderOutboxItem> EnqueueForProviderAsync(ScimProvider provider, string resourceType, string internalId, string operation, string reason, CancellationToken cancellationToken = default);
}

public sealed class ScimProviderEnqueuer : IScimProviderEnqueuer
{
    public const int UserPriority = 0;
    public const int GroupPriority = 1;

    private readonly IScimProviderStore _providers;
    private readonly IScimProviderOutboxStore _outbox;
    private readonly ILogger<ScimProviderEnqueuer> _logger;

    public ScimProviderEnqueuer(
        IScimProviderStore providers,
        IScimProviderOutboxStore outbox,
        ILogger<ScimProviderEnqueuer> logger)
    {
        _providers = providers;
        _outbox = outbox;
        _logger = logger;
    }

    public async Task<int> EnqueueUserAsync(string tenantId, string userId, string operation, string reason, EventOrigin? origin = null, CancellationToken cancellationToken = default)
    {
        var providers = await _providers.GetEnabledByTenantAsync(tenantId, cancellationToken);
        var count = 0;
        foreach (var provider in providers.Where(p => p.SyncUsers && !IsEcho(p, origin, ScimResourceTypes.User, userId)))
        {
            await EnqueueForProviderAsync(provider, ScimResourceTypes.User, userId, operation, reason, cancellationToken);
            count++;
        }

        return count;
    }

    public async Task<int> EnqueueGroupAsync(string tenantId, string roleId, string operation, string reason, EventOrigin? origin = null, CancellationToken cancellationToken = default)
    {
        var providers = await _providers.GetEnabledByTenantAsync(tenantId, cancellationToken);
        var count = 0;
        foreach (var provider in providers.Where(p => p.SyncGroups && !IsEcho(p, origin, ScimResourceTypes.Group, roleId)))
        {
            await EnqueueForProviderAsync(provider, ScimResourceTypes.Group, roleId, operation, reason, cancellationToken);
            count++;
        }

        return count;
    }

    private bool IsEcho(ScimProvider provider, EventOrigin? origin, string resourceType, string internalId)
    {
        if (string.IsNullOrEmpty(provider.LinkedScimClientId)
            || origin?.Is(EventOriginKinds.Scim, provider.LinkedScimClientId) != true)
            return false;

        _logger.LogDebug("SCIM outbound skipped {ResourceType} {InternalId} for provider {ProviderId}: the change came from its linked inbound client {ScimClientId}",
            resourceType, internalId, provider.Id, provider.LinkedScimClientId);
        return true;
    }

    public async Task<ScimProviderOutboxItem> EnqueueForProviderAsync(ScimProvider provider, string resourceType, string internalId, string operation, string reason, CancellationToken cancellationToken = default)
    {
        var item = new ScimProviderOutboxItem
        {
            TenantId = provider.TenantId,
            ScimProviderId = provider.Id,
            ResourceType = resourceType,
            InternalId = internalId,
            Operation = operation,
            Priority = resourceType == ScimResourceTypes.Group ? GroupPriority : UserPriority,
            Reason = reason.Length > 200 ? reason[..200] : reason,
            NextAttemptAt = DateTime.UtcNow,
        };

        var stored = await _outbox.EnqueueAsync(item, cancellationToken);
        _logger.LogDebug("SCIM outbound queued {Operation} {ResourceType} {InternalId} for provider {ProviderId} ({Reason})",
            operation, resourceType, internalId, provider.Id, reason);
        return stored;
    }
}
