using System.Collections.Concurrent;
using System.Text.Json;
using Extism.Sdk;
using Microsoft.Extensions.Logging;
using Oluso.Core.Domain.Interfaces;
using Oluso.Core.UserJourneys;

namespace Oluso.UserJourneys.Plugins;

/// <summary>
/// Plugin executor using Extism for WebAssembly plugins.
/// Falls back to managed plugins when Extism is not available.
///
/// To use Extism:
/// 1. Install Extism.Sdk NuGet package
/// 2. Ensure libextism is installed on the system
/// 3. Configure plugin directory via AddFileSystemPluginStore()
/// </summary>
public class ExtismPluginExecutor : IPluginExecutor, IDisposable
{
    private readonly ConcurrentDictionary<string, LoadedPlugin> _plugins = new();
    private readonly IManagedPluginRegistry _managedPluginRegistry;
    private readonly IPluginWatcher _pluginWatcher;
    private readonly IPluginStore? _pluginStore;
    private readonly ILogger<ExtismPluginExecutor> _logger;
    private readonly PluginExecutorOptions _options;
    private bool _disposed;

    /// <summary>
    /// Fields that should never be passed to plugins for security reasons
    /// </summary>
    private static readonly HashSet<string> SensitiveFields = new(StringComparer.OrdinalIgnoreCase)
    {
        "password", "pwd", "pass", "passwd", "secret", "credential",
        "token", "access_token", "refresh_token", "id_token",
        "code", "mfa_code", "otp", "totp", "hotp", "verification_code",
        "pin", "cvv", "cvc", "ssn", "social_security",
        "private_key", "api_key", "apikey", "auth_code"
    };

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase
    };

    public ExtismPluginExecutor(
        IManagedPluginRegistry managedPluginRegistry,
        IPluginWatcher pluginWatcher,
        ILogger<ExtismPluginExecutor> logger,
        IPluginStore? pluginStore = null,
        PluginExecutorOptions? options = null)
    {
        _managedPluginRegistry = managedPluginRegistry;
        _pluginWatcher = pluginWatcher;
        _pluginStore = pluginStore;
        _logger = logger;
        _options = options ?? new PluginExecutorOptions();

        // Subscribe to plugin file changes for hot-reload
        _pluginWatcher.OnPluginChanged += OnPluginFileChanged;
    }

    public async Task<PluginExecutionResult> ExecuteAsync(
        string pluginName,
        string functionName,
        PluginExecutionContext context,
        CancellationToken cancellationToken = default)
    {
        // First check for managed (.NET) plugins
        var managedPlugin = _managedPluginRegistry.Get(pluginName);
        if (managedPlugin != null)
        {
            _logger.LogDebug("Executing managed plugin: {PluginName}.{FunctionName}", pluginName, functionName);
            return await managedPlugin.ExecuteAsync(functionName, context, cancellationToken);
        }

        // Check for loaded WASM plugins
        if (_plugins.TryGetValue(pluginName, out var loadedPlugin))
        {
            return await ExecuteWasmPluginAsync(loadedPlugin, functionName, context, cancellationToken);
        }

        // Try to load from plugin store
        if (_pluginStore != null)
        {
            var wasmBytes = await _pluginStore.GetPluginBytesAsync(pluginName, context.TenantId, cancellationToken);
            if (wasmBytes != null)
            {
                await LoadPluginAsync(pluginName, wasmBytes, cancellationToken);
                return await ExecuteAsync(pluginName, functionName, context, cancellationToken);
            }
        }

        // Try to load from plugin directory
        if (_options.PluginDirectory != null)
        {
            var pluginPath = Path.Combine(_options.PluginDirectory, $"{pluginName}.wasm");
            if (File.Exists(pluginPath))
            {
                var wasmBytes = await File.ReadAllBytesAsync(pluginPath, cancellationToken);
                await LoadPluginAsync(pluginName, wasmBytes, cancellationToken);
                return await ExecuteAsync(pluginName, functionName, context, cancellationToken);
            }
        }

        return new PluginExecutionResult
        {
            Success = false,
            Error = $"Plugin '{pluginName}' not found",
            Action = PluginAction.Fail
        };
    }

    public Task LoadPluginAsync(string pluginName, byte[] wasmBytes, CancellationToken cancellationToken = default)
    {
        try
        {
            // Unload existing plugin if present
            if (_plugins.TryRemove(pluginName, out var existing))
            {
                existing.Dispose();
            }

            // Write to temp file since Extism SDK requires file path or URL
            var tempDir = Path.Combine(Path.GetTempPath(), "oluso-plugins");
            Directory.CreateDirectory(tempDir);
            var tempPath = Path.Combine(tempDir, $"{pluginName}.wasm");
            File.WriteAllBytes(tempPath, wasmBytes);

            // Create Extism plugin with WASI support
            var manifest = new Manifest(new PathWasmSource(tempPath))
            {
                Config = new Dictionary<string, string>
                {
                    { "plugin_name", pluginName }
                },
                AllowedHosts = new[] { "*" } // Allow network access if needed
            };

            var plugin = new Plugin(manifest, new HostFunction[] { }, withWasi: true);

            var loadedPlugin = new LoadedPlugin
            {
                Name = pluginName,
                WasmBytes = wasmBytes,
                LoadedAt = DateTime.UtcNow,
                FilePath = tempPath,
                Plugin = plugin
            };

            _plugins[pluginName] = loadedPlugin;
            _logger.LogInformation("Loaded WASM plugin: {PluginName} ({Size} bytes)",
                pluginName, wasmBytes.Length);

            return Task.CompletedTask;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to load plugin: {PluginName}", pluginName);
            throw;
        }
    }

    public Task UnloadPluginAsync(string pluginName, CancellationToken cancellationToken = default)
    {
        if (_plugins.TryRemove(pluginName, out var plugin))
        {
            plugin.Dispose();
            _logger.LogInformation("Unloaded plugin: {PluginName}", pluginName);
        }
        return Task.CompletedTask;
    }

    public bool IsPluginLoaded(string pluginName)
    {
        return _plugins.ContainsKey(pluginName) || _managedPluginRegistry.Get(pluginName) != null;
    }

    public Core.UserJourneys.PluginInfo? GetPluginInfo(string pluginName)
    {
        if (_plugins.TryGetValue(pluginName, out var loaded))
        {
            return new Core.UserJourneys.PluginInfo
            {
                Name = loaded.Name,
                LoadedAt = loaded.LoadedAt,
                FilePath = loaded.FilePath
            };
        }

        var managed = _managedPluginRegistry.Get(pluginName);
        if (managed != null)
        {
            return new Core.UserJourneys.PluginInfo
            {
                Name = managed.Name,
                Version = managed.Version,
                LoadedAt = DateTime.UtcNow
            };
        }

        return null;
    }

    public IEnumerable<Core.UserJourneys.PluginInfo> GetLoadedPlugins()
    {
        var wasmPlugins = _plugins.Values.Select(p => new Core.UserJourneys.PluginInfo
        {
            Name = p.Name,
            LoadedAt = p.LoadedAt,
            FilePath = p.FilePath
        });

        var managedPlugins = _managedPluginRegistry.GetAll().Select(p => new Core.UserJourneys.PluginInfo
        {
            Name = p.Name,
            Version = p.Plugin.Version,
            LoadedAt = DateTime.UtcNow
        });

        return wasmPlugins.Concat(managedPlugins);
    }

    public async Task ReloadPluginAsync(string pluginName, CancellationToken cancellationToken = default)
    {
        // Try plugin store first
        if (_pluginStore != null)
        {
            var wasmBytes = await _pluginStore.GetPluginBytesAsync(pluginName, null, cancellationToken);
            if (wasmBytes != null)
            {
                await LoadPluginAsync(pluginName, wasmBytes, cancellationToken);
                return;
            }
        }

        // Try plugin directory
        if (_options.PluginDirectory != null)
        {
            var pluginPath = Path.Combine(_options.PluginDirectory, $"{pluginName}.wasm");
            if (File.Exists(pluginPath))
            {
                var wasmBytes = await File.ReadAllBytesAsync(pluginPath, cancellationToken);
                await LoadPluginFromFileAsync(pluginName, pluginPath, wasmBytes, cancellationToken);
                return;
            }
        }

        throw new InvalidOperationException($"Plugin '{pluginName}' not found for reload");
    }

    private Task LoadPluginFromFileAsync(string pluginName, string filePath, byte[] wasmBytes, CancellationToken cancellationToken)
    {
        try
        {
            // Unload existing plugin if present
            if (_plugins.TryRemove(pluginName, out var existing))
            {
                existing.Dispose();
            }

            // Create Extism plugin with WASI support from file path
            var manifest = new Manifest(new PathWasmSource(filePath))
            {
                Config = new Dictionary<string, string>
                {
                    { "plugin_name", pluginName }
                },
                AllowedHosts = new[] { "*" }
            };

            var plugin = new Plugin(manifest, new HostFunction[] { }, withWasi: true);

            var loadedPlugin = new LoadedPlugin
            {
                Name = pluginName,
                WasmBytes = wasmBytes,
                LoadedAt = DateTime.UtcNow,
                FilePath = filePath,
                Plugin = plugin
            };

            _plugins[pluginName] = loadedPlugin;
            _logger.LogInformation("Loaded WASM plugin from file: {PluginName} ({FilePath}, {Size} bytes)",
                pluginName, filePath, wasmBytes.Length);

            return Task.CompletedTask;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to load plugin from file: {PluginName} ({FilePath})", pluginName, filePath);
            throw;
        }
    }

    private async Task<PluginExecutionResult> ExecuteWasmPluginAsync(
        LoadedPlugin loadedPlugin,
        string functionName,
        PluginExecutionContext context,
        CancellationToken cancellationToken)
    {
        if (loadedPlugin.Plugin == null)
        {
            return new PluginExecutionResult
            {
                Success = false,
                Error = "Plugin not properly loaded",
                Action = PluginAction.Fail
            };
        }

        try
        {
            // Prepare input - sanitize to prevent leaking sensitive data
            var sanitizedInput = SanitizeInput(context.Input);

            var input = new PluginInput
            {
                Function = functionName,
                UserId = context.UserId,
                TenantId = context.TenantId,
                Input = sanitizedInput ?? new Dictionary<string, object>(),
                JourneyData = context.JourneyData ?? new Dictionary<string, object>()
            };

            var inputJson = JsonSerializer.Serialize(input, JsonOptions);

            _logger.LogDebug("Executing WASM plugin: {PluginName}.{FunctionName}", loadedPlugin.Name, functionName);

            // Execute with timeout
            using var cts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            cts.CancelAfter(_options.ExecutionTimeout);

            // Execute the plugin
            var outputJson = await Task.Run(() =>
            {
                return loadedPlugin.Plugin.Call(functionName, inputJson);
            }, cts.Token);

            // Parse output
            var output = JsonSerializer.Deserialize<PluginOutput>(outputJson, JsonOptions);

            if (output == null)
            {
                return new PluginExecutionResult
                {
                    Success = false,
                    Error = "Plugin returned null output",
                    Action = PluginAction.Fail
                };
            }

            return new PluginExecutionResult
            {
                Success = output.Success,
                Error = output.Error,
                Output = output.Data,
                Action = output.Action switch
                {
                    "continue" => PluginAction.Continue,
                    "require_input" => PluginAction.RequireInput,
                    "branch" => PluginAction.Branch,
                    "complete" => PluginAction.Complete,
                    "fail" => PluginAction.Fail,
                    _ => PluginAction.Continue
                }
            };
        }
        catch (OperationCanceledException)
        {
            _logger.LogError("WASM plugin execution timed out: {PluginName}.{FunctionName}",
                loadedPlugin.Name, functionName);
            return new PluginExecutionResult
            {
                Success = false,
                Error = "Plugin execution timed out",
                Action = PluginAction.Fail
            };
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error executing WASM plugin: {PluginName}.{FunctionName}",
                loadedPlugin.Name, functionName);
            return new PluginExecutionResult
            {
                Success = false,
                Error = ex.Message,
                Action = PluginAction.Fail
            };
        }
    }

    /// <summary>
    /// Input structure sent to WASM plugins
    /// </summary>
    private class PluginInput
    {
        public string Function { get; set; } = null!;
        public string? UserId { get; set; }
        public string? TenantId { get; set; }
        public IDictionary<string, object> Input { get; set; } = new Dictionary<string, object>();
        public IDictionary<string, object> JourneyData { get; set; } = new Dictionary<string, object>();
    }

    /// <summary>
    /// Output structure expected from WASM plugins
    /// </summary>
    private class PluginOutput
    {
        public bool Success { get; set; }
        public string? Error { get; set; }
        public string? Action { get; set; }
        public IDictionary<string, object>? Data { get; set; }
    }

    private static IDictionary<string, object>? SanitizeInput(IDictionary<string, object>? input)
    {
        if (input == null) return null;

        return input
            .Where(kv => !SensitiveFields.Contains(kv.Key))
            .ToDictionary(kv => kv.Key, kv => kv.Value);
    }

    /// <summary>
    /// Handle plugin file changes for hot-reload
    /// </summary>
    private async void OnPluginFileChanged(object? sender, PluginChangedEventArgs e)
    {
        try
        {
            switch (e.ChangeType)
            {
                case PluginChangeType.Deleted:
                    await UnloadPluginAsync(e.PluginName, CancellationToken.None);
                    _logger.LogInformation("Hot-unloaded plugin: {PluginName}", e.PluginName);
                    break;

                case PluginChangeType.Created:
                case PluginChangeType.Modified:
                    // Wait a bit for file write to complete
                    await Task.Delay(100);

                    if (File.Exists(e.FilePath))
                    {
                        var wasmBytes = await File.ReadAllBytesAsync(e.FilePath, CancellationToken.None);
                        await LoadPluginFromFileAsync(e.PluginName, e.FilePath, wasmBytes, CancellationToken.None);
                        _logger.LogInformation("Hot-reloaded plugin: {PluginName}", e.PluginName);
                    }
                    break;
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to hot-reload plugin: {PluginName}", e.PluginName);
        }
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;

        // Unsubscribe from watcher events
        _pluginWatcher.OnPluginChanged -= OnPluginFileChanged;

        foreach (var plugin in _plugins.Values)
        {
            plugin.Dispose();
        }
        _plugins.Clear();
    }

    private class LoadedPlugin : IDisposable
    {
        public string Name { get; set; } = null!;
        public byte[] WasmBytes { get; set; } = null!;
        public DateTime LoadedAt { get; set; }
        public string? FilePath { get; set; }
        public Plugin? Plugin { get; set; }

        public void Dispose()
        {
            Plugin?.Dispose();
        }
    }
}
