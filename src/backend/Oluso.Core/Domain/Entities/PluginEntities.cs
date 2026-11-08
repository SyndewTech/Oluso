namespace Oluso.Core.Domain.Entities;

/// <summary>
/// Defines a hook binding for a plugin, specifying which domain events it should respond to
/// </summary>
public class PluginHookBinding : TenantEntity
{
    /// <summary>
    /// Unique identifier
    /// </summary>
    public string Id { get; set; } = Guid.NewGuid().ToString("N");

    /// <summary>
    /// Reference to the plugin
    /// </summary>
    public string PluginId { get; set; } = null!;
    public PluginMetadata? Plugin { get; set; }

    /// <summary>
    /// The domain this hook applies to (e.g., "journey", "workflow", or a module domain)
    /// </summary>
    public string Domain { get; set; } = null!;

    /// <summary>
    /// The entity type within the domain (e.g., "Deal", "Account", "Contact")
    /// For journey hooks, this could be the journey ID or "*" for all journeys
    /// </summary>
    public string EntityType { get; set; } = null!;

    /// <summary>
    /// The event that triggers this hook
    /// Example: before_insert, after_insert, before_update, after_update, before_delete, after_delete
    /// Journey: before_step, after_step, before_submit, after_submit, on_error, on_abandon
    /// </summary>
    public string EventType { get; set; } = null!;

    /// <summary>
    /// The function name to call within the WASM module
    /// </summary>
    public string FunctionName { get; set; } = null!;

    /// <summary>
    /// Execution order (lower numbers run first)
    /// </summary>
    public int Order { get; set; }

    /// <summary>
    /// Whether this hook binding is active
    /// </summary>
    public bool IsActive { get; set; } = true;

    /// <summary>
    /// What to do if the plugin execution fails
    /// </summary>
    public PluginFailureMode FailureMode { get; set; } = PluginFailureMode.Continue;

    /// <summary>
    /// Maximum execution time in milliseconds before timeout
    /// </summary>
    public int TimeoutMs { get; set; } = 5000;

    /// <summary>
    /// JSON filter conditions for when to execute (e.g., {"stage": "Negotiation"})
    /// </summary>
    public string? FilterConditions { get; set; }
}

/// <summary>
/// What to do when a plugin execution fails
/// </summary>
public enum PluginFailureMode
{
    /// <summary>Log the error and continue with the operation</summary>
    Continue = 0,
    /// <summary>Reject the operation if the plugin fails</summary>
    Reject = 1,
    /// <summary>Retry the plugin execution up to max retries</summary>
    Retry = 2
}

/// <summary>
/// Network access configuration for a plugin
/// </summary>
public class PluginNetworkConfig : TenantEntity
{
    /// <summary>
    /// Unique identifier
    /// </summary>
    public string Id { get; set; } = Guid.NewGuid().ToString("N");

    /// <summary>
    /// Reference to the plugin
    /// </summary>
    public string PluginId { get; set; } = null!;
    public PluginMetadata? Plugin { get; set; }

    /// <summary>
    /// Network access mode
    /// </summary>
    public NetworkAccessMode AccessMode { get; set; } = NetworkAccessMode.None;

    /// <summary>
    /// JSON array of allowed hosts (for Whitelist mode)
    /// Supports wildcards like "*.example.com"
    /// </summary>
    public string? AllowedHosts { get; set; }

    /// <summary>
    /// Maximum time for each HTTP request in milliseconds
    /// </summary>
    public int HttpTimeoutMs { get; set; } = 5000;

    /// <summary>
    /// Maximum number of HTTP requests per execution
    /// </summary>
    public int MaxHttpRequestsPerExecution { get; set; } = 5;

    /// <summary>
    /// Whether this plugin requires admin approval for network access
    /// </summary>
    public bool RequiresApproval { get; set; }

    /// <summary>
    /// Whether the network access has been approved
    /// </summary>
    public bool IsApproved { get; set; }

    /// <summary>
    /// Who approved the network access
    /// </summary>
    public string? ApprovedBy { get; set; }

    /// <summary>
    /// When the network access was approved
    /// </summary>
    public DateTime? ApprovedAt { get; set; }
}

/// <summary>
/// Network access modes for plugins
/// </summary>
public enum NetworkAccessMode
{
    /// <summary>No network access allowed</summary>
    None = 0,
    /// <summary>Only requests to the Oluso API (same tenant context)</summary>
    Self = 1,
    /// <summary>Only requests to explicitly allowed hosts</summary>
    Whitelist = 2,
    /// <summary>Unrestricted network access (requires admin approval)</summary>
    All = 3
}

/// <summary>
/// Execution log entry for a plugin invocation
/// </summary>
public class PluginExecutionLog : TenantEntity
{
    /// <summary>
    /// Unique identifier
    /// </summary>
    public string Id { get; set; } = Guid.NewGuid().ToString("N");

    /// <summary>
    /// Reference to the plugin
    /// </summary>
    public string PluginId { get; set; } = null!;

    /// <summary>
    /// Reference to the hook binding that triggered this execution
    /// </summary>
    public string? HookBindingId { get; set; }

    /// <summary>
    /// Unique execution ID for tracing
    /// </summary>
    public string ExecutionId { get; set; } = Guid.NewGuid().ToString("N");

    /// <summary>
    /// The domain that triggered the execution
    /// </summary>
    public string Domain { get; set; } = null!;

    /// <summary>
    /// The event that triggered the execution
    /// </summary>
    public string EventType { get; set; } = null!;

    /// <summary>
    /// The entity type involved
    /// </summary>
    public string? EntityType { get; set; }

    /// <summary>
    /// The entity ID involved (if applicable)
    /// </summary>
    public string? EntityId { get; set; }

    /// <summary>
    /// Execution duration in milliseconds
    /// </summary>
    public int DurationMs { get; set; }

    /// <summary>
    /// Result of the execution
    /// </summary>
    public PluginExecutionResult Result { get; set; }

    /// <summary>
    /// The action taken by the plugin (continue, reject, skip, etc.)
    /// </summary>
    public string? ResultAction { get; set; }

    /// <summary>
    /// Truncated input JSON (for debugging)
    /// </summary>
    public string? InputSummary { get; set; }

    /// <summary>
    /// Truncated output JSON (for debugging)
    /// </summary>
    public string? OutputSummary { get; set; }

    /// <summary>
    /// Error message if execution failed
    /// </summary>
    public string? ErrorMessage { get; set; }

    /// <summary>
    /// Stack trace if execution failed
    /// </summary>
    public string? ErrorStackTrace { get; set; }

    /// <summary>
    /// Number of HTTP requests made during execution
    /// </summary>
    public int HttpRequestCount { get; set; }

    /// <summary>
    /// The user who triggered the execution
    /// </summary>
    public string? UserId { get; set; }

    /// <summary>
    /// When the execution started
    /// </summary>
    public DateTime ExecutedAt { get; set; } = DateTime.UtcNow;
}

/// <summary>
/// Result of a plugin execution
/// </summary>
public enum PluginExecutionResult
{
    /// <summary>Execution completed successfully</summary>
    Success = 0,
    /// <summary>Plugin rejected the operation</summary>
    Rejected = 1,
    /// <summary>Execution failed with an error</summary>
    Error = 2,
    /// <summary>Execution timed out</summary>
    Timeout = 3,
    /// <summary>Plugin was skipped (e.g., filter condition not met)</summary>
    Skipped = 4
}

/// <summary>
/// Configuration overrides for a plugin at the tenant level
/// </summary>
public class PluginConfigOverride : TenantEntity
{
    /// <summary>
    /// Unique identifier
    /// </summary>
    public string Id { get; set; } = Guid.NewGuid().ToString("N");

    /// <summary>
    /// Reference to the plugin
    /// </summary>
    public string PluginId { get; set; } = null!;
    public PluginMetadata? Plugin { get; set; }

    /// <summary>
    /// JSON-serialized configuration overrides
    /// </summary>
    public string ConfigJson { get; set; } = "{}";

    /// <summary>
    /// JSON-serialized secret values (encrypted at rest)
    /// </summary>
    public string? SecretsJson { get; set; }

    /// <summary>
    /// When the config was last updated
    /// </summary>
    public DateTime UpdatedAt { get; set; } = DateTime.UtcNow;

    /// <summary>
    /// Who last updated the config
    /// </summary>
    public string? UpdatedBy { get; set; }
}

