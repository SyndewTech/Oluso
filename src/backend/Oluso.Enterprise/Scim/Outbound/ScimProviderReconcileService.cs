using Microsoft.Extensions.Logging;
using Oluso.Core.Services;
using Oluso.Enterprise.Scim.Entities;
using Oluso.Enterprise.Scim.Stores;

namespace Oluso.Enterprise.Scim.Outbound;

public sealed class ScimReconcileSummary
{
    public int UsersQueued { get; set; }
    public int UserRemovalsQueued { get; set; }
    public int GroupsQueued { get; set; }
    public int GroupRemovalsQueued { get; set; }

    public override string ToString() =>
        $"users: {UsersQueued} queued, {UserRemovalsQueued} to remove; groups: {GroupsQueued} queued, {GroupRemovalsQueued} to remove";
}

/// <summary>
/// Queues a full pass over everything a provider should hold: every in-scope
/// user and group (groups also as its <see cref="IScimGroupFilter"/>s allow) is
/// queued for upsert, and every remote mapping whose subject is no longer in
/// scope is queued for removal. Events keep the
/// provider current between passes; this catches whatever events missed.
/// Must run inside a scope whose tenant context is the provider's tenant.
/// </summary>
public interface IScimProviderReconcileService
{
    Task<ScimReconcileSummary> ReconcileAsync(ScimProvider provider, CancellationToken cancellationToken = default);
}

public sealed class ScimProviderReconcileService : IScimProviderReconcileService
{
    private readonly IOlusoUserService _users;
    private readonly ScimGroupSourceRegistry _groups;
    private readonly IScimProviderResourceMappingStore _mappings;
    private readonly IScimProviderEnqueuer _enqueuer;
    private readonly IScimProviderStore _providers;
    private readonly ScimOptions _options;
    private readonly ILogger<ScimProviderReconcileService> _logger;

    public ScimProviderReconcileService(
        IOlusoUserService users,
        ScimGroupSourceRegistry groups,
        IScimProviderResourceMappingStore mappings,
        IScimProviderEnqueuer enqueuer,
        IScimProviderStore providers,
        ScimOptions options,
        ILogger<ScimProviderReconcileService> logger)
    {
        _users = users;
        _groups = groups;
        _mappings = mappings;
        _enqueuer = enqueuer;
        _providers = providers;
        _options = options;
        _logger = logger;
    }

    public async Task<ScimReconcileSummary> ReconcileAsync(ScimProvider provider, CancellationToken cancellationToken = default)
    {
        var tenantId = provider.TenantId ?? throw new InvalidOperationException($"Provider {provider.Id} has no tenant");
        var summary = new ScimReconcileSummary();

        if (provider.SyncUsers)
        {
            var targetUsers = await CollectTargetUsersAsync(provider, tenantId, cancellationToken);

            foreach (var userId in targetUsers)
            {
                await _enqueuer.EnqueueForProviderAsync(provider, ScimResourceTypes.User, userId, ScimOutboxOperations.Upsert, "reconcile", cancellationToken);
                summary.UsersQueued++;
            }

            var mapped = await _mappings.GetAllByProviderAsync(provider.Id, ScimResourceTypes.User, cancellationToken);
            foreach (var mapping in mapped.Where(m => !m.IsDeprovisioned && !targetUsers.Contains(m.InternalId)))
            {
                await _enqueuer.EnqueueForProviderAsync(provider, ScimResourceTypes.User, mapping.InternalId, ScimOutboxOperations.Remove, "reconcile:out-of-scope", cancellationToken);
                summary.UserRemovalsQueued++;
            }
        }

        if (provider.SyncGroups)
        {
            var targetGroups = new HashSet<string>(StringComparer.Ordinal);
            foreach (var source in _groups.EnabledFor(provider))
            {
                var scope = ScimGroupSourceRegistry.ScopeFor(provider, source.Kind);
                var allowed = await _groups.FilterAsync(provider, source.Kind, cancellationToken);
                var groups = await source.ListAsync(tenantId, cancellationToken);
                foreach (var group in groups.Where(g => (scope.Count == 0 || scope.Contains(g.Id, StringComparer.Ordinal))
                                                        && (allowed == null || allowed.Contains(g.Id))))
                    targetGroups.Add(group.Key);
            }

            foreach (var key in targetGroups)
            {
                await _enqueuer.EnqueueForProviderAsync(provider, ScimResourceTypes.Group, key, ScimOutboxOperations.Upsert, "reconcile", cancellationToken);
                summary.GroupsQueued++;
            }

            var mapped = await _mappings.GetAllByProviderAsync(provider.Id, ScimResourceTypes.Group, cancellationToken);
            foreach (var mapping in mapped.Where(m => !targetGroups.Contains(m.InternalId)))
            {
                await _enqueuer.EnqueueForProviderAsync(provider, ScimResourceTypes.Group, mapping.InternalId, ScimOutboxOperations.Remove, "reconcile:out-of-scope", cancellationToken);
                summary.GroupRemovalsQueued++;
            }
        }

        provider.LastReconcileAt = DateTime.UtcNow;
        provider.LastReconcileSummary = summary.ToString();
        await _providers.UpdateAsync(provider, cancellationToken);

        _logger.LogInformation("SCIM provider {ProviderId} reconcile queued: {Summary}", provider.Id, summary);
        return summary;
    }

    /// <summary>
    /// Users whose home tenant is this tenant. Members granted access from
    /// another home tenant are not provisioned: their identity belongs to
    /// the other tenant and its providers. With a scope, the set is the
    /// union of the scope roles' members and the scope teams' effective members.
    /// </summary>
    private async Task<HashSet<string>> CollectTargetUsersAsync(ScimProvider provider, string tenantId, CancellationToken cancellationToken)
    {
        var target = new HashSet<string>(StringComparer.Ordinal);

        if (provider.HasUserScope)
        {
            foreach (var roleId in provider.GetScopeRoleIds())
                target.UnionWith(await _groups.Get(ScimGroupKinds.Role).GetMemberUserIdsAsync(tenantId, roleId, cancellationToken));

            foreach (var teamId in provider.GetScopeTeamIds())
                target.UnionWith(await _groups.Get(ScimGroupKinds.Team).GetMemberUserIdsAsync(tenantId, teamId, cancellationToken));

            return target;
        }

        var page = 1;
        while (true)
        {
            var result = await _users.GetUsersAsync(new UsersQuery
            {
                TenantId = tenantId,
                Page = page,
                PageSize = _options.Outbound.ReconcilePageSize,
            }, cancellationToken);

            target.UnionWith(result.Users
                .Where(u => string.Equals(u.TenantId, tenantId, StringComparison.Ordinal))
                .Select(u => u.Id));

            if (result.Users.Count == 0 || page >= result.TotalPages)
                break;
            page++;
        }

        return target;
    }
}
