using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Oluso.Core.Domain.Entities;
using Oluso.Core.Plugins;

namespace Oluso.EntityFramework.Stores;

/// <summary>
/// Entity Framework implementation of IPluginExecutionLogger
/// </summary>
public class PluginExecutionLogStore : IPluginExecutionLogger
{
    private readonly IOlusoDbContext _dbContext;
    private readonly ILogger<PluginExecutionLogStore> _logger;

    public PluginExecutionLogStore(IOlusoDbContext dbContext, ILogger<PluginExecutionLogStore> logger)
    {
        _dbContext = dbContext;
        _logger = logger;
    }

    public async Task LogExecutionAsync(
        PluginExecutionLog log,
        CancellationToken cancellationToken = default)
    {
        try
        {
            if (string.IsNullOrEmpty(log.Id))
            {
                log.Id = Guid.NewGuid().ToString("N");
            }

            _dbContext.PluginExecutionLogs.Add(log);
            await _dbContext.SaveChangesAsync(cancellationToken);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to log plugin execution for {PluginId}", log.PluginId);
            // Don't throw - logging should not break the application
        }
    }

    public async Task<IReadOnlyList<PluginExecutionLog>> GetRecentExecutionsAsync(
        string tenantId,
        string pluginId,
        int limit = 100,
        CancellationToken cancellationToken = default)
    {
        return await _dbContext.PluginExecutionLogs
            .AsNoTracking()
            .Where(l => l.TenantId == tenantId && l.PluginId == pluginId)
            .OrderByDescending(l => l.ExecutedAt)
            .Take(limit)
            .ToListAsync(cancellationToken);
    }

    public async Task<IReadOnlyList<PluginExecutionLog>> GetExecutionsByIdAsync(
        string tenantId,
        string executionId,
        CancellationToken cancellationToken = default)
    {
        return await _dbContext.PluginExecutionLogs
            .AsNoTracking()
            .Where(l => l.TenantId == tenantId && l.ExecutionId == executionId)
            .OrderBy(l => l.ExecutedAt)
            .ToListAsync(cancellationToken);
    }
}

/// <summary>
/// Store for managing plugin network configurations
/// </summary>
public class PluginNetworkConfigStore
{
    private readonly IOlusoDbContext _dbContext;
    private readonly ILogger<PluginNetworkConfigStore> _logger;

    public PluginNetworkConfigStore(IOlusoDbContext dbContext, ILogger<PluginNetworkConfigStore> logger)
    {
        _dbContext = dbContext;
        _logger = logger;
    }

    public async Task<PluginNetworkConfig?> GetByPluginIdAsync(
        string tenantId,
        string pluginId,
        CancellationToken cancellationToken = default)
    {
        return await _dbContext.PluginNetworkConfigs
            .AsNoTracking()
            .FirstOrDefaultAsync(c => c.TenantId == tenantId && c.PluginId == pluginId, cancellationToken);
    }

    public async Task<PluginNetworkConfig> CreateOrUpdateAsync(
        PluginNetworkConfig config,
        CancellationToken cancellationToken = default)
    {
        var existing = await _dbContext.PluginNetworkConfigs
            .FirstOrDefaultAsync(c => c.TenantId == config.TenantId && c.PluginId == config.PluginId, cancellationToken);

        if (existing != null)
        {
            existing.AccessMode = config.AccessMode;
            existing.AllowedHosts = config.AllowedHosts;
            existing.HttpTimeoutMs = config.HttpTimeoutMs;
            existing.MaxHttpRequestsPerExecution = config.MaxHttpRequestsPerExecution;
            existing.RequiresApproval = config.RequiresApproval;
            existing.IsApproved = config.IsApproved;
            existing.ApprovedBy = config.ApprovedBy;
            existing.ApprovedAt = config.ApprovedAt;

            _dbContext.PluginNetworkConfigs.Update(existing);
            await _dbContext.SaveChangesAsync(cancellationToken);
            return existing;
        }
        else
        {
            if (string.IsNullOrEmpty(config.Id))
            {
                config.Id = Guid.NewGuid().ToString("N");
            }

            _dbContext.PluginNetworkConfigs.Add(config);
            await _dbContext.SaveChangesAsync(cancellationToken);

            _logger.LogInformation("Created network config for plugin {PluginId}", config.PluginId);
            return config;
        }
    }

    public async Task DeleteAsync(
        string tenantId,
        string pluginId,
        CancellationToken cancellationToken = default)
    {
        var config = await _dbContext.PluginNetworkConfigs
            .FirstOrDefaultAsync(c => c.TenantId == tenantId && c.PluginId == pluginId, cancellationToken);

        if (config != null)
        {
            _dbContext.PluginNetworkConfigs.Remove(config);
            await _dbContext.SaveChangesAsync(cancellationToken);
        }
    }
}

/// <summary>
/// Store for managing plugin configuration overrides
/// </summary>
public class PluginConfigOverrideStore
{
    private readonly IOlusoDbContext _dbContext;
    private readonly ILogger<PluginConfigOverrideStore> _logger;

    public PluginConfigOverrideStore(IOlusoDbContext dbContext, ILogger<PluginConfigOverrideStore> logger)
    {
        _dbContext = dbContext;
        _logger = logger;
    }

    public async Task<PluginConfigOverride?> GetByPluginIdAsync(
        string tenantId,
        string pluginId,
        CancellationToken cancellationToken = default)
    {
        return await _dbContext.PluginConfigOverrides
            .AsNoTracking()
            .FirstOrDefaultAsync(c => c.TenantId == tenantId && c.PluginId == pluginId, cancellationToken);
    }

    public async Task<PluginConfigOverride> CreateOrUpdateAsync(
        PluginConfigOverride config,
        CancellationToken cancellationToken = default)
    {
        var existing = await _dbContext.PluginConfigOverrides
            .FirstOrDefaultAsync(c => c.TenantId == config.TenantId && c.PluginId == config.PluginId, cancellationToken);

        if (existing != null)
        {
            existing.ConfigJson = config.ConfigJson;
            existing.SecretsJson = config.SecretsJson;
            existing.UpdatedAt = DateTime.UtcNow;
            existing.UpdatedBy = config.UpdatedBy;

            _dbContext.PluginConfigOverrides.Update(existing);
            await _dbContext.SaveChangesAsync(cancellationToken);
            return existing;
        }
        else
        {
            if (string.IsNullOrEmpty(config.Id))
            {
                config.Id = Guid.NewGuid().ToString("N");
            }

            _dbContext.PluginConfigOverrides.Add(config);
            await _dbContext.SaveChangesAsync(cancellationToken);

            _logger.LogInformation("Created config override for plugin {PluginId}", config.PluginId);
            return config;
        }
    }

    public async Task DeleteAsync(
        string tenantId,
        string pluginId,
        CancellationToken cancellationToken = default)
    {
        var config = await _dbContext.PluginConfigOverrides
            .FirstOrDefaultAsync(c => c.TenantId == tenantId && c.PluginId == pluginId, cancellationToken);

        if (config != null)
        {
            _dbContext.PluginConfigOverrides.Remove(config);
            await _dbContext.SaveChangesAsync(cancellationToken);
        }
    }
}
