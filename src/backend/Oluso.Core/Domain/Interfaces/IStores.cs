using Oluso.Core.Domain.Entities;

namespace Oluso.Core.Domain.Interfaces;

/// <summary>
/// Store for client configuration
/// </summary>
public interface IClientStore
{
    Task<Client?> FindClientByIdAsync(string clientId, CancellationToken cancellationToken = default);
    Task<IEnumerable<Client>> GetAllClientsAsync(CancellationToken cancellationToken = default);
    Task<Client> AddClientAsync(Client client, CancellationToken cancellationToken = default);
    Task<Client> UpdateClientAsync(Client client, CancellationToken cancellationToken = default);
    Task DeleteClientAsync(string clientId, CancellationToken cancellationToken = default);
}

/// <summary>
/// Store for API and identity resources
/// </summary>
public interface IResourceStore
{
    // Identity Resources
    Task<IEnumerable<IdentityResource>> GetAllIdentityResourcesAsync(CancellationToken cancellationToken = default);
    Task<IEnumerable<IdentityResource>> FindIdentityResourcesByScopeNameAsync(IEnumerable<string> scopeNames, CancellationToken cancellationToken = default);
    Task<IdentityResource?> GetIdentityResourceByIdAsync(int id, CancellationToken cancellationToken = default);
    Task<IdentityResource> AddIdentityResourceAsync(IdentityResource resource, CancellationToken cancellationToken = default);
    Task<IdentityResource> UpdateIdentityResourceAsync(IdentityResource resource, CancellationToken cancellationToken = default);
    Task DeleteIdentityResourceAsync(int id, CancellationToken cancellationToken = default);

    // API Resources
    Task<IEnumerable<ApiResource>> GetAllApiResourcesAsync(CancellationToken cancellationToken = default);
    Task<IEnumerable<ApiResource>> FindApiResourcesByScopeNameAsync(IEnumerable<string> scopeNames, CancellationToken cancellationToken = default);
    Task<IEnumerable<ApiResource>> FindApiResourcesByNameAsync(IEnumerable<string> apiResourceNames, CancellationToken cancellationToken = default);
    Task<ApiResource?> GetApiResourceByIdAsync(int id, CancellationToken cancellationToken = default);
    Task<ApiResource> AddApiResourceAsync(ApiResource resource, CancellationToken cancellationToken = default);
    Task<ApiResource> UpdateApiResourceAsync(ApiResource resource, CancellationToken cancellationToken = default);
    Task DeleteApiResourceAsync(int id, CancellationToken cancellationToken = default);

    // API Scopes
    Task<IEnumerable<ApiScope>> GetAllApiScopesAsync(CancellationToken cancellationToken = default);
    Task<IEnumerable<ApiScope>> FindApiScopesByNameAsync(IEnumerable<string> scopeNames, CancellationToken cancellationToken = default);
    Task<ApiScope?> GetApiScopeByIdAsync(int id, CancellationToken cancellationToken = default);
    Task<ApiScope> AddApiScopeAsync(ApiScope scope, CancellationToken cancellationToken = default);
    Task<ApiScope> UpdateApiScopeAsync(ApiScope scope, CancellationToken cancellationToken = default);
    Task DeleteApiScopeAsync(int id, CancellationToken cancellationToken = default);
}

/// <summary>
/// Store for persisted grants (tokens, codes, etc.)
/// </summary>
public interface IPersistedGrantStore
{
    Task StoreAsync(PersistedGrant grant, CancellationToken cancellationToken = default);
    Task<PersistedGrant?> GetAsync(string key, CancellationToken cancellationToken = default);
    Task<IEnumerable<PersistedGrant>> GetAllAsync(PersistedGrantFilter filter, CancellationToken cancellationToken = default);
    Task RemoveAsync(string key, CancellationToken cancellationToken = default);
    Task RemoveAllAsync(PersistedGrantFilter filter, CancellationToken cancellationToken = default);
    Task RemoveAllBySubjectAndClientAsync(string subjectId, string clientId, CancellationToken cancellationToken = default);
}

/// <summary>
/// Filter for persisted grant queries
/// </summary>
public class PersistedGrantFilter
{
    public string? SubjectId { get; set; }
    public string? SessionId { get; set; }
    public string? ClientId { get; set; }
    public string? Type { get; set; }
}

/// <summary>
/// Store for signing keys with support for tenant/client scoping and rotation
/// </summary>
public interface ISigningKeyStore
{
    // Basic CRUD
    Task<SigningKey?> GetByIdAsync(string id, CancellationToken cancellationToken = default);
    Task StoreAsync(SigningKey key, CancellationToken cancellationToken = default);
    Task UpdateAsync(SigningKey key, CancellationToken cancellationToken = default);
    Task DeleteAsync(string id, CancellationToken cancellationToken = default);

    // Querying
    Task<SigningKey?> GetActiveSigningKeyAsync(string? tenantId, string? clientId = null, CancellationToken cancellationToken = default);
    Task<IReadOnlyList<SigningKey>> GetByTenantAsync(string? tenantId, CancellationToken cancellationToken = default);
    Task<IReadOnlyList<SigningKey>> GetByClientAsync(string? tenantId, string clientId, CancellationToken cancellationToken = default);
    Task<IReadOnlyList<SigningKey>> GetJwksKeysAsync(string? tenantId, CancellationToken cancellationToken = default);
    Task<IReadOnlyList<SigningKey>> GetExpiringKeysAsync(int daysUntilExpiration, CancellationToken cancellationToken = default);

    // Usage tracking
    Task IncrementUsageAsync(string id, CancellationToken cancellationToken = default);

    // Rotation configuration
    Task<KeyRotationConfig?> GetRotationConfigAsync(string? tenantId, string? clientId = null, CancellationToken cancellationToken = default);
    Task SaveRotationConfigAsync(KeyRotationConfig config, CancellationToken cancellationToken = default);
}

/// <summary>
/// Store for authorization codes
/// </summary>
public interface IAuthorizationCodeStore
{
    Task StoreAsync(AuthorizationCode code, CancellationToken cancellationToken = default);
    Task<AuthorizationCode?> GetAsync(string code, CancellationToken cancellationToken = default);
    Task RemoveAsync(string code, CancellationToken cancellationToken = default);
}

/// <summary>
/// Store for user consent
/// </summary>
public interface IConsentStore
{
    Task StoreConsentAsync(Consent consent, CancellationToken cancellationToken = default);
    Task<Consent?> GetConsentAsync(string subjectId, string clientId, CancellationToken cancellationToken = default);
    Task<IEnumerable<Consent>> GetConsentsBySubjectAsync(string subjectId, CancellationToken cancellationToken = default);
    Task RevokeConsentAsync(string subjectId, string clientId, CancellationToken cancellationToken = default);
    Task RevokeAllConsentsAsync(string subjectId, CancellationToken cancellationToken = default);
    Task<bool> HasConsentAsync(string subjectId, string clientId, IEnumerable<string> requestedScopes, CancellationToken cancellationToken = default);
}

/// <summary>
/// Store for device flow codes
/// </summary>
public interface IDeviceFlowStore
{
    Task StoreDeviceAuthorizationAsync(string deviceCode, string userCode, DeviceFlowCode data, CancellationToken cancellationToken = default);
    Task<DeviceFlowCode?> FindByUserCodeAsync(string userCode, CancellationToken cancellationToken = default);
    Task<DeviceFlowCode?> FindByDeviceCodeAsync(string deviceCode, CancellationToken cancellationToken = default);
    Task UpdateByUserCodeAsync(string userCode, DeviceFlowCode data, CancellationToken cancellationToken = default);
    Task RemoveByDeviceCodeAsync(string deviceCode, CancellationToken cancellationToken = default);
}

/// <summary>
/// Store for tenants (multi-tenancy)
/// </summary>
public interface ITenantStore
{
    Task<Tenant?> GetByIdAsync(string tenantId, CancellationToken cancellationToken = default);
    Task<Tenant?> GetByIdentifierAsync(string identifier, CancellationToken cancellationToken = default);
    Task<IEnumerable<Tenant>> GetAllAsync(CancellationToken cancellationToken = default);
    Task<Tenant> CreateAsync(Tenant tenant, CancellationToken cancellationToken = default);
    Task<Tenant> UpdateAsync(Tenant tenant, CancellationToken cancellationToken = default);
    Task DeleteAsync(string tenantId, CancellationToken cancellationToken = default);
}

/// <summary>
/// Store for pushed authorization requests (PAR) - RFC 9126
/// </summary>
public interface IPushedAuthorizationStore
{
    /// <summary>
    /// Store a pushed authorization request
    /// </summary>
    Task StoreAsync(PushedAuthorizationRequest request, CancellationToken cancellationToken = default);

    /// <summary>
    /// Get a pushed authorization request by request_uri.
    /// Returns null if not found or expired
    /// </summary>
    Task<PushedAuthorizationRequest?> GetAsync(string requestUri, CancellationToken cancellationToken = default);

    /// <summary>
    /// Remove a pushed authorization request (after use or explicitly)
    /// </summary>
    Task RemoveAsync(string requestUri, CancellationToken cancellationToken = default);

    /// <summary>
    /// Remove all expired pushed authorization requests
    /// </summary>
    Task RemoveExpiredAsync(CancellationToken cancellationToken = default);
}

/// <summary>
/// Store for external identity providers
/// </summary>
public interface IIdentityProviderStore
{
    Task<IdentityProvider?> GetByIdAsync(int id, CancellationToken cancellationToken = default);
    Task<IdentityProvider?> GetBySchemeAsync(string scheme, CancellationToken cancellationToken = default);
    Task<IEnumerable<IdentityProvider>> GetAllAsync(CancellationToken cancellationToken = default);
    Task<IEnumerable<IdentityProvider>> GetByProviderTypeAsync(ExternalProviderType providerType, CancellationToken cancellationToken = default);
    Task<bool> SchemeExistsGloballyAsync(string scheme, CancellationToken cancellationToken = default);
    Task<IdentityProvider> AddAsync(IdentityProvider provider, CancellationToken cancellationToken = default);
    Task<IdentityProvider> UpdateAsync(IdentityProvider provider, CancellationToken cancellationToken = default);
    Task DeleteAsync(int id, CancellationToken cancellationToken = default);
}

/// <summary>
/// Store for roles
/// </summary>
public interface IRoleStore
{
    Task<OlusoRole?> GetByIdAsync(string roleId, CancellationToken cancellationToken = default);
    Task<OlusoRole?> GetByNameAsync(string name, string? tenantId, CancellationToken cancellationToken = default);
    Task<IEnumerable<OlusoRole>> GetRolesAsync(string? tenantId, bool includeSystem = true, CancellationToken cancellationToken = default);
    Task<OlusoRole> CreateAsync(OlusoRole role, CancellationToken cancellationToken = default);
    Task<OlusoRole> UpdateAsync(OlusoRole role, CancellationToken cancellationToken = default);
    Task DeleteAsync(string roleId, CancellationToken cancellationToken = default);
    Task<IEnumerable<RoleClaim>> GetRoleClaimsAsync(string roleId, CancellationToken cancellationToken = default);
    Task AddRoleClaimAsync(string roleId, string type, string value, CancellationToken cancellationToken = default);
    Task RemoveRoleClaimAsync(string roleId, string type, string value, CancellationToken cancellationToken = default);
    Task<int> GetUsersInRoleAsync(string roleId, CancellationToken cancellationToken = default);
    Task<IEnumerable<RoleUserInfo>> GetUsersByRoleAsync(string roleId, CancellationToken cancellationToken = default);
}

public class RoleClaim
{
    public string Type { get; set; } = null!;
    public string Value { get; set; } = null!;
}

public class RoleUserInfo
{
    public string Id { get; set; } = null!;
    public string UserName { get; set; } = null!;
    public string Email { get; set; } = null!;
    public string? DisplayName { get; set; }
}

/// <summary>
/// Store for WASM plugins (metadata in database, bytes in file storage)
/// </summary>
public interface IPluginStore
{
    /// <summary>
    /// Get all plugins available to a tenant (tenant-specific + global)
    /// </summary>
    Task<IEnumerable<PluginMetadata>> GetAvailablePluginsAsync(string? tenantId, CancellationToken cancellationToken = default);

    /// <summary>
    /// Get plugin metadata by name
    /// </summary>
    Task<PluginMetadata?> GetPluginInfoAsync(string pluginName, string? tenantId, CancellationToken cancellationToken = default);

    /// <summary>
    /// Get plugin metadata by ID
    /// </summary>
    Task<PluginMetadata?> GetByIdAsync(string id, CancellationToken cancellationToken = default);

    /// <summary>
    /// Get plugin WASM bytes
    /// </summary>
    Task<byte[]?> GetPluginBytesAsync(string pluginName, string? tenantId, CancellationToken cancellationToken = default);

    /// <summary>
    /// Save/upload a plugin (metadata to database, bytes to file storage)
    /// </summary>
    Task<PluginMetadata> SavePluginAsync(string pluginName, byte[] wasmBytes, PluginMetadata metadata, string? tenantId, CancellationToken cancellationToken = default);

    /// <summary>
    /// Update plugin metadata only (no bytes change)
    /// </summary>
    Task<PluginMetadata> UpdatePluginMetadataAsync(PluginMetadata metadata, CancellationToken cancellationToken = default);

    /// <summary>
    /// Delete a plugin
    /// </summary>
    Task<bool> DeletePluginAsync(string pluginName, string? tenantId, CancellationToken cancellationToken = default);

    /// <summary>
    /// Check if a plugin exists
    /// </summary>
    Task<bool> ExistsAsync(string pluginName, string? tenantId, CancellationToken cancellationToken = default);

    /// <summary>
    /// Increment execution count and update last executed time
    /// </summary>
    Task RecordExecutionAsync(string pluginName, string? tenantId, double executionMs, CancellationToken cancellationToken = default);

    /// <summary>
    /// Search plugins by name, tags, or description
    /// </summary>
    Task<IEnumerable<PluginMetadata>> SearchAsync(string query, string? tenantId, CancellationToken cancellationToken = default);
}

/// <summary>
/// Legacy PluginInfo for backward compatibility with existing code
/// </summary>
public class PluginInfo
{
    public string Name { get; set; } = null!;
    public string? DisplayName { get; set; }
    public string? Description { get; set; }
    public string? Version { get; set; }
    public string? Author { get; set; }
    public PluginScope Scope { get; set; }
    public string? TenantId { get; set; }
    public long SizeBytes { get; set; }
    public DateTime CreatedAt { get; set; }
    public DateTime? UpdatedAt { get; set; }
    public List<string>? RequiredClaims { get; set; }
    public List<string>? OutputClaims { get; set; }
    public Dictionary<string, object>? ConfigSchema { get; set; }

    /// <summary>
    /// Convert from PluginMetadata entity
    /// </summary>
    public static PluginInfo FromMetadata(PluginMetadata metadata)
    {
        return new PluginInfo
        {
            Name = metadata.Name,
            DisplayName = metadata.DisplayName,
            Description = metadata.Description,
            Version = metadata.Version,
            Author = metadata.Author,
            Scope = metadata.Scope,
            TenantId = metadata.TenantId,
            SizeBytes = metadata.SizeBytes,
            CreatedAt = metadata.CreatedAt,
            UpdatedAt = metadata.UpdatedAt,
            RequiredClaims = metadata.GetRequiredClaimsList(),
            OutputClaims = metadata.GetOutputClaimsList()
        };
    }
}

/// <summary>
/// Service for querying audit logs (read-only)
/// </summary>
public interface IAuditLogService
{
    bool IsEnabled { get; }
    Task<AuditLogQueryResult> QueryAsync(AuditLogQuery query, CancellationToken cancellationToken = default);
    Task<AuditLog?> GetByIdAsync(long id, CancellationToken cancellationToken = default);
    Task<IEnumerable<AuditLog>> GetByResourceAsync(string? tenantId, string resourceType, string resourceId, int limit, CancellationToken cancellationToken = default);
    Task<IEnumerable<AuditLog>> GetBySubjectAsync(string? tenantId, string subjectId, int limit, CancellationToken cancellationToken = default);
    Task<int> PurgeOldLogsAsync(string? tenantId, DateTime cutoffDate, CancellationToken cancellationToken = default);
}

/// <summary>
/// Store for writing audit logs
/// </summary>
public interface IAuditLogStore
{
    /// <summary>
    /// Writes an audit log entry
    /// </summary>
    Task WriteAsync(AuditLog auditLog, CancellationToken cancellationToken = default);

    /// <summary>
    /// Writes multiple audit log entries (for batch processing)
    /// </summary>
    Task WriteBatchAsync(IEnumerable<AuditLog> auditLogs, CancellationToken cancellationToken = default);
}

public class AuditLogQuery
{
    public string? TenantId { get; set; }
    public string? Action { get; set; }
    public string? Category { get; set; }
    public string? EventType { get; set; }
    public string? SubjectId { get; set; }
    public string? ResourceType { get; set; }
    public string? ResourceId { get; set; }
    public string? ClientId { get; set; }
    public bool? Success { get; set; }
    public string? SearchTerm { get; set; }
    public DateTime? StartDate { get; set; }
    public DateTime? EndDate { get; set; }
    public string SortBy { get; set; } = "Timestamp";
    public bool SortDescending { get; set; } = true;
    public int Page { get; set; } = 1;
    public int PageSize { get; set; } = 50;
}

public class AuditLogQueryResult
{
    public IEnumerable<AuditLog> Items { get; set; } = new List<AuditLog>();
    public int TotalCount { get; set; }
    public int Page { get; set; }
    public int PageSize { get; set; }
    public int TotalPages => (int)Math.Ceiling((double)TotalCount / PageSize);
}

public class AuditLog : Oluso.Core.Domain.Entities.TenantEntity
{
    public long Id { get; set; }
    public DateTime Timestamp { get; set; }
    public string EventType { get; set; } = null!;
    public string Category { get; set; } = null!;
    public string? Action { get; set; }
    public string? SubjectId { get; set; }
    public string? SubjectName { get; set; }
    public string? SubjectEmail { get; set; }
    public string? ResourceType { get; set; }
    public string? ResourceId { get; set; }
    public string? ResourceName { get; set; }
    public string? ClientId { get; set; }
    public string? IpAddress { get; set; }
    public string? UserAgent { get; set; }
    public bool Success { get; set; }
    public string? ErrorMessage { get; set; }
    public string? Details { get; set; }
    public string? Reason { get; set; }
    public string? ActivityId { get; set; }
}

/// <summary>
/// Store for webhooks
/// </summary>
public interface IWebhookStore
{
    Task<IEnumerable<WebhookEndpoint>> GetEndpointsAsync(string tenantId, CancellationToken cancellationToken = default);
    Task<WebhookEndpoint?> GetEndpointAsync(string endpointId, CancellationToken cancellationToken = default);
    Task<WebhookEndpoint> CreateEndpointAsync(string tenantId, CreateWebhookEndpoint request, CancellationToken cancellationToken = default);
    Task<WebhookEndpoint> UpdateEndpointAsync(string endpointId, UpdateWebhookEndpoint request, CancellationToken cancellationToken = default);
    Task DeleteEndpointAsync(string endpointId, CancellationToken cancellationToken = default);
    Task<string> RotateSecretAsync(string endpointId, CancellationToken cancellationToken = default);
    Task<IEnumerable<WebhookDelivery>> GetDeliveryHistoryAsync(string endpointId, int limit, CancellationToken cancellationToken = default);
    Task<WebhookDelivery?> GetDeliveryAsync(string deliveryId, CancellationToken cancellationToken = default);
}

public class WebhookEndpoint
{
    public string Id { get; set; } = null!;
    public string TenantId { get; set; } = null!;
    public string Name { get; set; } = null!;
    public string? Description { get; set; }
    public string Url { get; set; } = null!;
    public bool Enabled { get; set; }
    public List<string> EventTypes { get; set; } = new();
    public Dictionary<string, string>? Headers { get; set; }
    public string? ApiVersion { get; set; }
    public string? Secret { get; set; }
    public DateTime CreatedAt { get; set; }
    public DateTime? UpdatedAt { get; set; }
}

public class CreateWebhookEndpoint
{
    public string Name { get; set; } = null!;
    public string? Description { get; set; }
    public string Url { get; set; } = null!;
    public bool Enabled { get; set; } = true;
    public List<string> EventTypes { get; set; } = new();
    public Dictionary<string, string>? Headers { get; set; }
    public string? ApiVersion { get; set; }
}

public class UpdateWebhookEndpoint
{
    public string? Name { get; set; }
    public string? Description { get; set; }
    public string? Url { get; set; }
    public bool? Enabled { get; set; }
    public List<string>? EventTypes { get; set; }
    public Dictionary<string, string>? Headers { get; set; }
    public string? ApiVersion { get; set; }
}

public class WebhookDelivery
{
    public string Id { get; set; } = null!;
    public string EndpointId { get; set; } = null!;
    public string EventType { get; set; } = null!;
    public WebhookDeliveryStatus Status { get; set; }
    public int HttpStatusCode { get; set; }
    public string? Error { get; set; }
    public int AttemptCount { get; set; }
    public DateTime CreatedAt { get; set; }
    public DateTime? DeliveredAt { get; set; }
}

public enum WebhookDeliveryStatus
{
    Pending,
    Success,
    Failed,
    Retrying
}

/// <summary>
/// Store for FIDO2/WebAuthn credentials
/// </summary>
public interface IFido2CredentialStore
{
    /// <summary>
    /// Get a credential by its ID
    /// </summary>
    Task<Fido2CredentialEntity?> GetByIdAsync(string id, CancellationToken cancellationToken = default);

    /// <summary>
    /// Get a credential by its credential ID (from the authenticator)
    /// </summary>
    Task<Fido2CredentialEntity?> GetByCredentialIdAsync(string credentialId, CancellationToken cancellationToken = default);

    /// <summary>
    /// Get all credentials for a user
    /// </summary>
    Task<IReadOnlyList<Fido2CredentialEntity>> GetByUserIdAsync(string userId, CancellationToken cancellationToken = default);

    /// <summary>
    /// Get all active credentials for a user
    /// </summary>
    Task<IReadOnlyList<Fido2CredentialEntity>> GetActiveByUserIdAsync(string userId, CancellationToken cancellationToken = default);

    /// <summary>
    /// Check if a credential ID already exists
    /// </summary>
    Task<bool> ExistsAsync(string credentialId, CancellationToken cancellationToken = default);

    /// <summary>
    /// Store a new credential
    /// </summary>
    Task AddAsync(Fido2CredentialEntity credential, CancellationToken cancellationToken = default);

    /// <summary>
    /// Update an existing credential (counter, last used, etc.)
    /// </summary>
    Task UpdateAsync(Fido2CredentialEntity credential, CancellationToken cancellationToken = default);

    /// <summary>
    /// Remove a credential
    /// </summary>
    Task RemoveAsync(string id, CancellationToken cancellationToken = default);

    /// <summary>
    /// Update the signature counter and last used timestamp
    /// </summary>
    Task UpdateCounterAsync(string credentialId, uint newCounter, CancellationToken cancellationToken = default);
}

