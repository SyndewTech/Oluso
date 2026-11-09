using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Oluso.Core.Domain.Entities;
using Oluso.Core.Plugins;

namespace Oluso.EntityFramework.Stores;

/// <summary>
/// Entity Framework implementation of IPluginHookStore
/// </summary>
public class PluginHookStore : IPluginHookStore
{
    private readonly IOlusoDbContext _dbContext;
    private readonly ILogger<PluginHookStore> _logger;

    public PluginHookStore(IOlusoDbContext dbContext, ILogger<PluginHookStore> logger)
    {
        _dbContext = dbContext;
        _logger = logger;
    }

    public async Task<IReadOnlyList<PluginHookBinding>> GetActiveHooksAsync(
        string tenantId,
        string domain,
        string entityType,
        string eventType,
        CancellationToken cancellationToken = default)
    {
        return await _dbContext.PluginHookBindings
            .AsNoTracking()
            .Include(h => h.Plugin)
            .Where(h => h.TenantId == tenantId &&
                       h.Domain == domain &&
                       h.IsActive &&
                       h.Plugin != null &&
                       h.Plugin.Enabled &&
                       (h.EntityType == entityType || h.EntityType == "*") &&
                       h.EventType == eventType)
            .OrderBy(h => h.Order)
            .ToListAsync(cancellationToken);
    }

    public async Task<IReadOnlyList<PluginHookBinding>> GetHooksByPluginAsync(
        string tenantId,
        string pluginId,
        CancellationToken cancellationToken = default)
    {
        return await _dbContext.PluginHookBindings
            .AsNoTracking()
            .Where(h => h.TenantId == tenantId && h.PluginId == pluginId)
            .OrderBy(h => h.Domain)
            .ThenBy(h => h.EntityType)
            .ThenBy(h => h.EventType)
            .ThenBy(h => h.Order)
            .ToListAsync(cancellationToken);
    }

    public async Task<PluginHookBinding> CreateAsync(
        PluginHookBinding binding,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrEmpty(binding.Id))
        {
            binding.Id = Guid.NewGuid().ToString("N");
        }

        _dbContext.PluginHookBindings.Add(binding);
        await _dbContext.SaveChangesAsync(cancellationToken);

        _logger.LogInformation(
            "Created hook binding {Id} for plugin {PluginId} on {Domain}/{EntityType}/{EventType}",
            binding.Id, binding.PluginId, binding.Domain, binding.EntityType, binding.EventType);

        return binding;
    }

    public async Task<PluginHookBinding> UpdateAsync(
        PluginHookBinding binding,
        CancellationToken cancellationToken = default)
    {
        var existing = await _dbContext.PluginHookBindings
            .FirstOrDefaultAsync(h => h.Id == binding.Id && h.TenantId == binding.TenantId, cancellationToken);

        if (existing == null)
        {
            throw new InvalidOperationException($"Hook binding with ID '{binding.Id}' not found");
        }

        existing.Domain = binding.Domain;
        existing.EntityType = binding.EntityType;
        existing.EventType = binding.EventType;
        existing.FunctionName = binding.FunctionName;
        existing.Order = binding.Order;
        existing.IsActive = binding.IsActive;
        existing.FailureMode = binding.FailureMode;
        existing.TimeoutMs = binding.TimeoutMs;
        existing.FilterConditions = binding.FilterConditions;

        _dbContext.PluginHookBindings.Update(existing);
        await _dbContext.SaveChangesAsync(cancellationToken);

        _logger.LogDebug("Updated hook binding {Id}", binding.Id);

        return existing;
    }

    public async Task DeleteAsync(
        string tenantId,
        string id,
        CancellationToken cancellationToken = default)
    {
        var binding = await _dbContext.PluginHookBindings
            .FirstOrDefaultAsync(h => h.Id == id && h.TenantId == tenantId, cancellationToken);

        if (binding != null)
        {
            _dbContext.PluginHookBindings.Remove(binding);
            await _dbContext.SaveChangesAsync(cancellationToken);

            _logger.LogInformation("Deleted hook binding {Id}", id);
        }
    }
}
