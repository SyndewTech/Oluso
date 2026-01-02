using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Oluso.Core.Api;
using Oluso.Core.Domain.Entities;
using Oluso.Core.Domain.Interfaces;
using Oluso.Core.UserJourneys;

namespace Oluso.Admin.Controllers;

/// <summary>
/// Admin API for managing WASM plugins per tenant
/// </summary>
[Route("api/admin/plugins")]
public class PluginsController : AdminBaseController
{
    private readonly IPluginStore _pluginStore;
    private readonly IPluginExecutor _pluginExecutor;
    private readonly ITenantContext _tenantContext;
    private readonly ILogger<PluginsController> _logger;

    private const long MaxPluginSize = 10 * 1024 * 1024; // 10MB

    public PluginsController(
        IPluginStore pluginStore,
        IPluginExecutor pluginExecutor,
        ITenantContext tenantContext,
        ILogger<PluginsController> logger) : base(tenantContext)
    {
        _pluginStore = pluginStore;
        _pluginExecutor = pluginExecutor;
        _tenantContext = tenantContext;
        _logger = logger;
    }

    /// <summary>
    /// Get all plugins available to the current tenant
    /// </summary>
    [HttpGet]
    public async Task<ActionResult<IEnumerable<PluginListItemDto>>> GetPlugins(
        CancellationToken cancellationToken = default)
    {
        var plugins = await _pluginStore.GetAvailablePluginsAsync(
            _tenantContext.TenantId,
            cancellationToken);

        var result = plugins.Select(p => new PluginListItemDto
        {
            Name = p.Name,
            DisplayName = p.DisplayName ?? p.Name,
            Description = p.Description,
            Version = p.Version,
            Author = p.Author,
            Scope = p.Scope.ToString(),
            IsGlobal = p.Scope == PluginScope.Global,
            SizeBytes = p.SizeBytes,
            CreatedAt = p.CreatedAt,
            UpdatedAt = p.UpdatedAt,
            IsLoaded = _pluginExecutor.IsPluginLoaded(p.Name)
        });

        return Ok(result);
    }

    /// <summary>
    /// Get plugin details
    /// </summary>
    [HttpGet("{pluginName}")]
    public async Task<ActionResult<PluginDetailDto>> GetPlugin(
        string pluginName,
        CancellationToken cancellationToken = default)
    {
        var plugin = await _pluginStore.GetPluginInfoAsync(
            pluginName,
            _tenantContext.TenantId,
            cancellationToken);

        if (plugin == null)
        {
            return NotFound();
        }

        return Ok(new PluginDetailDto
        {
            Name = plugin.Name,
            DisplayName = plugin.DisplayName ?? plugin.Name,
            Description = plugin.Description,
            Version = plugin.Version,
            Author = plugin.Author,
            Scope = plugin.Scope.ToString(),
            TenantId = plugin.TenantId,
            SizeBytes = plugin.SizeBytes,
            CreatedAt = plugin.CreatedAt,
            UpdatedAt = plugin.UpdatedAt,
            RequiredClaims = plugin.GetRequiredClaimsList(),
            OutputClaims = plugin.GetOutputClaimsList(),
            ConfigSchema = plugin.GetConfigSchemaObject(),
            IsLoaded = _pluginExecutor.IsPluginLoaded(plugin.Name)
        });
    }

    /// <summary>
    /// Upload a new plugin
    /// </summary>
    [HttpPost]
    [RequestSizeLimit(MaxPluginSize)]
    public async Task<ActionResult<PluginDetailDto>> UploadPlugin(
        [FromForm] UploadPluginRequest request,
        CancellationToken cancellationToken = default)
    {
        if (request.File == null || request.File.Length == 0)
        {
            return BadRequest(new { error = "No file provided" });
        }

        if (!request.File.FileName.EndsWith(".wasm", StringComparison.OrdinalIgnoreCase))
        {
            return BadRequest(new { error = "File must be a .wasm file" });
        }

        if (request.File.Length > MaxPluginSize)
        {
            return BadRequest(new { error = $"File size exceeds maximum of {MaxPluginSize / 1024 / 1024}MB" });
        }

        var pluginName = request.Name
            ?? Path.GetFileNameWithoutExtension(request.File.FileName);

        // Sanitize plugin name
        pluginName = SanitizePluginName(pluginName);
        if (string.IsNullOrEmpty(pluginName))
        {
            return BadRequest(new { error = "Invalid plugin name" });
        }

        // Check if updating existing
        var existing = await _pluginStore.GetPluginInfoAsync(
            pluginName,
            _tenantContext.TenantId,
            cancellationToken);

        if (existing != null && existing.Scope == PluginScope.Global)
        {
            return BadRequest(new { error = "Cannot overwrite global plugins. Use a different name." });
        }

        // Read file bytes
        using var memoryStream = new MemoryStream();
        await request.File.CopyToAsync(memoryStream, cancellationToken);
        var wasmBytes = memoryStream.ToArray();

        // Basic WASM validation (check magic bytes)
        if (!IsValidWasm(wasmBytes))
        {
            return BadRequest(new { error = "Invalid WASM file format" });
        }

        // Create metadata
        var metadata = new PluginMetadata
        {
            Name = pluginName,
            DisplayName = request.DisplayName ?? pluginName,
            Description = request.Description,
            Version = request.Version ?? "1.0.0",
            Author = request.Author
        };

        // Set JSON-serialized fields
        if (request.RequiredClaims != null)
            metadata.SetRequiredClaimsList(request.RequiredClaims);
        if (request.OutputClaims != null)
            metadata.SetOutputClaimsList(request.OutputClaims);
        if (request.ConfigSchema != null)
            metadata.SetConfigSchemaObject(request.ConfigSchema);

        // Save plugin
        await _pluginStore.SavePluginAsync(
            pluginName,
            wasmBytes,
            metadata,
            _tenantContext.TenantId,
            cancellationToken);

        // Reload in executor if already loaded
        if (_pluginExecutor.IsPluginLoaded(pluginName))
        {
            await _pluginExecutor.LoadPluginAsync(pluginName, wasmBytes, cancellationToken);
        }

        _logger.LogInformation("Uploaded plugin {PluginName} for tenant {TenantId}",
            pluginName, _tenantContext.TenantId);

        var saved = await _pluginStore.GetPluginInfoAsync(
            pluginName,
            _tenantContext.TenantId,
            cancellationToken);

        return CreatedAtAction(nameof(GetPlugin), new { pluginName }, new PluginDetailDto
        {
            Name = saved!.Name,
            DisplayName = saved.DisplayName ?? saved.Name,
            Description = saved.Description,
            Version = saved.Version,
            Author = saved.Author,
            Scope = saved.Scope.ToString(),
            TenantId = saved.TenantId,
            SizeBytes = saved.SizeBytes,
            CreatedAt = saved.CreatedAt,
            UpdatedAt = saved.UpdatedAt,
            RequiredClaims = saved.GetRequiredClaimsList(),
            OutputClaims = saved.GetOutputClaimsList(),
            ConfigSchema = saved.GetConfigSchemaObject(),
            IsLoaded = _pluginExecutor.IsPluginLoaded(saved.Name)
        });
    }

    /// <summary>
    /// Update plugin metadata (not the WASM file)
    /// </summary>
    [HttpPatch("{pluginName}")]
    public async Task<ActionResult<PluginDetailDto>> UpdatePluginMetadata(
        string pluginName,
        [FromBody] UpdatePluginMetadataRequest request,
        CancellationToken cancellationToken = default)
    {
        var existing = await _pluginStore.GetPluginInfoAsync(
            pluginName,
            _tenantContext.TenantId,
            cancellationToken);

        if (existing == null)
        {
            return NotFound();
        }

        // Can only update tenant-specific plugins
        if (existing.Scope == PluginScope.Global)
        {
            return BadRequest(new { error = "Cannot modify global plugins" });
        }

        if (existing.TenantId != _tenantContext.TenantId)
        {
            return Forbid();
        }

        // Get existing bytes
        var wasmBytes = await _pluginStore.GetPluginBytesAsync(
            pluginName,
            _tenantContext.TenantId,
            cancellationToken);

        if (wasmBytes == null)
        {
            return NotFound();
        }

        // Update metadata
        existing.DisplayName = request.DisplayName ?? existing.DisplayName;
        existing.Description = request.Description ?? existing.Description;
        existing.Version = request.Version ?? existing.Version;
        existing.Author = request.Author ?? existing.Author;
        if (request.RequiredClaims != null)
            existing.SetRequiredClaimsList(request.RequiredClaims);
        if (request.OutputClaims != null)
            existing.SetOutputClaimsList(request.OutputClaims);
        if (request.ConfigSchema != null)
            existing.SetConfigSchemaObject(request.ConfigSchema);

        await _pluginStore.SavePluginAsync(
            pluginName,
            wasmBytes,
            existing,
            _tenantContext.TenantId,
            cancellationToken);

        return Ok(new PluginDetailDto
        {
            Name = existing.Name,
            DisplayName = existing.DisplayName ?? existing.Name,
            Description = existing.Description,
            Version = existing.Version,
            Author = existing.Author,
            Scope = existing.Scope.ToString(),
            TenantId = existing.TenantId,
            SizeBytes = existing.SizeBytes,
            CreatedAt = existing.CreatedAt,
            UpdatedAt = existing.UpdatedAt,
            RequiredClaims = existing.GetRequiredClaimsList(),
            OutputClaims = existing.GetOutputClaimsList(),
            ConfigSchema = existing.GetConfigSchemaObject(),
            IsLoaded = _pluginExecutor.IsPluginLoaded(existing.Name)
        });
    }

    /// <summary>
    /// Delete a plugin
    /// </summary>
    [HttpDelete("{pluginName}")]
    public async Task<IActionResult> DeletePlugin(
        string pluginName,
        CancellationToken cancellationToken = default)
    {
        var existing = await _pluginStore.GetPluginInfoAsync(
            pluginName,
            _tenantContext.TenantId,
            cancellationToken);

        if (existing == null)
        {
            return NotFound();
        }

        // Can only delete tenant-specific plugins
        if (existing.Scope == PluginScope.Global)
        {
            return BadRequest(new { error = "Cannot delete global plugins" });
        }

        if (existing.TenantId != _tenantContext.TenantId)
        {
            return Forbid();
        }

        // Unload from executor
        if (_pluginExecutor.IsPluginLoaded(pluginName))
        {
            await _pluginExecutor.UnloadPluginAsync(pluginName, cancellationToken);
        }

        var deleted = await _pluginStore.DeletePluginAsync(
            pluginName,
            _tenantContext.TenantId,
            cancellationToken);

        if (!deleted)
        {
            return NotFound();
        }

        _logger.LogInformation("Deleted plugin {PluginName} for tenant {TenantId}",
            pluginName, _tenantContext.TenantId);

        return NoContent();
    }

    /// <summary>
    /// Reload a plugin in the executor
    /// </summary>
    [HttpPost("{pluginName}/reload")]
    public async Task<IActionResult> ReloadPlugin(
        string pluginName,
        CancellationToken cancellationToken = default)
    {
        var wasmBytes = await _pluginStore.GetPluginBytesAsync(
            pluginName,
            _tenantContext.TenantId,
            cancellationToken);

        if (wasmBytes == null)
        {
            return NotFound();
        }

        await _pluginExecutor.LoadPluginAsync(pluginName, wasmBytes, cancellationToken);

        _logger.LogInformation("Reloaded plugin {PluginName}", pluginName);

        return Ok(new { reloaded = true });
    }

    private static string SanitizePluginName(string name)
    {
        // Only allow alphanumeric, hyphens, underscores
        return new string(name
            .Where(c => char.IsLetterOrDigit(c) || c == '-' || c == '_')
            .ToArray())
            .ToLowerInvariant();
    }

    private static bool IsValidWasm(byte[] bytes)
    {
        // WASM magic bytes: 0x00 0x61 0x73 0x6D (\0asm)
        if (bytes.Length < 8) return false;
        return bytes[0] == 0x00 &&
               bytes[1] == 0x61 &&
               bytes[2] == 0x73 &&
               bytes[3] == 0x6D;
    }
}

#region DTOs

public class PluginListItemDto
{
    public string Name { get; set; } = null!;
    public string DisplayName { get; set; } = null!;
    public string? Description { get; set; }
    public string? Version { get; set; }
    public string? Author { get; set; }
    public string Scope { get; set; } = null!;
    public bool IsGlobal { get; set; }
    public long SizeBytes { get; set; }
    public DateTime CreatedAt { get; set; }
    public DateTime? UpdatedAt { get; set; }
    public bool IsLoaded { get; set; }
}

public class PluginDetailDto
{
    public string Name { get; set; } = null!;
    public string DisplayName { get; set; } = null!;
    public string? Description { get; set; }
    public string? Version { get; set; }
    public string? Author { get; set; }
    public string Scope { get; set; } = null!;
    public string? TenantId { get; set; }
    public long SizeBytes { get; set; }
    public DateTime CreatedAt { get; set; }
    public DateTime? UpdatedAt { get; set; }
    public List<string>? RequiredClaims { get; set; }
    public List<string>? OutputClaims { get; set; }
    public Dictionary<string, object>? ConfigSchema { get; set; }
    public bool IsLoaded { get; set; }
}

public class UploadPluginRequest
{
    public IFormFile? File { get; set; }
    public string? Name { get; set; }
    public string? DisplayName { get; set; }
    public string? Description { get; set; }
    public string? Version { get; set; }
    public string? Author { get; set; }
    public List<string>? RequiredClaims { get; set; }
    public List<string>? OutputClaims { get; set; }
    public Dictionary<string, object>? ConfigSchema { get; set; }
}

public class UpdatePluginMetadataRequest
{
    public string? DisplayName { get; set; }
    public string? Description { get; set; }
    public string? Version { get; set; }
    public string? Author { get; set; }
    public List<string>? RequiredClaims { get; set; }
    public List<string>? OutputClaims { get; set; }
    public Dictionary<string, object>? ConfigSchema { get; set; }
}

#endregion
