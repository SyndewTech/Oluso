namespace Oluso.Core.Domain.Entities;

/// <summary>
/// Plugin metadata stored in database.
/// The actual WASM bytes are stored in file storage (local, Azure Blob, S3, etc.)
/// </summary>
public class PluginMetadata : TenantEntity
{
    /// <summary>
    /// Unique plugin identifier
    /// </summary>
    public string Id { get; set; } = Guid.NewGuid().ToString("N");

    /// <summary>
    /// Plugin name (used as identifier)
    /// </summary>
    public string Name { get; set; } = null!;

    /// <summary>
    /// Human-friendly display name
    /// </summary>
    public string? DisplayName { get; set; }

    /// <summary>
    /// Description of what the plugin does
    /// </summary>
    public string? Description { get; set; }

    /// <summary>
    /// Plugin version (semver format recommended)
    /// </summary>
    public string? Version { get; set; }

    /// <summary>
    /// Plugin author
    /// </summary>
    public string? Author { get; set; }

    /// <summary>
    /// Plugin scope: Global or Tenant-specific
    /// </summary>
    public PluginScope Scope { get; set; }

    /// <summary>
    /// Reference to the file in storage (path, blob URI, etc.)
    /// </summary>
    public string StorageReference { get; set; } = null!;

    /// <summary>
    /// Storage provider type
    /// </summary>
    public FileStorageProvider StorageProvider { get; set; } = FileStorageProvider.Local;

    /// <summary>
    /// Size of the plugin in bytes
    /// </summary>
    public long SizeBytes { get; set; }

    /// <summary>
    /// SHA256 hash of the plugin bytes for integrity verification
    /// </summary>
    public string? ContentHash { get; set; }

    /// <summary>
    /// Whether the plugin is enabled
    /// </summary>
    public bool Enabled { get; set; } = true;

    /// <summary>
    /// JSON-serialized list of required input claims
    /// </summary>
    public string? RequiredClaims { get; set; }

    /// <summary>
    /// JSON-serialized list of output claims
    /// </summary>
    public string? OutputClaims { get; set; }

    /// <summary>
    /// JSON-serialized configuration schema (JSON Schema format)
    /// </summary>
    public string? ConfigSchema { get; set; }

    /// <summary>
    /// JSON-serialized default configuration values
    /// </summary>
    public string? DefaultConfig { get; set; }

    /// <summary>
    /// Plugin type: Step, ClaimsTransform, Validator, Condition
    /// </summary>
    public PluginType Type { get; set; } = PluginType.Step;

    /// <summary>
    /// Tags for categorization (comma-separated)
    /// </summary>
    public string? Tags { get; set; }

    /// <summary>
    /// When the plugin was uploaded
    /// </summary>
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

    /// <summary>
    /// When the plugin was last updated
    /// </summary>
    public DateTime? UpdatedAt { get; set; }

    /// <summary>
    /// Who uploaded/created the plugin
    /// </summary>
    public string? CreatedBy { get; set; }

    /// <summary>
    /// Who last updated the plugin
    /// </summary>
    public string? UpdatedBy { get; set; }

    /// <summary>
    /// Number of times this plugin has been executed
    /// </summary>
    public long ExecutionCount { get; set; }

    /// <summary>
    /// Last time this plugin was executed
    /// </summary>
    public DateTime? LastExecutedAt { get; set; }

    /// <summary>
    /// Average execution time in milliseconds
    /// </summary>
    public double? AverageExecutionMs { get; set; }

    /// <summary>
    /// Gets the required claims as a list
    /// </summary>
    public List<string> GetRequiredClaimsList()
    {
        if (string.IsNullOrEmpty(RequiredClaims)) return new List<string>();
        return System.Text.Json.JsonSerializer.Deserialize<List<string>>(RequiredClaims) ?? new List<string>();
    }

    /// <summary>
    /// Sets the required claims from a list
    /// </summary>
    public void SetRequiredClaimsList(List<string>? claims)
    {
        RequiredClaims = claims != null ? System.Text.Json.JsonSerializer.Serialize(claims) : null;
    }

    /// <summary>
    /// Gets the output claims as a list
    /// </summary>
    public List<string> GetOutputClaimsList()
    {
        if (string.IsNullOrEmpty(OutputClaims)) return new List<string>();
        return System.Text.Json.JsonSerializer.Deserialize<List<string>>(OutputClaims) ?? new List<string>();
    }

    /// <summary>
    /// Sets the output claims from a list
    /// </summary>
    public void SetOutputClaimsList(List<string>? claims)
    {
        OutputClaims = claims != null ? System.Text.Json.JsonSerializer.Serialize(claims) : null;
    }

    /// <summary>
    /// Gets the tags as a list
    /// </summary>
    public List<string> GetTagsList() =>
        string.IsNullOrEmpty(Tags)
            ? new List<string>()
            : Tags.Split(',', StringSplitOptions.RemoveEmptyEntries).ToList();

    /// <summary>
    /// Sets the tags from a list
    /// </summary>
    public void SetTagsList(IEnumerable<string>? tags) =>
        Tags = tags != null ? string.Join(',', tags) : null;

    /// <summary>
    /// Gets the config schema as a dictionary
    /// </summary>
    public Dictionary<string, object>? GetConfigSchemaObject()
    {
        if (string.IsNullOrEmpty(ConfigSchema)) return null;
        return System.Text.Json.JsonSerializer.Deserialize<Dictionary<string, object>>(ConfigSchema);
    }

    /// <summary>
    /// Sets the config schema from a dictionary
    /// </summary>
    public void SetConfigSchemaObject(Dictionary<string, object>? schema)
    {
        ConfigSchema = schema != null ? System.Text.Json.JsonSerializer.Serialize(schema) : null;
    }

    /// <summary>
    /// Gets the default config as a dictionary
    /// </summary>
    public Dictionary<string, object>? GetDefaultConfigObject()
    {
        if (string.IsNullOrEmpty(DefaultConfig)) return null;
        return System.Text.Json.JsonSerializer.Deserialize<Dictionary<string, object>>(DefaultConfig);
    }

    /// <summary>
    /// Sets the default config from a dictionary
    /// </summary>
    public void SetDefaultConfigObject(Dictionary<string, object>? config)
    {
        DefaultConfig = config != null ? System.Text.Json.JsonSerializer.Serialize(config) : null;
    }
}


/// <summary>
/// Plugin scope
/// </summary>
public enum PluginScope
{
    /// <summary>Available to all tenants</summary>
    Global,
    /// <summary>Available only to specific tenant</summary>
    Tenant
}

/// <summary>
/// Plugin type
/// </summary>
public enum PluginType
{
    /// <summary>Full step implementation</summary>
    Step,
    /// <summary>Claims transformation only</summary>
    ClaimsTransform,
    /// <summary>Validation only</summary>
    Validator,
    /// <summary>Condition evaluation only</summary>
    Condition
}