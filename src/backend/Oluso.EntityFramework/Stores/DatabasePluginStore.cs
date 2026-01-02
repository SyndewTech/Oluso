using System.Security.Cryptography;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Oluso.Core.Domain.Entities;
using Oluso.Core.Domain.Interfaces;
using Oluso.Core.Storage;

namespace Oluso.EntityFramework.Stores;

/// <summary>
/// Database-backed plugin store.
/// Metadata is stored in the database, plugin bytes are stored using IFileUploader.
/// This provides queryable metadata, execution stats, and flexible storage backends.
/// </summary>
public class DatabasePluginStore : IPluginStore
{
    private readonly IOlusoDbContext _dbContext;
    private readonly IFileUploader _fileUploader;
    private readonly ILogger<DatabasePluginStore> _logger;
    private readonly ITenantContext? _tenantContext;

    public DatabasePluginStore(
        IOlusoDbContext dbContext,
        IFileUploader fileUploader,
        ILogger<DatabasePluginStore> logger,
        ITenantContext? tenantContext = null)
    {
        _dbContext = dbContext;
        _fileUploader = fileUploader;
        _logger = logger;
        _tenantContext = tenantContext;
    }

    public async Task<IEnumerable<PluginMetadata>> GetAvailablePluginsAsync(
        string? tenantId,
        CancellationToken cancellationToken = default)
    {
        return await _dbContext.PluginMetadata
            .AsNoTracking()
            .Where(p => p.Enabled)
            .OrderBy(p => p.Name)
            .ToListAsync(cancellationToken);
    }

    public async Task<PluginMetadata?> GetPluginInfoAsync(
        string pluginName,
        string? tenantId,
        CancellationToken cancellationToken = default)
    {
        // Order by TenantId descending so tenant-specific (non-null) comes before global (null)
        return await _dbContext.PluginMetadata
            .AsNoTracking()
            .Where(p => p.Name == pluginName)
            .OrderByDescending(p => p.TenantId)
            .FirstOrDefaultAsync(cancellationToken);
    }

    public async Task<PluginMetadata?> GetByIdAsync(
        string id,
        CancellationToken cancellationToken = default)
    {
        return await _dbContext.PluginMetadata
            .AsNoTracking()
            .FirstOrDefaultAsync(p => p.Id == id, cancellationToken);
    }

    public async Task<byte[]?> GetPluginBytesAsync(
        string pluginName,
        string? tenantId,
        CancellationToken cancellationToken = default)
    {
        var metadata = await GetPluginInfoAsync(pluginName, tenantId, cancellationToken);

        if (metadata == null)
        {
            return null;
        }

        return await _fileUploader.DownloadAsync(metadata.StorageReference, cancellationToken);
    }

    public async Task<PluginMetadata> SavePluginAsync(
        string pluginName,
        byte[] wasmBytes,
        PluginMetadata metadata,
        string? tenantId,
        CancellationToken cancellationToken = default)
    {
        // Compute content hash
        using var sha256 = SHA256.Create();
        var hashBytes = sha256.ComputeHash(wasmBytes);
        var contentHash = Convert.ToHexString(hashBytes).ToLowerInvariant();

        // Upload bytes to file storage
        var storagePath = GetStoragePath(pluginName, tenantId);
        var uploadResult = await _fileUploader.UploadAsync(
            storagePath,
            wasmBytes,
            "application/wasm",
            cancellationToken: cancellationToken);

        if (!uploadResult.Success)
        {
            throw new InvalidOperationException($"Failed to upload plugin bytes: {uploadResult.Error}");
        }

        // Check if updating or creating
        var existing = await _dbContext.PluginMetadata
            .FirstOrDefaultAsync(p => p.Name == pluginName && p.TenantId == tenantId, cancellationToken);

        if (existing != null)
        {
            // Update existing
            existing.DisplayName = metadata.DisplayName;
            existing.Description = metadata.Description;
            existing.Version = metadata.Version;
            existing.Author = metadata.Author;
            existing.StorageReference = uploadResult.StorageReference;
            existing.StorageProvider = _fileUploader.Provider;
            existing.SizeBytes = wasmBytes.Length;
            existing.ContentHash = contentHash;
            existing.Enabled = metadata.Enabled;
            existing.RequiredClaims = metadata.RequiredClaims;
            existing.OutputClaims = metadata.OutputClaims;
            existing.ConfigSchema = metadata.ConfigSchema;
            existing.DefaultConfig = metadata.DefaultConfig;
            existing.Type = metadata.Type;
            existing.Tags = metadata.Tags;
            existing.UpdatedAt = DateTime.UtcNow;
            existing.UpdatedBy = metadata.UpdatedBy;

            _dbContext.PluginMetadata.Update(existing);
            await _dbContext.SaveChangesAsync(cancellationToken);

            _logger.LogInformation("Updated plugin {PluginName} ({Size} bytes) for tenant {TenantId}",
                pluginName, wasmBytes.Length, tenantId ?? "global");

            return existing;
        }
        else
        {
            // Create new
            metadata.Id = Guid.NewGuid().ToString("N");
            metadata.Name = pluginName;
            metadata.TenantId = tenantId;
            metadata.Scope = tenantId == null ? PluginScope.Global : PluginScope.Tenant;
            metadata.StorageReference = uploadResult.StorageReference;
            metadata.StorageProvider = _fileUploader.Provider;
            metadata.SizeBytes = wasmBytes.Length;
            metadata.ContentHash = contentHash;
            metadata.CreatedAt = DateTime.UtcNow;
            metadata.UpdatedAt = null;

            _dbContext.PluginMetadata.Add(metadata);
            await _dbContext.SaveChangesAsync(cancellationToken);

            _logger.LogInformation("Created plugin {PluginName} ({Size} bytes) for tenant {TenantId}",
                pluginName, wasmBytes.Length, tenantId ?? "global");

            return metadata;
        }
    }

    public async Task<PluginMetadata> UpdatePluginMetadataAsync(
        PluginMetadata metadata,
        CancellationToken cancellationToken = default)
    {
        var existing = await _dbContext.PluginMetadata
            .FirstOrDefaultAsync(p => p.Id == metadata.Id, cancellationToken);

        if (existing == null)
        {
            throw new InvalidOperationException($"Plugin with ID '{metadata.Id}' not found");
        }

        existing.DisplayName = metadata.DisplayName;
        existing.Description = metadata.Description;
        existing.Version = metadata.Version;
        existing.Author = metadata.Author;
        existing.Enabled = metadata.Enabled;
        existing.RequiredClaims = metadata.RequiredClaims;
        existing.OutputClaims = metadata.OutputClaims;
        existing.ConfigSchema = metadata.ConfigSchema;
        existing.DefaultConfig = metadata.DefaultConfig;
        existing.Type = metadata.Type;
        existing.Tags = metadata.Tags;
        existing.UpdatedAt = DateTime.UtcNow;
        existing.UpdatedBy = metadata.UpdatedBy;

        _dbContext.PluginMetadata.Update(existing);
        await _dbContext.SaveChangesAsync(cancellationToken);

        _logger.LogDebug("Updated metadata for plugin {PluginName}", metadata.Name);

        return existing;
    }

    public async Task<bool> DeletePluginAsync(
        string pluginName,
        string? tenantId,
        CancellationToken cancellationToken = default)
    {
        var metadata = await _dbContext.PluginMetadata
            .FirstOrDefaultAsync(p => p.Name == pluginName && p.TenantId == tenantId, cancellationToken);

        if (metadata == null)
        {
            return false;
        }

        // Delete from file storage
        await _fileUploader.DeleteAsync(metadata.StorageReference, cancellationToken);

        // Delete from database
        _dbContext.PluginMetadata.Remove(metadata);
        await _dbContext.SaveChangesAsync(cancellationToken);

        _logger.LogInformation("Deleted plugin {PluginName} for tenant {TenantId}",
            pluginName, tenantId ?? "global");

        return true;
    }

    public async Task<bool> ExistsAsync(
        string pluginName,
        string? tenantId,
        CancellationToken cancellationToken = default)
    {
        // Global query filter handles tenant isolation
        return await _dbContext.PluginMetadata
            .AnyAsync(p => p.Name == pluginName, cancellationToken);
    }

    public async Task RecordExecutionAsync(
        string pluginName,
        string? tenantId,
        double executionMs,
        CancellationToken cancellationToken = default)
    {
        // Order by TenantId descending so tenant-specific (non-null) comes before global (null)
        var metadata = await _dbContext.PluginMetadata
            .Where(p => p.Name == pluginName)
            .OrderByDescending(p => p.TenantId)
            .FirstOrDefaultAsync(cancellationToken);

        if (metadata != null)
        {
            // Update execution stats
            var totalMs = (metadata.AverageExecutionMs ?? 0) * metadata.ExecutionCount;
            metadata.ExecutionCount++;
            metadata.AverageExecutionMs = (totalMs + executionMs) / metadata.ExecutionCount;
            metadata.LastExecutedAt = DateTime.UtcNow;

            _dbContext.PluginMetadata.Update(metadata);
            await _dbContext.SaveChangesAsync(cancellationToken);
        }
    }

    public async Task<IEnumerable<PluginMetadata>> SearchAsync(
        string query,
        string? tenantId,
        CancellationToken cancellationToken = default)
    {
        var searchQuery = query.ToLowerInvariant();

        // Search in name, display name, description, and tags
        return await _dbContext.PluginMetadata
            .AsNoTracking()
            .Where(p => p.Enabled &&
                (p.Name.ToLower().Contains(searchQuery) ||
                 (p.DisplayName != null && p.DisplayName.ToLower().Contains(searchQuery)) ||
                 (p.Description != null && p.Description.ToLower().Contains(searchQuery)) ||
                 (p.Tags != null && p.Tags.ToLower().Contains(searchQuery))))
            .OrderBy(p => p.Name)
            .ToListAsync(cancellationToken);
    }

    private static string GetStoragePath(string pluginName, string? tenantId)
    {
        return tenantId == null
            ? $"plugins/global/{pluginName}.wasm"
            : $"plugins/tenants/{tenantId}/{pluginName}.wasm";
    }
}