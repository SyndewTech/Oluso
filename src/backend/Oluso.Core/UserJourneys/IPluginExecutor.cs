namespace Oluso.Core.UserJourneys;

/// <summary>
/// Executes WASM plugins for custom journey steps
/// </summary>
public interface IPluginExecutor
{
    /// <summary>
    /// Executes a plugin function
    /// </summary>
    Task<PluginExecutionResult> ExecuteAsync(
        string pluginName,
        string functionName,
        PluginExecutionContext context,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Gets information about a loaded plugin
    /// </summary>
    PluginInfo? GetPluginInfo(string pluginName);

    /// <summary>
    /// Gets all loaded plugins
    /// </summary>
    IEnumerable<PluginInfo> GetLoadedPlugins();

    /// <summary>
    /// Reloads a plugin from disk
    /// </summary>
    Task ReloadPluginAsync(string pluginName, CancellationToken cancellationToken = default);

    /// <summary>
    /// Checks if a plugin is currently loaded
    /// </summary>
    bool IsPluginLoaded(string pluginName);

    /// <summary>
    /// Loads a plugin from bytes
    /// </summary>
    Task LoadPluginAsync(string pluginName, byte[] wasmBytes, CancellationToken cancellationToken = default);

    /// <summary>
    /// Unloads a plugin
    /// </summary>
    Task UnloadPluginAsync(string pluginName, CancellationToken cancellationToken = default);
}

/// <summary>
/// Context passed to plugin execution
/// </summary>
public class PluginExecutionContext
{
    public string? UserId { get; init; }
    public string? TenantId { get; init; }
    public IDictionary<string, object>? Input { get; init; }
    public IDictionary<string, object>? JourneyData { get; init; }
}

/// <summary>
/// Result from plugin execution
/// </summary>
public class PluginExecutionResult
{
    public bool Success { get; init; }
    public string? Error { get; init; }
    public IDictionary<string, object>? Output { get; init; }
    public PluginAction Action { get; init; } = PluginAction.Continue;
}

/// <summary>
/// Action indicated by plugin result
/// </summary>
public enum PluginAction
{
    Continue,
    RequireInput,
    Branch,
    Complete,
    Fail
}

/// <summary>
/// Information about a loaded plugin
/// </summary>
public class PluginInfo
{
    public required string Name { get; init; }
    public string? Version { get; init; }
    public string? Description { get; init; }
    public string? FilePath { get; init; }
    public DateTime LoadedAt { get; init; }
    public IReadOnlyCollection<string>? ExportedFunctions { get; init; }
}

/// <summary>
/// Options for plugin execution
/// </summary>
public class PluginExecutorOptions
{
    /// <summary>
    /// Directory where WASM plugins are stored
    /// </summary>
    public string? PluginDirectory { get; set; }

    /// <summary>
    /// Enable hot reload when plugin files change
    /// </summary>
    public bool EnableHotReload { get; set; } = true;

    /// <summary>
    /// Maximum execution time for plugins
    /// </summary>
    public TimeSpan ExecutionTimeout { get; set; } = TimeSpan.FromSeconds(30);

    /// <summary>
    /// Maximum memory (bytes) a plugin can use
    /// </summary>
    public long MaxMemoryBytes { get; set; } = 100 * 1024 * 1024; // 100MB
}

/// <summary>
/// Registry for managed plugins (non-WASM plugins implemented in .NET)
/// </summary>
public interface IManagedPluginRegistry
{
    /// <summary>
    /// Registers a managed plugin
    /// </summary>
    void Register(string name, IManagedPlugin plugin);

    /// <summary>
    /// Gets a registered plugin
    /// </summary>
    IManagedPlugin? Get(string name);

    /// <summary>
    /// Gets all registered plugins
    /// </summary>
    IEnumerable<(string Name, IManagedPlugin Plugin)> GetAll();
}

/// <summary>
/// A managed plugin implemented in .NET
/// </summary>
public interface IManagedPlugin
{
    /// <summary>
    /// Plugin name
    /// </summary>
    string Name { get; }

    /// <summary>
    /// Plugin version
    /// </summary>
    string? Version { get; }

    /// <summary>
    /// Whether this plugin is currently enabled
    /// </summary>
    bool IsEnabled { get; }

    /// <summary>
    /// Executes the plugin
    /// </summary>
    Task<PluginExecutionResult> ExecuteAsync(
        string functionName,
        PluginExecutionContext context,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Gets the claims provider exposed by this plugin (if any).
    /// Plugins that provide custom claims for tokens should implement this.
    /// </summary>
    IPluginClaimsProvider? GetClaimsProvider() => null;
}

/// <summary>
/// Claims provider exposed by a plugin.
/// Plugins implement this to contribute claims to tokens.
/// </summary>
public interface IPluginClaimsProvider
{
    /// <summary>
    /// The plugin name this provider belongs to
    /// </summary>
    string PluginName { get; }

    /// <summary>
    /// Priority for claim resolution. Higher priority providers are processed first.
    /// Use 100 for standard modules, 50 for custom modules.
    /// </summary>
    int Priority => 100;

    /// <summary>
    /// The scopes that trigger this claims provider.
    /// If empty or null, provider is always invoked (regardless of scopes).
    /// </summary>
    IEnumerable<string>? TriggerScopes => null;

    /// <summary>
    /// The protocols that trigger this claims provider (e.g., "oidc", "saml", "wsfed").
    /// If empty or null, provider is always invoked (regardless of protocol).
    /// Use this to ensure protocol-specific claims providers only respond to their protocol.
    /// </summary>
    IEnumerable<string>? TriggerProtocols => null;

    /// <summary>
    /// Get claims for a user during token generation.
    /// </summary>
    Task<PluginClaimsResult> GetClaimsAsync(
        PluginClaimsContext context,
        CancellationToken cancellationToken = default);
}

/// <summary>
/// Context for plugin claims providers
/// </summary>
public class PluginClaimsContext
{
    /// <summary>
    /// The user's subject ID
    /// </summary>
    public required string SubjectId { get; init; }

    /// <summary>
    /// The tenant ID (if multi-tenant)
    /// </summary>
    public string? TenantId { get; init; }

    /// <summary>
    /// The client ID requesting the token
    /// </summary>
    public string? ClientId { get; init; }

    /// <summary>
    /// The scopes being requested
    /// </summary>
    public IEnumerable<string> Scopes { get; init; } = Array.Empty<string>();

    /// <summary>
    /// The caller (e.g., "TokenEndpoint", "UserInfoEndpoint")
    /// </summary>
    public string? Caller { get; init; }

    /// <summary>
    /// Session ID
    /// </summary>
    public string? SessionId { get; init; }

    /// <summary>
    /// The protocol being used (e.g., "oidc", "saml", "wsfed").
    /// Claims providers can use this to only respond to specific protocols.
    /// </summary>
    public string? Protocol { get; init; }
}

/// <summary>
/// Result from a plugin claims provider
/// </summary>
public class PluginClaimsResult
{
    /// <summary>
    /// The claims to add to the token
    /// </summary>
    public IDictionary<string, object> Claims { get; init; } = new Dictionary<string, object>();

    /// <summary>
    /// Whether the provider executed successfully
    /// </summary>
    public bool Success { get; init; } = true;

    /// <summary>
    /// Error message if the provider failed
    /// </summary>
    public string? Error { get; init; }

    public static PluginClaimsResult Ok(IDictionary<string, object> claims)
        => new() { Claims = claims, Success = true };

    public static PluginClaimsResult Empty()
        => new() { Claims = new Dictionary<string, object>(), Success = true };

    public static PluginClaimsResult Fail(string error)
        => new() { Success = false, Error = error };
}

/// <summary>
/// Schema for dynamically rendered forms from plugins
/// </summary>
public class DynamicFormSchema
{
    /// <summary>
    /// Form title displayed at the top
    /// </summary>
    public string? Title { get; set; }

    /// <summary>
    /// Optional description/instructions
    /// </summary>
    public string? Description { get; set; }

    /// <summary>
    /// Error message to display (e.g., validation failure)
    /// </summary>
    public string? ErrorMessage { get; set; }

    /// <summary>
    /// Form fields to render
    /// </summary>
    public List<DynamicFormField> Fields { get; set; } = new();

    /// <summary>
    /// Submit button text (default: "Continue")
    /// </summary>
    public string SubmitButtonText { get; set; } = "Continue";

    /// <summary>
    /// Optional cancel button text (if set, shows cancel button)
    /// </summary>
    public string? CancelButtonText { get; set; }

    /// <summary>
    /// Custom CSS class for the form container
    /// </summary>
    public string? CssClass { get; set; }
}

/// <summary>
/// A field in a dynamic form
/// </summary>
public class DynamicFormField
{
    /// <summary>
    /// Field name (used as form input name)
    /// </summary>
    public string Name { get; set; } = null!;

    /// <summary>
    /// Display label
    /// </summary>
    public string? Label { get; set; }

    /// <summary>
    /// Field type: text, email, password, number, date, select, checkbox, radio, hidden, textarea
    /// </summary>
    public string Type { get; set; } = "text";

    /// <summary>
    /// Placeholder text
    /// </summary>
    public string? Placeholder { get; set; }

    /// <summary>
    /// Default/current value
    /// </summary>
    public string? Value { get; set; }

    /// <summary>
    /// Is field required
    /// </summary>
    public bool Required { get; set; }

    /// <summary>
    /// Help text shown below the field
    /// </summary>
    public string? HelpText { get; set; }

    /// <summary>
    /// Validation error message for this field
    /// </summary>
    public string? ValidationError { get; set; }

    /// <summary>
    /// Options for select/radio fields
    /// </summary>
    public List<DynamicFormOption>? Options { get; set; }

    /// <summary>
    /// Pattern for validation (regex)
    /// </summary>
    public string? Pattern { get; set; }

    /// <summary>
    /// Min value (for number) or min length (for text)
    /// </summary>
    public int? Min { get; set; }

    /// <summary>
    /// Max value (for number) or max length (for text)
    /// </summary>
    public int? Max { get; set; }

    /// <summary>
    /// Custom CSS class for this field
    /// </summary>
    public string? CssClass { get; set; }
}

/// <summary>
/// Option for select/radio fields
/// </summary>
public class DynamicFormOption
{
    public string Value { get; set; } = null!;
    public string Label { get; set; } = null!;
    public bool Selected { get; set; }
    public bool Disabled { get; set; }
}
