using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Oluso.Admin.Authorization;
using Oluso.Core.Authorization;
using Oluso.Core.Api;
using Oluso.Core.Domain.Entities;
using Oluso.Core.Domain.Interfaces;
using Oluso.Admin.Events;
using Oluso.Core.Events;
using Oluso.Core.Plugins;
using Oluso.Core.UserJourneys;
using Oluso.Admin.Dto.Plugins;

namespace Oluso.Admin.Controllers;

/// <summary>
/// Admin API for managing WASM plugins, hook bindings, and configurations
/// </summary>
[Route("api/admin/plugins")]
public class PluginsController : AdminBaseController
{
    private readonly IPluginStore _pluginStore;
    private readonly IPluginExecutor _pluginExecutor;
    private readonly IPluginHookStore? _hookStore;
    private readonly IPluginExecutionLogger? _executionLogger;
    private readonly IOlusoEventService _eventService;
    private readonly ITenantContext _tenantContext;
    private readonly ILogger<PluginsController> _logger;
    private readonly string? _platformHost;

    private const long MaxPluginSize = 10 * 1024 * 1024; // 10MB

    public PluginsController(
        IPluginStore pluginStore,
        IPluginExecutor pluginExecutor,
        ITenantContext tenantContext,
        IOlusoEventService eventService,
        ILogger<PluginsController> logger,
        Microsoft.Extensions.Configuration.IConfiguration configuration,
        IPluginHookStore? hookStore = null,
        IPluginExecutionLogger? executionLogger = null) : base(tenantContext)
    {
        _pluginStore = pluginStore;
        _pluginExecutor = pluginExecutor;
        _tenantContext = tenantContext;
        _eventService = eventService;
        _hookStore = hookStore;
        _executionLogger = executionLogger;
        _logger = logger;

        var issuer = configuration["Oluso:IssuerUri"];
        _platformHost = Uri.TryCreate(issuer, UriKind.Absolute, out var uri) ? uri.Host : null;
    }

    // ============ Plugin Metadata ============

    /// <summary>
    /// Get all plugins available to the current tenant
    /// </summary>
    [HttpGet]
    [RequirePermission(AdminPermissions.PluginsRead)]
    public async Task<ActionResult<IEnumerable<PluginListItemDto>>> GetPlugins(
        CancellationToken cancellationToken = default)
    {
        var plugins = await _pluginStore.GetAvailablePluginsAsync(
            _tenantContext.TenantId,
            cancellationToken);

        var result = plugins.Select(p => new PluginListItemDto
        {
            Id = p.Id,
            Name = p.Name,
            DisplayName = p.DisplayName ?? p.Name,
            Description = p.Description,
            Version = p.Version,
            Author = p.Author,
            Type = p.Type.ToString(),
            Scope = p.Scope.ToString(),
            Enabled = p.Enabled,
            IsGlobal = p.Scope == PluginScope.Global,
            SizeBytes = p.SizeBytes,
            ExecutionCount = p.ExecutionCount,
            AverageExecutionMs = p.AverageExecutionMs,
            LastExecutedAt = p.LastExecutedAt,
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
    [RequirePermission(AdminPermissions.PluginsRead)]
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
            Id = plugin.Id,
            Name = plugin.Name,
            DisplayName = plugin.DisplayName ?? plugin.Name,
            Description = plugin.Description,
            Version = plugin.Version,
            Author = plugin.Author,
            Type = plugin.Type.ToString(),
            Scope = plugin.Scope.ToString(),
            Enabled = plugin.Enabled,
            TenantId = plugin.TenantId,
            SizeBytes = plugin.SizeBytes,
            ContentHash = plugin.ContentHash,
            ExecutionCount = plugin.ExecutionCount,
            AverageExecutionMs = plugin.AverageExecutionMs,
            LastExecutedAt = plugin.LastExecutedAt,
            CreatedAt = plugin.CreatedAt,
            CreatedBy = plugin.CreatedBy,
            UpdatedAt = plugin.UpdatedAt,
            UpdatedBy = plugin.UpdatedBy,
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
    [RequirePermission(AdminPermissions.PluginsManage)]
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
            Author = request.Author,
            Type = Enum.TryParse<PluginType>(request.Type, true, out var pType) ? pType : PluginType.Step,
            Enabled = request.Enabled ?? true,
            CreatedBy = AdminUserId
        };

        // Set JSON-serialized fields
        if (request.RequiredClaims != null)
            metadata.SetRequiredClaimsList(request.RequiredClaims);
        if (request.OutputClaims != null)
            metadata.SetOutputClaimsList(request.OutputClaims);
        if (request.ConfigSchema != null)
            metadata.SetConfigSchemaObject(request.ConfigSchema);

        // Save plugin
        var saved = await _pluginStore.SavePluginAsync(
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

        await _eventService.RaiseAsync(new PluginUploadedEvent
        {
            TenantId = TenantId,
            AdminUserId = RequireAdminUserId,
            AdminUserName = AdminUserName,
            IpAddress = ClientIp,
            ResourceId = saved.Id,
            ResourceName = saved.Name,
            PluginType = saved.Type.ToString(),
            SizeBytes = saved.SizeBytes
        }, cancellationToken);

        _logger.LogInformation("Uploaded plugin {PluginName} for tenant {TenantId}",
            pluginName, _tenantContext.TenantId);

        return CreatedAtAction(nameof(GetPlugin), new { pluginName }, new PluginDetailDto
        {
            Id = saved.Id,
            Name = saved.Name,
            DisplayName = saved.DisplayName ?? saved.Name,
            Description = saved.Description,
            Version = saved.Version,
            Author = saved.Author,
            Type = saved.Type.ToString(),
            Scope = saved.Scope.ToString(),
            Enabled = saved.Enabled,
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
    [RequirePermission(AdminPermissions.PluginsWrite)]
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
        existing.Enabled = request.Enabled ?? existing.Enabled;
        existing.UpdatedBy = AdminUserId;

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

        await _eventService.RaiseAsync(new PluginUpdatedEvent
        {
            TenantId = TenantId,
            AdminUserId = RequireAdminUserId,
            AdminUserName = AdminUserName,
            IpAddress = ClientIp,
            ResourceId = existing.Id,
            ResourceName = existing.Name
        }, cancellationToken);

        return Ok(new PluginDetailDto
        {
            Id = existing.Id,
            Name = existing.Name,
            DisplayName = existing.DisplayName ?? existing.Name,
            Description = existing.Description,
            Version = existing.Version,
            Author = existing.Author,
            Type = existing.Type.ToString(),
            Scope = existing.Scope.ToString(),
            Enabled = existing.Enabled,
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
    [RequirePermission(AdminPermissions.PluginsManage)]
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

        await _eventService.RaiseAsync(new PluginDeletedEvent
        {
            TenantId = TenantId,
            AdminUserId = RequireAdminUserId,
            AdminUserName = AdminUserName,
            IpAddress = ClientIp,
            ResourceId = existing.Id,
            ResourceName = existing.Name
        }, cancellationToken);

        _logger.LogInformation("Deleted plugin {PluginName} for tenant {TenantId}",
            pluginName, _tenantContext.TenantId);

        return NoContent();
    }

    /// <summary>
    /// Reload a plugin in the executor
    /// </summary>
    [HttpPost("{pluginName}/reload")]
    [RequirePermission(AdminPermissions.PluginsManage)]
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

    // ============ Hook Bindings ============

    /// <summary>
    /// Get all hook bindings for a plugin
    /// </summary>
    [HttpGet("{pluginName}/hooks")]
    [RequirePermission(AdminPermissions.PluginsRead)]
    public async Task<ActionResult<IEnumerable<PluginHookBindingDto>>> GetHooks(
        string pluginName,
        CancellationToken cancellationToken)
    {
        if (_hookStore == null)
        {
            return BadRequest(new { error = "Plugin hook store not configured. Call AddPluginHooks() during startup." });
        }

        // Get plugin to find its ID
        var plugin = await _pluginStore.GetPluginInfoAsync(pluginName, TenantId, cancellationToken);
        if (plugin == null)
        {
            return NotFound();
        }

        var hooks = await _hookStore.GetHooksByPluginAsync(TenantId!, plugin.Id, cancellationToken);

        var result = hooks.Select(h => new PluginHookBindingDto
        {
            Id = h.Id,
            PluginId = h.PluginId,
            Domain = h.Domain,
            EntityType = h.EntityType,
            EventType = h.EventType,
            FunctionName = h.FunctionName,
            Order = h.Order,
            IsActive = h.IsActive,
            FailureMode = h.FailureMode.ToString(),
            TimeoutMs = h.TimeoutMs,
            FilterConditions = h.FilterConditions
        });

        return Ok(result);
    }

    /// <summary>
    /// Create a hook binding for a plugin
    /// </summary>
    [HttpPost("{pluginName}/hooks")]
    [RequirePermission(AdminPermissions.PluginsWrite)]
    public async Task<ActionResult<PluginHookBindingDto>> CreateHook(
        string pluginName,
        [FromBody] CreateHookBindingRequest request,
        CancellationToken cancellationToken)
    {
        if (_hookStore == null)
        {
            return BadRequest(new { error = "Plugin hook store not configured. Call AddPluginHooks() during startup." });
        }

        // Get plugin to find its ID
        var plugin = await _pluginStore.GetPluginInfoAsync(pluginName, TenantId, cancellationToken);
        if (plugin == null)
        {
            return NotFound();
        }

        var binding = new PluginHookBinding
        {
            TenantId = TenantId,
            PluginId = plugin.Id,
            Domain = request.Domain,
            EntityType = request.EntityType,
            EventType = request.EventType,
            FunctionName = request.FunctionName,
            Order = request.Order ?? 0,
            IsActive = request.IsActive ?? true,
            FailureMode = Enum.TryParse<PluginFailureMode>(request.FailureMode, true, out var mode)
                ? mode : PluginFailureMode.Continue,
            TimeoutMs = request.TimeoutMs ?? 5000,
            FilterConditions = request.FilterConditions
        };

        var created = await _hookStore.CreateAsync(binding, cancellationToken);

        await _eventService.RaiseAsync(new PluginHookCreatedEvent
        {
            TenantId = TenantId,
            AdminUserId = RequireAdminUserId,
            AdminUserName = AdminUserName,
            IpAddress = ClientIp,
            ResourceId = created.Id,
            ResourceName = $"{created.Domain}/{created.EntityType}/{created.EventType}",
            PluginId = plugin.Id,
            PluginName = plugin.Name
        }, cancellationToken);

        return CreatedAtAction(nameof(GetHooks), new { pluginName }, new PluginHookBindingDto
        {
            Id = created.Id,
            PluginId = created.PluginId,
            Domain = created.Domain,
            EntityType = created.EntityType,
            EventType = created.EventType,
            FunctionName = created.FunctionName,
            Order = created.Order,
            IsActive = created.IsActive,
            FailureMode = created.FailureMode.ToString(),
            TimeoutMs = created.TimeoutMs,
            FilterConditions = created.FilterConditions
        });
    }

    /// <summary>
    /// Update a hook binding
    /// </summary>
    [HttpPut("{pluginName}/hooks/{hookId}")]
    [RequirePermission(AdminPermissions.PluginsWrite)]
    public async Task<ActionResult<PluginHookBindingDto>> UpdateHook(
        string pluginName,
        string hookId,
        [FromBody] UpdateHookBindingRequest request,
        CancellationToken cancellationToken)
    {
        if (_hookStore == null)
        {
            return BadRequest(new { error = "Plugin hook store not configured. Call AddPluginHooks() during startup." });
        }

        // Get plugin to find its ID
        var plugin = await _pluginStore.GetPluginInfoAsync(pluginName, TenantId, cancellationToken);
        if (plugin == null)
        {
            return NotFound();
        }

        var binding = new PluginHookBinding
        {
            Id = hookId,
            TenantId = TenantId,
            PluginId = plugin.Id,
            Domain = request.Domain,
            EntityType = request.EntityType,
            EventType = request.EventType,
            FunctionName = request.FunctionName,
            Order = request.Order ?? 0,
            IsActive = request.IsActive ?? true,
            FailureMode = Enum.TryParse<PluginFailureMode>(request.FailureMode, true, out var mode)
                ? mode : PluginFailureMode.Continue,
            TimeoutMs = request.TimeoutMs ?? 5000,
            FilterConditions = request.FilterConditions
        };

        try
        {
            var updated = await _hookStore.UpdateAsync(binding, cancellationToken);

            await _eventService.RaiseAsync(new PluginHookUpdatedEvent
            {
                TenantId = TenantId,
                AdminUserId = RequireAdminUserId,
                AdminUserName = AdminUserName,
                IpAddress = ClientIp,
                ResourceId = updated.Id,
                ResourceName = $"{updated.Domain}/{updated.EntityType}/{updated.EventType}",
                PluginId = plugin.Id,
                PluginName = plugin.Name
            }, cancellationToken);

            return Ok(new PluginHookBindingDto
            {
                Id = updated.Id,
                PluginId = updated.PluginId,
                Domain = updated.Domain,
                EntityType = updated.EntityType,
                EventType = updated.EventType,
                FunctionName = updated.FunctionName,
                Order = updated.Order,
                IsActive = updated.IsActive,
                FailureMode = updated.FailureMode.ToString(),
                TimeoutMs = updated.TimeoutMs,
                FilterConditions = updated.FilterConditions
            });
        }
        catch (InvalidOperationException)
        {
            return NotFound();
        }
    }

    /// <summary>
    /// Delete a hook binding
    /// </summary>
    [HttpDelete("{pluginName}/hooks/{hookId}")]
    [RequirePermission(AdminPermissions.PluginsWrite)]
    public async Task<ActionResult> DeleteHook(
        string pluginName,
        string hookId,
        CancellationToken cancellationToken)
    {
        if (_hookStore == null)
        {
            return BadRequest(new { error = "Plugin hook store not configured. Call AddPluginHooks() during startup." });
        }

        // Get plugin for the event
        var plugin = await _pluginStore.GetPluginInfoAsync(pluginName, TenantId, cancellationToken);

        await _hookStore.DeleteAsync(TenantId!, hookId, cancellationToken);

        await _eventService.RaiseAsync(new PluginHookDeletedEvent
        {
            TenantId = TenantId,
            AdminUserId = RequireAdminUserId,
            AdminUserName = AdminUserName,
            IpAddress = ClientIp,
            ResourceId = hookId,
            ResourceName = hookId,
            PluginId = plugin?.Id,
            PluginName = plugin?.Name
        }, cancellationToken);

        return NoContent();
    }

    // ============ Execution Logs ============

    /// <summary>
    /// Get recent execution logs for a plugin
    /// </summary>
    [HttpGet("{pluginName}/executions")]
    [RequirePermission(AdminPermissions.PluginsRead)]
    public async Task<ActionResult<IEnumerable<PluginExecutionLogDto>>> GetExecutions(
        string pluginName,
        [FromQuery] int limit = 100,
        CancellationToken cancellationToken = default)
    {
        if (_executionLogger == null)
        {
            return BadRequest(new { error = "Plugin execution logger not configured. Call AddPluginHooks() during startup." });
        }

        // Get plugin to find its ID
        var plugin = await _pluginStore.GetPluginInfoAsync(pluginName, TenantId, cancellationToken);
        if (plugin == null)
        {
            return NotFound();
        }

        var logs = await _executionLogger.GetRecentExecutionsAsync(TenantId!, plugin.Id, limit, cancellationToken);

        var result = logs.Select(l => new PluginExecutionLogDto
        {
            Id = l.Id,
            PluginId = l.PluginId,
            HookBindingId = l.HookBindingId,
            ExecutionId = l.ExecutionId,
            Domain = l.Domain,
            EventType = l.EventType,
            EntityType = l.EntityType,
            EntityId = l.EntityId,
            DurationMs = l.DurationMs,
            Result = l.Result.ToString(),
            ResultAction = l.ResultAction,
            ErrorMessage = l.ErrorMessage,
            UserId = l.UserId,
            ExecutedAt = l.ExecutedAt
        });

        return Ok(result);
    }

    // ============ Network Configuration ============

    /// <summary>
    /// Get network configuration for a plugin
    /// </summary>
    [HttpGet("{pluginName}/network")]
    [RequirePermission(AdminPermissions.PluginsRead)]
    public async Task<ActionResult<PluginNetworkConfigDto>> GetNetworkConfig(
        string pluginName,
        CancellationToken cancellationToken)
    {
        var plugin = await _pluginStore.GetPluginInfoAsync(pluginName, TenantId, cancellationToken);
        if (plugin == null)
        {
            return NotFound();
        }

        var config = await _pluginStore.GetNetworkConfigAsync(plugin.Id, cancellationToken);

        // No row yet → return the safe default ("None" — no network access).
        if (config == null)
        {
            return Ok(new PluginNetworkConfigDto
            {
                PluginId = plugin.Id,
                PluginName = plugin.Name,
                AccessMode = NetworkAccessMode.None.ToString(),
                AllowedHosts = new List<string>(),
                HttpTimeoutMs = 5000,
                MaxHttpRequestsPerExecution = 5,
                RequiresApproval = false,
                IsApproved = false
            });
        }

        return Ok(MapToDto(plugin.Id, plugin.Name, config));
    }

    /// <summary>
    /// Update network configuration for a plugin
    /// </summary>
    [HttpPut("{pluginName}/network")]
    [RequirePermission(AdminPermissions.PluginsManage)]
    public async Task<ActionResult<PluginNetworkConfigDto>> UpdateNetworkConfig(
        string pluginName,
        [FromBody] UpdateNetworkConfigRequest request,
        CancellationToken cancellationToken)
    {
        var plugin = await _pluginStore.GetPluginInfoAsync(pluginName, TenantId, cancellationToken);
        if (plugin == null)
        {
            return NotFound();
        }

        if (!Enum.TryParse<NetworkAccessMode>(request.AccessMode, ignoreCase: true, out var accessMode))
        {
            return BadRequest(new { error = $"Invalid AccessMode '{request.AccessMode}' — expected one of None, Self, Whitelist" });
        }

        if (accessMode == NetworkAccessMode.All)
        {
            return BadRequest(new
            {
                error = "AccessMode=All is not permitted for sandboxed WASM plugins. Use Whitelist with explicit hosts, or implement as a managed (.NET) plugin if unrestricted egress is required."
            });
        }

        // Whitelist entries must not smuggle catch-alls or internal IPs.
        // Validation is save-time only — DNS rebinding (hostname resolves to
        // an internal IP at request time) is a separate runtime defence.
        if (accessMode == NetworkAccessMode.Whitelist && request.AllowedHosts != null)
        {
            var refusal = PluginNetworkPolicyValidator.ValidateAll(request.AllowedHosts, _platformHost);
            if (refusal != null)
            {
                return BadRequest(new { error = $"Invalid whitelist entry: {refusal}" });
            }
        }

        var existing = await _pluginStore.GetNetworkConfigAsync(plugin.Id, cancellationToken);

        // Wide-open access ("All") needs admin approval. Switching INTO All
        // resets approval — a separate approve endpoint flips IsApproved.
        // Switching out of All clears the approval state so re-entry needs
        // approval again.
        var requiresApproval = accessMode == NetworkAccessMode.All;
        var keepApproval = existing != null
            && existing.AccessMode == accessMode
            && existing.IsApproved;

        var toSave = new PluginNetworkConfig
        {
            Id = existing?.Id ?? string.Empty,
            PluginId = plugin.Id,
            TenantId = plugin.TenantId,
            AccessMode = accessMode,
            AllowedHosts = request.AllowedHosts != null && request.AllowedHosts.Count > 0
                ? System.Text.Json.JsonSerializer.Serialize(request.AllowedHosts)
                : null,
            HttpTimeoutMs = request.HttpTimeoutMs ?? 5000,
            MaxHttpRequestsPerExecution = request.MaxHttpRequestsPerExecution ?? 5,
            RequiresApproval = requiresApproval,
            IsApproved = keepApproval,
            ApprovedBy = keepApproval ? existing!.ApprovedBy : null,
            ApprovedAt = keepApproval ? existing!.ApprovedAt : null
        };

        var saved = await _pluginStore.UpsertNetworkConfigAsync(toSave, cancellationToken);

        // Drop the cached WASM module so the next execution reloads with
        // the new manifest. Without this, an admin's network policy change
        // wouldn't take effect until the host process restarts.
        if (_pluginExecutor.IsPluginLoaded(plugin.Name))
        {
            await _pluginExecutor.UnloadPluginAsync(plugin.Name, cancellationToken);
        }

        await _eventService.RaiseAsync(new PluginNetworkConfigUpdatedEvent
        {
            TenantId = TenantId,
            AdminUserId = RequireAdminUserId,
            AdminUserName = AdminUserName,
            IpAddress = ClientIp,
            ResourceId = plugin.Id,
            ResourceName = plugin.Name,
            AccessMode = saved.AccessMode.ToString()
        }, cancellationToken);

        return Ok(MapToDto(plugin.Id, plugin.Name, saved));
    }

    private static PluginNetworkConfigDto MapToDto(string pluginId, string pluginName, PluginNetworkConfig config)
    {
        var hosts = string.IsNullOrEmpty(config.AllowedHosts)
            ? new List<string>()
            : System.Text.Json.JsonSerializer.Deserialize<List<string>>(config.AllowedHosts) ?? new List<string>();

        return new PluginNetworkConfigDto
        {
            PluginId = pluginId,
            PluginName = pluginName,
            AccessMode = config.AccessMode.ToString(),
            AllowedHosts = hosts,
            HttpTimeoutMs = config.HttpTimeoutMs,
            MaxHttpRequestsPerExecution = config.MaxHttpRequestsPerExecution,
            RequiresApproval = config.RequiresApproval,
            IsApproved = config.IsApproved,
            ApprovedBy = config.ApprovedBy,
            ApprovedAt = config.ApprovedAt
        };
    }

    // ============ Helpers ============

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

public class UploadPluginRequest
{
    public IFormFile? File { get; set; }
    public string? Name { get; set; }
    public string? DisplayName { get; set; }
    public string? Description { get; set; }
    public string? Version { get; set; }
    public string? Author { get; set; }
    public string? Type { get; set; }
    public bool? Enabled { get; set; }
    public List<string>? RequiredClaims { get; set; }
    public List<string>? OutputClaims { get; set; }
    public Dictionary<string, object>? ConfigSchema { get; set; }
}
