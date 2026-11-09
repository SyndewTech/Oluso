namespace Oluso.Enterprise.Scim.Models.Admin;

public class ScimProviderResponse
{
    public string Id { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
    public string? Description { get; set; }
    public bool IsEnabled { get; set; }
    public string Flavor { get; set; } = string.Empty;
    public string BaseUrl { get; set; } = string.Empty;
    public string AuthType { get; set; } = string.Empty;
    public string? OAuthTokenEndpoint { get; set; }
    public string? OAuthClientId { get; set; }
    public string? OAuthScope { get; set; }

    /// <summary>A secret is stored. The value itself is never returned.</summary>
    public bool HasSecret { get; set; }
    public DateTime? SecretUpdatedAt { get; set; }

    public bool SyncUsers { get; set; }
    public bool SyncGroups { get; set; }
    /// <summary>roles, teams or both.</summary>
    public string GroupSource { get; set; } = string.Empty;
    public List<string> ScopeRoleIds { get; set; } = new();
    public List<string> ScopeTeamIds { get; set; } = new();
    public string UserNameSource { get; set; } = string.Empty;
    public string? UserNameExpression { get; set; }
    public string DeprovisionMode { get; set; } = string.Empty;
    public int ReconcileIntervalMinutes { get; set; }

    /// <summary>The inbound SCIM client that is this same system; its changes are not pushed back here.</summary>
    public string? LinkedScimClientId { get; set; }

    public DateTime CreatedAt { get; set; }
    public DateTime? UpdatedAt { get; set; }
    public DateTime? LastReconcileAt { get; set; }
    public string? LastReconcileSummary { get; set; }
    public DateTime? LastSuccessAt { get; set; }
    public DateTime? LastErrorAt { get; set; }
    public string? LastError { get; set; }
    public long SuccessCount { get; set; }
    public long ErrorCount { get; set; }
}

public class CreateScimProviderRequest
{
    public string Name { get; set; } = string.Empty;
    public string? Description { get; set; }
    public bool IsEnabled { get; set; } = true;
    public string Flavor { get; set; } = "generic";
    public string BaseUrl { get; set; } = string.Empty;
    public string AuthType { get; set; } = "bearer";
    public string? OAuthTokenEndpoint { get; set; }
    public string? OAuthClientId { get; set; }
    public string? OAuthScope { get; set; }

    /// <summary>Bearer token or OAuth client secret. Optional here; can be set later.</summary>
    public string? Secret { get; set; }

    public bool SyncUsers { get; set; } = true;
    public bool SyncGroups { get; set; }
    public string? GroupSource { get; set; }
    public List<string>? ScopeRoleIds { get; set; }
    public List<string>? ScopeTeamIds { get; set; }
    public string? UserNameSource { get; set; }
    public string? UserNameExpression { get; set; }
    public string DeprovisionMode { get; set; } = "deactivate";
    public int ReconcileIntervalMinutes { get; set; } = 360;

    /// <summary>The inbound SCIM client that is this same system; its changes are not pushed back here.</summary>
    public string? LinkedScimClientId { get; set; }
}

public class UpdateScimProviderRequest
{
    public string Name { get; set; } = string.Empty;
    public string? Description { get; set; }
    public bool IsEnabled { get; set; } = true;
    public string Flavor { get; set; } = "generic";
    public string BaseUrl { get; set; } = string.Empty;
    public string AuthType { get; set; } = "bearer";
    public string? OAuthTokenEndpoint { get; set; }
    public string? OAuthClientId { get; set; }
    public string? OAuthScope { get; set; }
    public bool SyncUsers { get; set; } = true;
    public bool SyncGroups { get; set; }
    public string? GroupSource { get; set; }
    public List<string>? ScopeRoleIds { get; set; }
    public List<string>? ScopeTeamIds { get; set; }
    public string? UserNameSource { get; set; }
    public string? UserNameExpression { get; set; }
    public string DeprovisionMode { get; set; } = "deactivate";
    public int ReconcileIntervalMinutes { get; set; } = 360;

    /// <summary>The inbound SCIM client that is this same system; its changes are not pushed back here.</summary>
    public string? LinkedScimClientId { get; set; }
}

public class SetScimProviderSecretRequest
{
    public string Secret { get; set; } = string.Empty;
}

public class ScimProviderFlavorResponse
{
    public string Key { get; set; } = string.Empty;
    public string DisplayName { get; set; } = string.Empty;
    public string Description { get; set; } = string.Empty;
    public string? Notes { get; set; }
    public string DefaultUserNameSource { get; set; } = string.Empty;
    public bool RequiresEmailUserName { get; set; }
    public bool SupportsPhoneNumbers { get; set; }
    public bool SupportsEnterpriseExtension { get; set; }
    public bool SupportsGroups { get; set; }
    public List<ScimProviderDefaultMappingResponse> DefaultUserMappings { get; set; } = new();
}

public class ScimProviderDefaultMappingResponse
{
    public string ScimAttribute { get; set; } = string.Empty;
    public string InternalProperty { get; set; } = string.Empty;
}

public class ScimProviderOptionsResponse
{
    public List<ScimProviderFlavorResponse> Flavors { get; set; } = new();
    public List<string> InternalProperties { get; set; } = new();
    public List<string> Transformations { get; set; } = new();
    public List<string> AuthTypes { get; set; } = new();
    public List<string> UserNameSources { get; set; } = new();
    public List<string> DeprovisionModes { get; set; } = new();
    public List<string> GroupSources { get; set; } = new();
}

public class ScimProviderAttributeMappingResponse
{
    public string Id { get; set; } = string.Empty;
    public string ScimProviderId { get; set; } = string.Empty;
    public string ScimAttribute { get; set; } = string.Empty;
    public string InternalProperty { get; set; } = string.Empty;
    public string? Expression { get; set; }
    public string? Condition { get; set; }
    public string? Transformation { get; set; }
    public string? DefaultValue { get; set; }
    public bool IsEnabled { get; set; }
    public DateTime CreatedAt { get; set; }
    public DateTime? UpdatedAt { get; set; }
}

public class SaveScimProviderAttributeMappingRequest
{
    public string ScimAttribute { get; set; } = string.Empty;
    /// <summary>Required unless <see cref="Expression"/> is set.</summary>
    public string? InternalProperty { get; set; }
    /// <summary>Scriban template computing the value; takes precedence over the property.</summary>
    public string? Expression { get; set; }
    /// <summary>Scriban boolean expression; the attribute is omitted when false.</summary>
    public string? Condition { get; set; }
    public string? Transformation { get; set; }
    public string? DefaultValue { get; set; }
    public bool IsEnabled { get; set; } = true;
}

/// <summary>
/// Dry-run of the mapping configuration against a real user: the payload
/// that would be pushed, without pushing it.
/// </summary>
public class ScimProviderPreviewRequest
{
    public string UserId { get; set; } = string.Empty;
}

public class ScimProviderPreviewResponse
{
    public string UserId { get; set; } = string.Empty;
    public string? UserName { get; set; }
    public bool InScope { get; set; }
    public string? Error { get; set; }
    public System.Text.Json.Nodes.JsonObject? Payload { get; set; }
}

public class ScimProviderTestResponse
{
    public bool Success { get; set; }
    public string Message { get; set; } = string.Empty;
    public bool DiscoveryReachable { get; set; }
    public int DiscoveryStatusCode { get; set; }
    public bool AuthenticationOk { get; set; }
    public int AuthenticationStatusCode { get; set; }
    public string? AuthenticationError { get; set; }
    public bool? PatchSupported { get; set; }
    public bool? BulkSupported { get; set; }
    public bool? FilterSupported { get; set; }
    public int? FilterMaxResults { get; set; }
    public bool? EtagSupported { get; set; }
    public List<string> AuthenticationSchemes { get; set; } = new();
}

public class ScimProviderReconcileResponse
{
    public int UsersQueued { get; set; }
    public int UserRemovalsQueued { get; set; }
    public int GroupsQueued { get; set; }
    public int GroupRemovalsQueued { get; set; }
    public string Summary { get; set; } = string.Empty;
}

public class ScimProviderQueueSummaryResponse
{
    public int Pending { get; set; }
    public int Processing { get; set; }
    public int Failed { get; set; }
    public int Dead { get; set; }
    public int SucceededLast24h { get; set; }
    public DateTime? OldestPendingAt { get; set; }
}

public class ScimProviderQueueItemResponse
{
    public string Id { get; set; } = string.Empty;
    public string ResourceType { get; set; } = string.Empty;
    public string InternalId { get; set; } = string.Empty;
    public string Operation { get; set; } = string.Empty;
    public string Status { get; set; } = string.Empty;
    public int Attempts { get; set; }
    public DateTime NextAttemptAt { get; set; }
    public int? LastStatusCode { get; set; }
    public string? LastError { get; set; }
    public string? Reason { get; set; }
    public DateTime CreatedAt { get; set; }
    public DateTime? UpdatedAt { get; set; }
    public DateTime? CompletedAt { get; set; }
}

public class ScimProviderQueueResponse
{
    public ScimProviderQueueSummaryResponse Summary { get; set; } = new();
    public int Total { get; set; }
    public int Skip { get; set; }
    public int Take { get; set; }
    public List<ScimProviderQueueItemResponse> Items { get; set; } = new();
}

public class ScimProviderLogResponse
{
    public string Id { get; set; } = string.Empty;
    public string Operation { get; set; } = string.Empty;
    public string? ResourceType { get; set; }
    public string? InternalId { get; set; }
    public string? RemoteId { get; set; }
    public string Method { get; set; } = string.Empty;
    public string Path { get; set; } = string.Empty;
    public int StatusCode { get; set; }
    public bool Success { get; set; }
    public string? ErrorMessage { get; set; }
    public long DurationMs { get; set; }
    public DateTime Timestamp { get; set; }
}

public class ScimProviderResourceResponse
{
    public string Id { get; set; } = string.Empty;
    public string ResourceType { get; set; } = string.Empty;
    public string InternalId { get; set; } = string.Empty;
    public string RemoteId { get; set; } = string.Empty;
    public string? RemoteVersion { get; set; }
    public bool IsDeprovisioned { get; set; }
    public DateTime CreatedAt { get; set; }
    public DateTime? LastSyncedAt { get; set; }
}

/// <summary>A request an identity provider made to this organisation's SCIM endpoint.</summary>
public class ScimClientLogResponse
{
    public string Id { get; set; } = string.Empty;
    public string Method { get; set; } = string.Empty;
    public string Path { get; set; } = string.Empty;
    public string? ResourceType { get; set; }
    public string? ResourceId { get; set; }
    public string Operation { get; set; } = string.Empty;
    public int StatusCode { get; set; }
    public bool Success { get; set; }
    public string? ErrorMessage { get; set; }
    public string? ClientIp { get; set; }
    public long DurationMs { get; set; }
    public DateTime Timestamp { get; set; }
}
