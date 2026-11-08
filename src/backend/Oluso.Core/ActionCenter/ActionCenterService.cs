using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace Oluso.Core.ActionCenter;

/// <summary>
/// Aggregates action items from all registered providers.
///
/// Providers run in parallel via <see cref="Task.WhenAll(IEnumerable{Task})"/>,
/// each inside its own DI scope so each gets a fresh <c>DbContext</c>. Sharing
/// the request scope's DbContext across parallel providers triggers EF Core's
/// concurrency detector ("A second operation was started on this context
/// instance before a previous operation completed").
/// </summary>
public class ActionCenterService : IActionCenterService
{
    private readonly IEnumerable<IActionCenterProvider> _providers;
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly ILogger<ActionCenterService> _logger;

    public ActionCenterService(
        IEnumerable<IActionCenterProvider> providers,
        IServiceScopeFactory scopeFactory,
        ILogger<ActionCenterService> logger)
    {
        _providers = providers;
        _scopeFactory = scopeFactory;
        _logger = logger;
    }

    public async Task<ActionCenterResult> GetItemsForUserAsync(string tenantId, string userId, CancellationToken cancellationToken = default)
    {
        var providerIds = _providers.Select(p => p.ProviderId).ToList();

        var tasks = providerIds.Select(providerId => RunProviderAsync(providerId, tenantId, userId, cancellationToken));
        var groups = await Task.WhenAll(tasks);

        var result = new ActionCenterResult();
        foreach (var group in groups)
        {
            if (group != null && group.Count > 0)
            {
                result.Groups.Add(group);
                result.TotalCount += group.Count;
            }
        }
        return result;
    }

    public async Task<int> GetTotalCountAsync(string tenantId, string userId, CancellationToken cancellationToken = default)
    {
        var providerIds = _providers.Select(p => p.ProviderId).ToList();

        var tasks = providerIds.Select(providerId => RunCountAsync(providerId, tenantId, userId, cancellationToken));
        var counts = await Task.WhenAll(tasks);
        return counts.Sum();
    }

    private async Task<ActionCenterGroup?> RunProviderAsync(
        string providerId, string tenantId, string userId, CancellationToken cancellationToken)
    {
        using var scope = _scopeFactory.CreateScope();
        var provider = scope.ServiceProvider
            .GetServices<IActionCenterProvider>()
            .FirstOrDefault(p => p.ProviderId == providerId);
        if (provider == null) return null;

        try
        {
            var items = await provider.GetItemsForUserAsync(tenantId, userId, cancellationToken);
            if (items.Count == 0) return null;

            return new ActionCenterGroup
            {
                ProviderId = provider.ProviderId,
                DisplayName = provider.DisplayName,
                Icon = provider.Icon,
                WorkspaceActionUrl = provider.WorkspaceActionUrl,
                Count = items.Count,
                Items = items
                    .OrderByDescending(i => i.Priority)
                    .ThenBy(i => i.DueDate)
                    .ThenByDescending(i => i.CreatedAt)
                    .ToList(),
            };
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Action center provider {ProviderId} failed for user {UserId} in tenant {TenantId}",
                providerId, userId, tenantId);
            return null;
        }
    }

    private async Task<int> RunCountAsync(
        string providerId, string tenantId, string userId, CancellationToken cancellationToken)
    {
        using var scope = _scopeFactory.CreateScope();
        var provider = scope.ServiceProvider
            .GetServices<IActionCenterProvider>()
            .FirstOrDefault(p => p.ProviderId == providerId);
        if (provider == null) return 0;

        try
        {
            return await provider.GetCountForUserAsync(tenantId, userId, cancellationToken);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Action center provider {ProviderId} count failed for user {UserId} in tenant {TenantId}",
                providerId, userId, tenantId);
            return 0;
        }
    }
}
