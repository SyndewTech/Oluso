namespace Oluso.Admin.Dto.Plugins;

public class PluginListItemDto
{
    public string Id { get; set; } = null!;
    public string Name { get; set; } = null!;
    public string DisplayName { get; set; } = null!;
    public string? Description { get; set; }
    public string? Version { get; set; }
    public string? Author { get; set; }
    public string? Type { get; set; }
    public string Scope { get; set; } = null!;
    public bool Enabled { get; set; }
    public bool IsGlobal { get; set; }
    public long SizeBytes { get; set; }
    public long ExecutionCount { get; set; }
    public double? AverageExecutionMs { get; set; }
    public DateTime? LastExecutedAt { get; set; }
    public DateTime CreatedAt { get; set; }
    public DateTime? UpdatedAt { get; set; }
    public bool IsLoaded { get; set; }
}

public class PluginDetailDto
{
    public string Id { get; set; } = null!;
    public string Name { get; set; } = null!;
    public string DisplayName { get; set; } = null!;
    public string? Description { get; set; }
    public string? Version { get; set; }
    public string? Author { get; set; }
    public string? Type { get; set; }
    public string Scope { get; set; } = null!;
    public bool Enabled { get; set; }
    public string? TenantId { get; set; }
    public long SizeBytes { get; set; }
    public string? ContentHash { get; set; }
    public long ExecutionCount { get; set; }
    public double? AverageExecutionMs { get; set; }
    public DateTime? LastExecutedAt { get; set; }
    public DateTime CreatedAt { get; set; }
    public string? CreatedBy { get; set; }
    public DateTime? UpdatedAt { get; set; }
    public string? UpdatedBy { get; set; }
    public List<string>? RequiredClaims { get; set; }
    public List<string>? OutputClaims { get; set; }
    public Dictionary<string, object>? ConfigSchema { get; set; }
    public bool IsLoaded { get; set; }
}

public class UpdatePluginMetadataRequest
{
    public string? DisplayName { get; set; }
    public string? Description { get; set; }
    public string? Version { get; set; }
    public string? Author { get; set; }
    public bool? Enabled { get; set; }
    public List<string>? RequiredClaims { get; set; }
    public List<string>? OutputClaims { get; set; }
    public Dictionary<string, object>? ConfigSchema { get; set; }
}

public class PluginHookBindingDto
{
    public string Id { get; set; } = null!;
    public string PluginId { get; set; } = null!;
    public string Domain { get; set; } = null!;
    public string EntityType { get; set; } = null!;
    public string EventType { get; set; } = null!;
    public string FunctionName { get; set; } = null!;
    public int Order { get; set; }
    public bool IsActive { get; set; }
    public string FailureMode { get; set; } = null!;
    public int TimeoutMs { get; set; }
    public string? FilterConditions { get; set; }
}

public class CreateHookBindingRequest
{
    public string Domain { get; set; } = null!;
    public string EntityType { get; set; } = null!;
    public string EventType { get; set; } = null!;
    public string FunctionName { get; set; } = null!;
    public int? Order { get; set; }
    public bool? IsActive { get; set; }
    public string? FailureMode { get; set; }
    public int? TimeoutMs { get; set; }
    public string? FilterConditions { get; set; }
}

public class UpdateHookBindingRequest
{
    public string Domain { get; set; } = null!;
    public string EntityType { get; set; } = null!;
    public string EventType { get; set; } = null!;
    public string FunctionName { get; set; } = null!;
    public int? Order { get; set; }
    public bool? IsActive { get; set; }
    public string? FailureMode { get; set; }
    public int? TimeoutMs { get; set; }
    public string? FilterConditions { get; set; }
}

public class PluginExecutionLogDto
{
    public string Id { get; set; } = null!;
    public string PluginId { get; set; } = null!;
    public string? HookBindingId { get; set; }
    public string ExecutionId { get; set; } = null!;
    public string Domain { get; set; } = null!;
    public string EventType { get; set; } = null!;
    public string? EntityType { get; set; }
    public string? EntityId { get; set; }
    public int DurationMs { get; set; }
    public string Result { get; set; } = null!;
    public string? ResultAction { get; set; }
    public string? ErrorMessage { get; set; }
    public string? UserId { get; set; }
    public DateTime ExecutedAt { get; set; }
}

public class PluginNetworkConfigDto
{
    public string PluginId { get; set; } = null!;
    public string PluginName { get; set; } = null!;
    public string AccessMode { get; set; } = "None";
    public List<string> AllowedHosts { get; set; } = new();
    public int HttpTimeoutMs { get; set; } = 5000;
    public int MaxHttpRequestsPerExecution { get; set; } = 5;
    public bool RequiresApproval { get; set; }
    public bool IsApproved { get; set; }
    public string? ApprovedBy { get; set; }
    public DateTime? ApprovedAt { get; set; }
}

public class UpdateNetworkConfigRequest
{
    public string? AccessMode { get; set; }
    public List<string>? AllowedHosts { get; set; }
    public int? HttpTimeoutMs { get; set; }
    public int? MaxHttpRequestsPerExecution { get; set; }
}
