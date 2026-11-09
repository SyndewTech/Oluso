using System.Collections.Concurrent;
using System.Security.Cryptography;
using System.Text.Json;
using Extism.Sdk;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using Oluso.Core.Domain.Interfaces;
using Oluso.Core.UserJourneys;
using NetworkAccessMode = Oluso.Core.Domain.Entities.NetworkAccessMode;
using PluginMetadata = Oluso.Core.Domain.Entities.PluginMetadata;
using PluginNetworkConfig = Oluso.Core.Domain.Entities.PluginNetworkConfig;

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
    private readonly string? _platformIssuerHost;
    private bool _disposed;

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase
    };

    public ExtismPluginExecutor(
        IManagedPluginRegistry managedPluginRegistry,
        IPluginWatcher pluginWatcher,
        ILogger<ExtismPluginExecutor> logger,
        IPluginStore? pluginStore = null,
        PluginExecutorOptions? options = null,
        IConfiguration? configuration = null)
    {
        _managedPluginRegistry = managedPluginRegistry;
        _pluginWatcher = pluginWatcher;
        _pluginStore = pluginStore;
        _logger = logger;
        _options = options ?? new PluginExecutorOptions();

        // Used to resolve the "Self" network access mode to the platform's
        // own host. Per-tenant custom domains are not granted via Self —
        // admins should add them explicitly to the Whitelist if a tenant
        // plugin needs to call its own custom domain.
        var issuerUri = configuration?["Oluso:IssuerUri"];
        _platformIssuerHost = Uri.TryCreate(issuerUri, UriKind.Absolute, out var uri) ? uri.Host : null;

        // Subscribe to plugin file changes for hot-reload
        _pluginWatcher.OnPluginChanged += OnPluginFileChanged;
    }

    public async Task<PluginExecutionResult> ExecuteAsync(
        string pluginName,
        string functionName,
        PluginExecutionContext context,
        CancellationToken cancellationToken = default)
    {
        // First check for managed (.NET) plugins. These are 1st-party code
        // registered at startup via OlusoBuilder.AddPlugin<T>() — we trust
        // them and don't gate by metadata.
        var managedPlugin = _managedPluginRegistry.Get(pluginName);
        if (managedPlugin != null)
        {
            _logger.LogDebug("Executing managed plugin: {PluginName}.{FunctionName}", pluginName, functionName);
            return await managedPlugin.ExecuteAsync(functionName, context, cancellationToken);
        }

        // Gate WASM plugins by their metadata before either loading from
        // store or executing an already-loaded one. A plugin disabled via
        // the admin UI must stop executing immediately, even if the WASM
        // module is still cached in _plugins from a prior run.
        PluginMetadata? metadata = null;
        if (_pluginStore != null)
        {
            metadata = await _pluginStore.GetPluginInfoAsync(pluginName, context.TenantId, cancellationToken);
            if (metadata != null && !metadata.Enabled)
            {
                // Hot-disable: drop the cached module so a re-enable forces
                // a fresh load (and therefore re-verifies the content hash).
                if (_plugins.TryRemove(pluginName, out var disabled))
                {
                    disabled.Dispose();
                }
                _logger.LogWarning("Refusing to execute disabled plugin {PluginName}", pluginName);
                return new PluginExecutionResult
                {
                    Success = false,
                    Error = $"Plugin '{pluginName}' is disabled",
                    Action = PluginAction.Fail
                };
            }
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
                // Pull the network policy alongside the bytes so the manifest
                // we hand to Extism reflects the admin's whitelist, not the
                // wide-open default.
                var networkConfig = metadata != null
                    ? await _pluginStore.GetNetworkConfigAsync(metadata.Id, cancellationToken)
                    : null;

                try
                {
                    await LoadPluginAsync(pluginName, wasmBytes, metadata?.ContentHash, networkConfig, cancellationToken);
                }
                catch (InvalidOperationException ex)
                {
                    // Hash mismatch and similar load-time refusals come back
                    // here. Surface as a structured execution failure rather
                    // than propagating an exception to the journey runner.
                    return new PluginExecutionResult
                    {
                        Success = false,
                        Error = ex.Message,
                        Action = PluginAction.Fail
                    };
                }
                return await ExecuteAsync(pluginName, functionName, context, cancellationToken);
            }
        }

        // Try to load from plugin directory. Filesystem plugins are
        // operator-supplied (not user-loaded) so they don't have a
        // metadata record — load without the hash check and with the
        // safe-default network policy (no hosts allowed).
        if (_options.PluginDirectory != null)
        {
            var pluginPath = Path.Combine(_options.PluginDirectory, $"{pluginName}.wasm");
            if (File.Exists(pluginPath))
            {
                var wasmBytes = await File.ReadAllBytesAsync(pluginPath, cancellationToken);
                await LoadPluginAsync(pluginName, wasmBytes, expectedContentHash: null, networkConfig: null, cancellationToken);
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

    public Task LoadPluginAsync(string pluginName, byte[] wasmBytes, CancellationToken cancellationToken = default) =>
        LoadPluginAsync(pluginName, wasmBytes, expectedContentHash: null, networkConfig: null, cancellationToken);

    private Task LoadPluginAsync(
        string pluginName,
        byte[] wasmBytes,
        string? expectedContentHash,
        PluginNetworkConfig? networkConfig,
        CancellationToken cancellationToken)
    {
        try
        {
            // Verify the bytes match the metadata hash before loading. Catches
            // tampering at the storage layer and prevents executing whatever
            // ended up on disk if the WASM file was swapped under us.
            if (!string.IsNullOrEmpty(expectedContentHash))
            {
                var actualHash = ComputeSha256(wasmBytes);
                if (!string.Equals(actualHash, expectedContentHash, StringComparison.OrdinalIgnoreCase))
                {
                    _logger.LogError(
                        "Refusing to load plugin {PluginName}: content hash mismatch (expected {Expected}, got {Actual})",
                        pluginName, expectedContentHash, actualHash);
                    throw new InvalidOperationException(
                        $"Plugin '{pluginName}' content hash does not match metadata — refusing to load");
                }
            }

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

            var allowedHosts = ResolveAllowedHosts(pluginName, networkConfig);

            // Create Extism plugin with WASI support. Note: Extism's manifest
            // AllowedHosts gates extism_http_request — empty array means the
            // plugin's HTTP calls all fail at the host boundary.
            var manifest = new Manifest(new PathWasmSource(tempPath))
            {
                Config = new Dictionary<string, string>
                {
                    { "plugin_name", pluginName }
                },
                AllowedHosts = allowedHosts
            };

            if (networkConfig != null && networkConfig.HttpTimeoutMs > 0)
            {
                manifest.Timeout = TimeSpan.FromMilliseconds(networkConfig.HttpTimeoutMs);
            }

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

    private static string ComputeSha256(byte[] bytes)
    {
        var hash = SHA256.HashData(bytes);
        return Convert.ToHexString(hash).ToLowerInvariant();
    }

    /// <summary>
    /// Translates the admin-set network policy into the host whitelist
    /// passed to Extism's manifest. Falls through to the safe default
    /// (no hosts allowed) when no policy has been set.
    /// </summary>
    private string[] ResolveAllowedHosts(string pluginName, PluginNetworkConfig? config)
    {
        if (config == null)
        {
            // No policy row — deny network entirely. Operator-supplied
            // filesystem plugins land here too; if they need network
            // access, persist a metadata row + policy.
            return Array.Empty<string>();
        }

        switch (config.AccessMode)
        {
            case NetworkAccessMode.None:
                return Array.Empty<string>();

            case NetworkAccessMode.Self:
                if (string.IsNullOrEmpty(_platformIssuerHost))
                {
                    _logger.LogWarning(
                        "Plugin {PluginName} requests Self network access but Oluso:IssuerUri is not configured — denying all hosts",
                        pluginName);
                    return Array.Empty<string>();
                }
                return new[] { _platformIssuerHost };

            case NetworkAccessMode.Whitelist:
                if (string.IsNullOrEmpty(config.AllowedHosts))
                {
                    return Array.Empty<string>();
                }
                try
                {
                    var hosts = JsonSerializer.Deserialize<List<string>>(config.AllowedHosts);
                    return hosts != null && hosts.Count > 0
                        ? hosts.Where(h => !string.IsNullOrWhiteSpace(h)).ToArray()
                        : Array.Empty<string>();
                }
                catch (JsonException ex)
                {
                    _logger.LogError(ex,
                        "Plugin {PluginName} has malformed AllowedHosts JSON — denying all hosts",
                        pluginName);
                    return Array.Empty<string>();
                }

            case NetworkAccessMode.All:
                // "All" is intentionally not honoured. Sandboxed WASM
                // plugins shouldn't have unrestricted egress — that's a
                // platform decision, not a tenant decision, and the only
                // legitimate "needs all egress" case is 1st-party code
                // which should be a managed plugin (registered via
                // OlusoBuilder.AddPlugin<T>()) and bypasses this gate
                // entirely. The enum value is kept for migration safety.
                _logger.LogWarning(
                    "Plugin {PluginName} requests All network access — refused. Use Whitelist with explicit hosts, or implement as a managed (.NET) plugin if unrestricted egress is required.",
                    pluginName);
                return Array.Empty<string>();

            default:
                return Array.Empty<string>();
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

            // Hot-reloaded filesystem plugins are operator-supplied with no
            // metadata record, so they get the safe-default network policy
            // (no hosts allowed). Operators who need network egress should
            // register the plugin in the database and configure a policy.
            var allowedHosts = ResolveAllowedHosts(pluginName, config: null);

            var manifest = new Manifest(new PathWasmSource(filePath))
            {
                Config = new Dictionary<string, string>
                {
                    { "plugin_name", pluginName }
                },
                AllowedHosts = allowedHosts
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
            // Strip credentials from both input *and* journey data — the
            // long-lived data bag can carry tokens or password-derived
            // secrets that would otherwise leak into the WASM sandbox.
            var input = new PluginInput
            {
                Function = functionName,
                UserId = context.UserId,
                TenantId = context.TenantId,
                Input = SensitiveInputs.RedactCopy(context.Input),
                JourneyData = SensitiveInputs.RedactCopy(context.JourneyData)
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
