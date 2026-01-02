using System.Security.Cryptography;
using System.Text.Json;
using Microsoft.Extensions.Logging;
using Oluso.Core.Domain.Entities;
using Oluso.Core.Domain.Interfaces;

namespace Oluso.EntityFramework.Stores;

/// <summary>
/// File system based plugin storage.
/// This is a legacy store that keeps metadata in JSON sidecar files.
/// For production use, prefer DatabasePluginStore which stores metadata in the database.
///
/// Structure:
///   {PluginDirectory}/
///     global/
///       plugin-name.wasm
///       plugin-name.json (metadata)
///     tenants/
///       {tenantId}/
///         plugin-name.wasm
///         plugin-name.json
/// </summary>
public class FileSystemPluginStore : IPluginStore
{
    private readonly string _baseDirectory;
    private readonly ILogger<FileSystemPluginStore> _logger;
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        WriteIndented = true
    };

    public FileSystemPluginStore(string baseDirectory, ILogger<FileSystemPluginStore> logger)
    {
        _baseDirectory = baseDirectory;
        _logger = logger;
        EnsureDirectories();
    }

    private void EnsureDirectories()
    {
        Directory.CreateDirectory(Path.Combine(_baseDirectory, "global"));
        Directory.CreateDirectory(Path.Combine(_baseDirectory, "tenants"));
    }

    public async Task<IEnumerable<PluginMetadata>> GetAvailablePluginsAsync(
        string? tenantId,
        CancellationToken cancellationToken = default)
    {
        var plugins = new List<PluginMetadata>();

        // Get global plugins
        var globalDir = Path.Combine(_baseDirectory, "global");
        if (Directory.Exists(globalDir))
        {
            plugins.AddRange(await GetPluginsFromDirectoryAsync(globalDir, PluginScope.Global, null, cancellationToken));
        }

        // Get tenant-specific plugins
        if (!string.IsNullOrEmpty(tenantId))
        {
            var tenantDir = Path.Combine(_baseDirectory, "tenants", tenantId);
            if (Directory.Exists(tenantDir))
            {
                plugins.AddRange(await GetPluginsFromDirectoryAsync(tenantDir, PluginScope.Tenant, tenantId, cancellationToken));
            }
        }

        return plugins;
    }

    private async Task<List<PluginMetadata>> GetPluginsFromDirectoryAsync(
        string directory,
        PluginScope scope,
        string? tenantId,
        CancellationToken cancellationToken)
    {
        var plugins = new List<PluginMetadata>();
        var wasmFiles = Directory.GetFiles(directory, "*.wasm");

        foreach (var wasmFile in wasmFiles)
        {
            var pluginName = Path.GetFileNameWithoutExtension(wasmFile);
            var metadataFile = Path.Combine(directory, $"{pluginName}.json");

            PluginMetadata metadata;
            if (File.Exists(metadataFile))
            {
                var json = await File.ReadAllTextAsync(metadataFile, cancellationToken);
                metadata = JsonSerializer.Deserialize<PluginMetadata>(json, JsonOptions) ?? new PluginMetadata();
            }
            else
            {
                metadata = new PluginMetadata();
            }

            var fileInfo = new FileInfo(wasmFile);
            metadata.Id = metadata.Id ?? Guid.NewGuid().ToString("N");
            metadata.Name = pluginName;
            metadata.Scope = scope;
            metadata.TenantId = tenantId;
            metadata.StorageReference = wasmFile;
            metadata.StorageProvider = FileStorageProvider.Local;
            metadata.SizeBytes = fileInfo.Length;
            metadata.CreatedAt = fileInfo.CreationTimeUtc;
            metadata.UpdatedAt = fileInfo.LastWriteTimeUtc;

            plugins.Add(metadata);
        }

        return plugins;
    }

    public async Task<PluginMetadata?> GetPluginInfoAsync(
        string pluginName,
        string? tenantId,
        CancellationToken cancellationToken = default)
    {
        // Check tenant-specific first
        if (!string.IsNullOrEmpty(tenantId))
        {
            var tenantPath = GetPluginPath(pluginName, tenantId);
            if (File.Exists(tenantPath))
            {
                return await LoadPluginMetadataAsync(tenantPath, pluginName, PluginScope.Tenant, tenantId, cancellationToken);
            }
        }

        // Fall back to global
        var globalPath = GetPluginPath(pluginName, null);
        if (File.Exists(globalPath))
        {
            return await LoadPluginMetadataAsync(globalPath, pluginName, PluginScope.Global, null, cancellationToken);
        }

        return null;
    }

    public Task<PluginMetadata?> GetByIdAsync(
        string id,
        CancellationToken cancellationToken = default)
    {
        // File system store doesn't support ID-based lookup
        // This is a limitation of the file system approach
        _logger.LogWarning("GetByIdAsync is not supported by FileSystemPluginStore. Use DatabasePluginStore instead.");
        return Task.FromResult<PluginMetadata?>(null);
    }

    private async Task<PluginMetadata> LoadPluginMetadataAsync(
        string wasmPath,
        string pluginName,
        PluginScope scope,
        string? tenantId,
        CancellationToken cancellationToken)
    {
        var directory = Path.GetDirectoryName(wasmPath)!;
        var metadataFile = Path.Combine(directory, $"{pluginName}.json");

        PluginMetadata metadata;
        if (File.Exists(metadataFile))
        {
            var json = await File.ReadAllTextAsync(metadataFile, cancellationToken);
            metadata = JsonSerializer.Deserialize<PluginMetadata>(json, JsonOptions) ?? new PluginMetadata();
        }
        else
        {
            metadata = new PluginMetadata();
        }

        var fileInfo = new FileInfo(wasmPath);
        metadata.Id = metadata.Id ?? Guid.NewGuid().ToString("N");
        metadata.Name = pluginName;
        metadata.Scope = scope;
        metadata.TenantId = tenantId;
        metadata.StorageReference = wasmPath;
        metadata.StorageProvider = FileStorageProvider.Local;
        metadata.SizeBytes = fileInfo.Length;
        metadata.CreatedAt = fileInfo.CreationTimeUtc;
        metadata.UpdatedAt = fileInfo.LastWriteTimeUtc;

        return metadata;
    }

    public async Task<byte[]?> GetPluginBytesAsync(
        string pluginName,
        string? tenantId,
        CancellationToken cancellationToken = default)
    {
        // Check tenant-specific first
        if (!string.IsNullOrEmpty(tenantId))
        {
            var tenantPath = GetPluginPath(pluginName, tenantId);
            if (File.Exists(tenantPath))
            {
                return await File.ReadAllBytesAsync(tenantPath, cancellationToken);
            }
        }

        // Fall back to global
        var globalPath = GetPluginPath(pluginName, null);
        if (File.Exists(globalPath))
        {
            return await File.ReadAllBytesAsync(globalPath, cancellationToken);
        }

        return null;
    }

    public async Task<PluginMetadata> SavePluginAsync(
        string pluginName,
        byte[] wasmBytes,
        PluginMetadata metadata,
        string? tenantId,
        CancellationToken cancellationToken = default)
    {
        var directory = tenantId == null
            ? Path.Combine(_baseDirectory, "global")
            : Path.Combine(_baseDirectory, "tenants", tenantId);

        Directory.CreateDirectory(directory);

        var wasmPath = Path.Combine(directory, $"{pluginName}.wasm");
        var metadataPath = Path.Combine(directory, $"{pluginName}.json");

        // Save WASM
        await File.WriteAllBytesAsync(wasmPath, wasmBytes, cancellationToken);

        // Compute hash
        using var sha256 = SHA256.Create();
        var hashBytes = sha256.ComputeHash(wasmBytes);
        var contentHash = Convert.ToHexString(hashBytes).ToLowerInvariant();

        // Update metadata
        metadata.Id = metadata.Id ?? Guid.NewGuid().ToString("N");
        metadata.Name = pluginName;
        metadata.Scope = tenantId == null ? PluginScope.Global : PluginScope.Tenant;
        metadata.TenantId = tenantId;
        metadata.StorageReference = wasmPath;
        metadata.StorageProvider = FileStorageProvider.Local;
        metadata.SizeBytes = wasmBytes.Length;
        metadata.ContentHash = contentHash;
        metadata.UpdatedAt = DateTime.UtcNow;
        if (metadata.CreatedAt == default)
        {
            metadata.CreatedAt = DateTime.UtcNow;
        }

        // Save metadata
        var json = JsonSerializer.Serialize(metadata, JsonOptions);
        await File.WriteAllTextAsync(metadataPath, json, cancellationToken);

        _logger.LogInformation("Saved plugin {PluginName} ({Size} bytes) for tenant {TenantId}",
            pluginName, wasmBytes.Length, tenantId ?? "global");

        return metadata;
    }

    public async Task<PluginMetadata> UpdatePluginMetadataAsync(
        PluginMetadata metadata,
        CancellationToken cancellationToken = default)
    {
        var directory = metadata.TenantId == null
            ? Path.Combine(_baseDirectory, "global")
            : Path.Combine(_baseDirectory, "tenants", metadata.TenantId);

        var metadataPath = Path.Combine(directory, $"{metadata.Name}.json");

        metadata.UpdatedAt = DateTime.UtcNow;

        var json = JsonSerializer.Serialize(metadata, JsonOptions);
        await File.WriteAllTextAsync(metadataPath, json, cancellationToken);

        _logger.LogDebug("Updated metadata for plugin {PluginName}", metadata.Name);

        return metadata;
    }

    public Task<bool> DeletePluginAsync(
        string pluginName,
        string? tenantId,
        CancellationToken cancellationToken = default)
    {
        var wasmPath = GetPluginPath(pluginName, tenantId);
        var metadataPath = Path.ChangeExtension(wasmPath, ".json");

        if (!File.Exists(wasmPath))
        {
            return Task.FromResult(false);
        }

        File.Delete(wasmPath);
        if (File.Exists(metadataPath))
        {
            File.Delete(metadataPath);
        }

        _logger.LogInformation("Deleted plugin {PluginName} for tenant {TenantId}",
            pluginName, tenantId ?? "global");

        return Task.FromResult(true);
    }

    public Task<bool> ExistsAsync(
        string pluginName,
        string? tenantId,
        CancellationToken cancellationToken = default)
    {
        // Check tenant-specific
        if (!string.IsNullOrEmpty(tenantId))
        {
            var tenantPath = GetPluginPath(pluginName, tenantId);
            if (File.Exists(tenantPath))
            {
                return Task.FromResult(true);
            }
        }

        // Check global
        var globalPath = GetPluginPath(pluginName, null);
        return Task.FromResult(File.Exists(globalPath));
    }

    public Task RecordExecutionAsync(
        string pluginName,
        string? tenantId,
        double executionMs,
        CancellationToken cancellationToken = default)
    {
        // File system store doesn't track execution stats
        // Use DatabasePluginStore for this functionality
        _logger.LogDebug("Plugin execution recorded: {PluginName} ({ExecutionMs}ms)", pluginName, executionMs);
        return Task.CompletedTask;
    }

    public async Task<IEnumerable<PluginMetadata>> SearchAsync(
        string query,
        string? tenantId,
        CancellationToken cancellationToken = default)
    {
        var allPlugins = await GetAvailablePluginsAsync(tenantId, cancellationToken);
        var searchLower = query.ToLowerInvariant();

        return allPlugins.Where(p =>
            p.Name.ToLowerInvariant().Contains(searchLower) ||
            (p.DisplayName?.ToLowerInvariant().Contains(searchLower) ?? false) ||
            (p.Description?.ToLowerInvariant().Contains(searchLower) ?? false) ||
            (p.Tags?.ToLowerInvariant().Contains(searchLower) ?? false));
    }

    private string GetPluginPath(string pluginName, string? tenantId)
    {
        if (tenantId == null)
        {
            return Path.Combine(_baseDirectory, "global", $"{pluginName}.wasm");
        }
        return Path.Combine(_baseDirectory, "tenants", tenantId, $"{pluginName}.wasm");
    }
}
