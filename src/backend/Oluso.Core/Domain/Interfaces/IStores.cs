using Oluso.Core.Domain.Entities;

namespace Oluso.Core.Domain.Interfaces;

/// <summary>
/// Store for client configuration
/// </summary>
public interface IClientStore
{
    Task<Client?> FindClientByIdAsync(string clientId, CancellationToken cancellationToken = default);
    Task<Client?> GetByIdAsync(int id, CancellationToken cancellationToken = default);

    /// <summary>
    /// Lightweight check used on every authenticated API call to validate the
    /// token's audience. Returns null if no client matches; otherwise the
    /// Enabled flag. Avoids loading the cartesian-explosion of child
    /// collections that FindClientByIdAsync pulls in.
    /// </summary>
    Task<bool?> IsClientEnabledAsync(string clientId, CancellationToken cancellationToken = default);
    Task<IEnumerable<Client>> GetAllClientsAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// Whether <paramref name="redirectUri"/> is exactly a registered redirect URI of an enabled client in the
    /// current tenant. One indexed lookup by URI; no client is loaded.
    /// </summary>
    Task<bool> IsRegisteredRedirectUriAsync(string redirectUri, CancellationToken cancellationToken = default) =>
        Task.FromResult(false);

    /// <summary>
    /// Sets the client's LastAccessed to <paramref name="now"/> unless it was already set within
    /// <paramref name="interval"/>, in one statement without loading the client. Returns whether it changed.
    /// </summary>
    Task<bool> RecordAccessAsync(string clientId, DateTime now, TimeSpan interval, CancellationToken cancellationToken = default);
    Task<Client> AddClientAsync(Client client, CancellationToken cancellationToken = default);
    Task<Client> UpdateClientAsync(Client client, CancellationToken cancellationToken = default);
    Task DeleteByIdAsync(int id, CancellationToken cancellationToken = default);

    /// <summary>
    /// Get all platform-level clients (TenantId = _platform).
    /// For SuperAdmin use only.
    /// </summary>
    Task<IEnumerable<Client>> GetPlatformClientsAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// Get a platform client by primary key.
    /// For SuperAdmin use only.
    /// </summary>
    Task<Client?> GetPlatformClientByIdAsync(int id, CancellationToken cancellationToken = default);

    /// <summary>
    /// Add a platform client.
    /// For SuperAdmin use only.
    /// </summary>
    Task<Client> AddPlatformClientAsync(Client client, CancellationToken cancellationToken = default);

    /// <summary>
    /// Update a platform client.
    /// For SuperAdmin use only.
    /// </summary>
    Task<Client> UpdatePlatformClientAsync(Client client, CancellationToken cancellationToken = default);

    /// <summary>
    /// Delete a platform client.
    /// For SuperAdmin use only.
    /// </summary>
    Task DeletePlatformClientByIdAsync(int id, CancellationToken cancellationToken = default);
}

/// <summary>
/// Store for resources (RFC 8707), identity resources, and scopes
/// </summary>
public interface IResourceStore
{
    // Identity Resources (OIDC standard - profile, email, etc.)
    Task<IEnumerable<IdentityResource>> GetAllIdentityResourcesAsync(CancellationToken cancellationToken = default);
    Task<IEnumerable<IdentityResource>> FindIdentityResourcesByScopeNameAsync(IEnumerable<string> scopeNames, CancellationToken cancellationToken = default);
    Task<IdentityResource?> GetIdentityResourceByIdAsync(int id, CancellationToken cancellationToken = default);
    Task<IdentityResource> AddIdentityResourceAsync(IdentityResource resource, CancellationToken cancellationToken = default);
    Task<IdentityResource> UpdateIdentityResourceAsync(IdentityResource resource, CancellationToken cancellationToken = default);
    Task DeleteIdentityResourceAsync(int id, CancellationToken cancellationToken = default);

    // Resources (RFC 8707 - protected resources identified by URIs)
    Task<IEnumerable<Resource>> GetAllResourcesAsync(CancellationToken cancellationToken = default);
    Task<Resource?> GetResourceByIdAsync(int id, CancellationToken cancellationToken = default);
    Task<Resource?> FindResourceByUriAsync(string uri, CancellationToken cancellationToken = default);
    Task<IEnumerable<Resource>> FindResourcesByUrisAsync(IEnumerable<string> uris, CancellationToken cancellationToken = default);

    /// <summary>Enabled resources that own at least one of the scopes (RFC 9068 §3 audience inference).</summary>
    Task<IEnumerable<Resource>> FindResourcesByScopeNamesAsync(IEnumerable<string> scopeNames, CancellationToken cancellationToken = default);
    Task<Resource> AddResourceAsync(Resource resource, CancellationToken cancellationToken = default);
    Task<Resource> UpdateResourceAsync(Resource resource, CancellationToken cancellationToken = default);
    Task DeleteResourceAsync(int id, CancellationToken cancellationToken = default);

    // API Scopes (permissions that can be requested by clients)
    Task<IEnumerable<ApiScope>> GetAllApiScopesAsync(CancellationToken cancellationToken = default);
    Task<IEnumerable<ApiScope>> FindApiScopesByNameAsync(IEnumerable<string> scopeNames, CancellationToken cancellationToken = default);

    /// <summary>
    /// The routed tenant's prefix scopes (<see cref="Oluso.Core.Services.ApiScopePrefixes"/>), with their properties
    /// and user claims. The default filters <see cref="GetAllApiScopesAsync"/>.
    /// </summary>
    async Task<IEnumerable<ApiScope>> FindPrefixApiScopesAsync(CancellationToken cancellationToken = default) =>
        (await GetAllApiScopesAsync(cancellationToken) ?? Enumerable.Empty<ApiScope>())
            .Where(Oluso.Core.Services.ApiScopePrefixes.IsPrefix)
            .ToList();

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

    /// <summary>Deletes grants whose expiration has passed. Returns the number removed.</summary>
    Task<int> RemoveExpiredAsync(CancellationToken cancellationToken = default);
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

    /// <summary>
    /// JWKS keys, keeping expired keys published for <paramref name="verificationGrace"/>
    /// after <see cref="SigningKey.ExpiresAt"/> so tokens signed just before expiry
    /// still verify, and dropping them afterwards so the set does not grow forever.
    /// </summary>
    Task<IReadOnlyList<SigningKey>> GetJwksKeysAsync(string? tenantId, TimeSpan verificationGrace, CancellationToken cancellationToken = default);
    Task<IReadOnlyList<SigningKey>> GetExpiringKeysAsync(int daysUntilExpiration, CancellationToken cancellationToken = default);

    /// <summary>
    /// Get all active/rotated signing keys across all tenants for token validation.
    /// Bypasses tenant query filters — used by SigningCredentialStore to validate
    /// tokens signed by platform clients regardless of current tenant context.
    /// </summary>
    Task<IReadOnlyList<SigningKey>> GetValidationKeysForTenantAndPlatformAsync(string? tenantId, CancellationToken cancellationToken = default);

    /// <summary>
    /// Validation keys for the tenant and the platform, including keys that expired
    /// within <paramref name="verificationGrace"/> so in-flight tokens keep validating
    /// until they expire themselves.
    /// </summary>
    Task<IReadOnlyList<SigningKey>> GetValidationKeysForTenantAndPlatformAsync(string? tenantId, TimeSpan verificationGrace, CancellationToken cancellationToken = default);

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
    Task<Tenant?> GetByCustomDomainAsync(string domain, CancellationToken cancellationToken = default);
    Task<IEnumerable<Tenant>> GetAllAsync(CancellationToken cancellationToken = default);
    Task<IEnumerable<Tenant>> GetByOrganizationAsync(string organizationId, CancellationToken cancellationToken = default);

    /// <summary>
    /// Paged tenant search with the term and scope applied in the query, for callers
    /// that must not load every tenant to find a few.
    /// </summary>
    Task<(IReadOnlyList<Tenant> Items, int TotalCount)> SearchAsync(TenantSearchQuery query, CancellationToken cancellationToken = default);
    Task<Tenant> CreateAsync(Tenant tenant, CancellationToken cancellationToken = default);
    Task<Tenant> UpdateAsync(Tenant tenant, CancellationToken cancellationToken = default);
    Task DeleteAsync(string tenantId, CancellationToken cancellationToken = default);
}

/// <summary>
/// Store for tenant memberships — the single source of truth for user↔tenant access
/// </summary>
public interface ITenantMembershipStore
{
    Task<IEnumerable<TenantMembership>> GetByTenantAsync(string tenantId, CancellationToken cancellationToken = default);
    Task<IEnumerable<TenantMembership>> GetByUserAsync(string userId, CancellationToken cancellationToken = default);
    Task<TenantMembership?> GetByTenantAndUserAsync(string tenantId, string userId, CancellationToken cancellationToken = default);
    Task<TenantMembership> AddAsync(TenantMembership membership, CancellationToken cancellationToken = default);
    Task RemoveAsync(string tenantId, string userId, CancellationToken cancellationToken = default);
}

/// <summary>
/// Store for organizations
/// </summary>
public interface IOrganizationStore
{
    Task<Organization?> GetByIdAsync(string organizationId, CancellationToken cancellationToken = default);
    Task<Organization?> GetBySlugAsync(string slug, CancellationToken cancellationToken = default);
    Task<IEnumerable<Organization>> GetAllAsync(bool includeDisabled = false, CancellationToken cancellationToken = default);

    /// <summary>Paged organisation search with the term and scope applied in the query.</summary>
    Task<(IReadOnlyList<Organization> Items, int TotalCount)> SearchAsync(OrganizationSearchQuery query, CancellationToken cancellationToken = default);
    Task<Organization> CreateAsync(Organization organization, CancellationToken cancellationToken = default);
    Task<Organization> UpdateAsync(Organization organization, CancellationToken cancellationToken = default);
    Task DeleteAsync(string organizationId, CancellationToken cancellationToken = default);
    Task<bool> SlugExistsAsync(string slug, CancellationToken cancellationToken = default);
}

/// <summary>
/// Store for organization memberships
/// </summary>
public interface IOrganizationMembershipStore
{
    Task<OrganizationMembership?> GetByIdAsync(string membershipId, CancellationToken cancellationToken = default);
    Task<OrganizationMembership?> GetByUserAndOrganizationAsync(string userId, string organizationId, CancellationToken cancellationToken = default);
    Task<IEnumerable<OrganizationMembership>> GetByOrganizationAsync(string organizationId, CancellationToken cancellationToken = default);
    Task<IEnumerable<OrganizationMembership>> GetByUserAsync(string userId, CancellationToken cancellationToken = default);
    Task<IEnumerable<Organization>> GetUserOrganizationsAsync(string userId, CancellationToken cancellationToken = default);
    Task<IEnumerable<string>> GetAllowedTenantIdsAsync(string userId, string organizationId, CancellationToken cancellationToken = default);
    Task<OrganizationMembership> CreateAsync(OrganizationMembership membership, CancellationToken cancellationToken = default);
    Task<OrganizationMembership> UpdateAsync(OrganizationMembership membership, CancellationToken cancellationToken = default);
    Task DeleteAsync(string membershipId, CancellationToken cancellationToken = default);
    Task<int> GetMemberCountAsync(string organizationId, CancellationToken cancellationToken = default);
    Task<bool> IsOwnerAsync(string userId, string organizationId, CancellationToken cancellationToken = default);
    Task<bool> IsAdminAsync(string userId, string organizationId, CancellationToken cancellationToken = default);
    Task<bool> IsMemberAsync(string userId, string organizationId, CancellationToken cancellationToken = default);
}

/// <summary>
/// Store for organization invitations
/// </summary>
public interface IOrganizationInvitationStore
{
    Task<OrganizationInvitation?> GetByIdAsync(string invitationId, CancellationToken cancellationToken = default);
    Task<OrganizationInvitation?> GetByTokenAsync(string token, CancellationToken cancellationToken = default);
    Task<IEnumerable<OrganizationInvitation>> GetByOrganizationAsync(string organizationId, InvitationStatus? status = null, CancellationToken cancellationToken = default);
    Task<IEnumerable<OrganizationInvitation>> GetByEmailAsync(string email, InvitationStatus? status = null, CancellationToken cancellationToken = default);
    Task<OrganizationInvitation?> GetPendingByEmailAndOrganizationAsync(string email, string organizationId, CancellationToken cancellationToken = default);
    Task<OrganizationInvitation> CreateAsync(OrganizationInvitation invitation, CancellationToken cancellationToken = default);
    Task<OrganizationInvitation> UpdateAsync(OrganizationInvitation invitation, CancellationToken cancellationToken = default);
    Task DeleteAsync(string invitationId, CancellationToken cancellationToken = default);
    Task<int> CleanupExpiredAsync(CancellationToken cancellationToken = default);
}

/// <summary>
/// Store for organization feature entitlements
/// </summary>
public interface IOrganizationFeatureStore
{
    Task<IReadOnlyList<OrganizationFeature>> GetFeaturesAsync(string organizationId, CancellationToken cancellationToken = default);
    Task<bool> IsFeatureEnabledAsync(string organizationId, string featureKey, CancellationToken cancellationToken = default);
    Task SetFeaturesAsync(string organizationId, IEnumerable<OrganizationFeatureUpdate> updates, string? grantedByUserId, CancellationToken cancellationToken = default);
    Task EnableFeatureAsync(string organizationId, string featureKey, string? grantedByUserId, DateTime? expiresAt = null, CancellationToken cancellationToken = default);
    Task DisableFeatureAsync(string organizationId, string featureKey, CancellationToken cancellationToken = default);
    Task<bool> HasAnyFeaturesConfiguredAsync(string organizationId, CancellationToken cancellationToken = default);

    /// <summary>
    /// Sync subscription-sourced features for an organization.
    /// Only touches features where Source = "subscription" with the given subscriptionId.
    /// Manually-granted features are left untouched.
    /// </summary>
    Task SyncSubscriptionFeaturesAsync(string organizationId, string subscriptionId, IEnumerable<OrganizationFeatureUpdate> subscriptionFeatures, CancellationToken cancellationToken = default);

    /// <summary>
    /// Remove all subscription-sourced features for an organization (e.g. on cancellation).
    /// </summary>
    Task RemoveSubscriptionFeaturesAsync(string organizationId, string subscriptionId, CancellationToken cancellationToken = default);
}

public record OrganizationFeatureUpdate(string FeatureKey, bool Enabled, DateTime? ExpiresAt = null, int? MaxSeats = null);

/// <summary>
/// Store for tenant feature entitlements.
/// Tenant features are a governance layer within org-level features:
/// org admins can further restrict which modules each tenant can access.
/// </summary>
public interface ITenantFeatureStore
{
    Task<IReadOnlyList<TenantFeature>> GetFeaturesAsync(string tenantId, CancellationToken cancellationToken = default);
    Task<bool> IsFeatureEnabledAsync(string tenantId, string featureKey, CancellationToken cancellationToken = default);
    Task<bool> HasAnyFeaturesConfiguredAsync(string tenantId, CancellationToken cancellationToken = default);
    Task SetFeaturesAsync(string tenantId, IEnumerable<TenantFeatureUpdate> updates, string? grantedByUserId, CancellationToken cancellationToken = default);
    Task EnableFeatureAsync(string tenantId, string featureKey, string? grantedByUserId, CancellationToken cancellationToken = default);
    Task DisableFeatureAsync(string tenantId, string featureKey, CancellationToken cancellationToken = default);
}

public record TenantFeatureUpdate(string FeatureKey, bool Enabled, int? MaxSeats = null);

/// <summary>
/// Store for per-user module access (seats).
/// Tracks which users have been granted access to a module within a tenant.
/// </summary>
public interface IUserModuleAccessStore
{
    Task<IReadOnlyList<UserModuleAccess>> GetAccessAsync(string tenantId, string featureKey, CancellationToken cancellationToken = default);
    Task<bool> HasAccessAsync(string tenantId, string userId, string featureKey, CancellationToken cancellationToken = default);
    Task<int> GetSeatCountAsync(string tenantId, string featureKey, CancellationToken cancellationToken = default);
    Task GrantAccessAsync(string tenantId, string userId, string featureKey, string? grantedByUserId, CancellationToken cancellationToken = default);
    Task RevokeAccessAsync(string tenantId, string userId, string featureKey, CancellationToken cancellationToken = default);
    Task<IReadOnlyList<UserModuleAccess>> GetUserModulesAsync(string tenantId, string userId, CancellationToken cancellationToken = default);
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

    /// <summary>
    /// Returns identity providers configured for a specific tenant, plus
    /// global providers (TenantId == null). Used by admin endpoints that
    /// edit a tenant whose id differs from the ambient
    /// <see cref="ITenantContext"/> — the standard
    /// <see cref="GetAllAsync(CancellationToken)"/> filters by ambient
    /// context and would return the wrong rows for SuperAdmin cross-tenant
    /// operations.
    /// </summary>
    Task<IEnumerable<IdentityProvider>> GetAllForTenantAsync(string tenantId, CancellationToken cancellationToken = default);

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
    Task<IReadOnlyList<OlusoRole>> GetByIdsAsync(IEnumerable<string> roleIds, CancellationToken cancellationToken = default);
    Task<OlusoRole?> GetByNameAsync(string name, string? tenantId, CancellationToken cancellationToken = default);
    Task<IEnumerable<OlusoRole>> GetRolesAsync(string? tenantId, bool includeSystem = true, CancellationToken cancellationToken = default);
    Task<OlusoRole> CreateAsync(OlusoRole role, CancellationToken cancellationToken = default);
    Task<OlusoRole> UpdateAsync(OlusoRole role, CancellationToken cancellationToken = default);
    Task DeleteAsync(string roleId, CancellationToken cancellationToken = default);
    Task<IEnumerable<RoleClaim>> GetRoleClaimsAsync(string roleId, CancellationToken cancellationToken = default);
    Task AddRoleClaimAsync(string roleId, string type, string value, CancellationToken cancellationToken = default);
    Task RemoveRoleClaimAsync(string roleId, string type, string value, CancellationToken cancellationToken = default);
    // Role assignments carry the tenant they apply in (OlusoUserRole.TenantId).
    // Members without a tenant parameter use the request's tenant, or at
    // platform scope: for reads, every assignment; for writes, the user's home
    // tenant. SuperAdmin and SystemAdmin apply in every tenant (GlobalRoles).

    /// <summary>
    /// Distinct users holding the role in any tenant. Guards role deletion,
    /// which removes the role's assignments everywhere.
    /// </summary>
    Task<int> GetUsersInRoleAsync(string roleId, CancellationToken cancellationToken = default);

    /// <summary>Users holding the role in the request's tenant.</summary>
    Task<IEnumerable<RoleUserInfo>> GetUsersByRoleAsync(string roleId, CancellationToken cancellationToken = default);

    /// <summary>Users holding the role in <paramref name="tenantId"/>.</summary>
    Task<IEnumerable<RoleUserInfo>> GetUsersByRoleInTenantAsync(string roleId, string tenantId, CancellationToken cancellationToken = default);

    /// <summary>Assigns the role in the request's tenant (platform scope: the user's home tenant).</summary>
    Task AssignToUserAsync(string roleId, string userId, CancellationToken cancellationToken = default);

    /// <summary>Assigns the role in the request's tenant (platform scope: the user's home tenant).</summary>
    Task AssignToUserAsync(string roleId, string userId, string? assignedBy, CancellationToken cancellationToken = default);

    /// <summary>
    /// Assigns the role in <paramref name="tenantId"/>. A tenant-owned role can
    /// only be assigned in its own tenant; anything else throws.
    /// </summary>
    Task AssignToUserInTenantAsync(string roleId, string userId, string tenantId, string? assignedBy, CancellationToken cancellationToken = default);

    /// <summary>Removes the assignment in the request's tenant (platform scope: the user's home tenant).</summary>
    Task RemoveFromUserAsync(string roleId, string userId, CancellationToken cancellationToken = default);

    /// <summary>
    /// Removes the assignment in <paramref name="tenantId"/>; assignments of the
    /// same role in other tenants are untouched.
    /// </summary>
    Task RemoveFromUserInTenantAsync(string roleId, string userId, string tenantId, CancellationToken cancellationToken = default);

    /// <summary>
    /// The roles a user holds <em>in a given tenant</em>: assignments stamped
    /// with that tenant, plus SuperAdmin/SystemAdmin wherever they were stamped.
    /// Independent of ambient tenant context.
    /// </summary>
    Task<IEnumerable<OlusoRole>> GetUserRolesInTenantAsync(
        string userId, string? tenantId, CancellationToken cancellationToken = default);
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
    public bool IsActive { get; set; }
    public string? TenantId { get; set; }
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

    /// <summary>
    /// Get the network policy for a plugin. Returns null when no policy
    /// has been set — callers should treat that as "no network access".
    /// </summary>
    Task<PluginNetworkConfig?> GetNetworkConfigAsync(string pluginId, CancellationToken cancellationToken = default);

    /// <summary>
    /// Insert or update the network policy for a plugin. Implementations
    /// must enforce that <see cref="PluginNetworkConfig.PluginId"/> refers
    /// to an existing plugin in the same tenant scope.
    /// </summary>
    Task<PluginNetworkConfig> UpsertNetworkConfigAsync(PluginNetworkConfig config, CancellationToken cancellationToken = default);
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
/// Store for tenant configuration sections (per-module settings rows).
/// </summary>
public interface ITenantConfigurationSectionStore
{
    Task<TenantConfigurationSection?> GetAsync(string tenantId, string sectionKey, CancellationToken cancellationToken = default);
    Task<IReadOnlyList<TenantConfigurationSection>> GetAllAsync(string tenantId, CancellationToken cancellationToken = default);
    Task UpsertAsync(TenantConfigurationSection section, CancellationToken cancellationToken = default);
    Task RemoveAsync(string tenantId, string sectionKey, CancellationToken cancellationToken = default);
}

/// <summary>
/// Store for tenant outbound sender profiles (replaces TenantEmailSettings singleton).
/// </summary>
public interface ISenderProfileStore
{
    Task<SenderProfile?> GetAsync(string tenantId, string id, CancellationToken cancellationToken = default);
    Task<SenderProfile?> GetDefaultAsync(string tenantId, CancellationToken cancellationToken = default);
    Task<IReadOnlyList<SenderProfile>> ListAsync(string tenantId, CancellationToken cancellationToken = default);
    Task CreateAsync(SenderProfile profile, CancellationToken cancellationToken = default);
    Task UpdateAsync(SenderProfile profile, CancellationToken cancellationToken = default);
    Task DeleteAsync(string tenantId, string id, CancellationToken cancellationToken = default);

    /// <summary>
    /// Atomically makes the named profile the tenant's default and unsets the
    /// flag on any others. Throws if the profile is missing or belongs to a
    /// different tenant.
    /// </summary>
    Task SetDefaultAsync(string tenantId, string id, CancellationToken cancellationToken = default);
}

/// <summary>
/// Store for a tenant's own application entitlements — the catalog the role
/// editor picks from and that role assignments are validated against.
/// </summary>
public interface IEntitlementStore
{
    Task<Entitlement?> GetAsync(string tenantId, string id, CancellationToken cancellationToken = default);
    Task<IReadOnlyList<Entitlement>> ListAsync(string tenantId, CancellationToken cancellationToken = default);

    /// <summary>
    /// Resolves the given names within the tenant. Used to validate a role's
    /// assignment and, at token issuance, to withhold disabled entitlements.
    /// </summary>
    Task<IReadOnlyList<Entitlement>> FindByNamesAsync(
        string? tenantId,
        IEnumerable<string> names,
        CancellationToken cancellationToken = default);

    Task CreateAsync(Entitlement entitlement, CancellationToken cancellationToken = default);
    Task UpdateAsync(Entitlement entitlement, CancellationToken cancellationToken = default);
    Task DeleteAsync(string tenantId, string id, CancellationToken cancellationToken = default);
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
    public string? OriginKind { get; set; }
    public string? OriginId { get; set; }
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

    /// <summary>The event's <see cref="Oluso.Core.Events.EventOrigin.Kind"/>.</summary>
    public string? OriginKind { get; set; }

    /// <summary>The event's <see cref="Oluso.Core.Events.EventOrigin.Id"/>.</summary>
    public string? OriginId { get; set; }

    public string? OriginName { get; set; }
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
    public int AuthType { get; set; }
    public string? AuthCredential { get; set; }

    public string? ApiVersion { get; set; }
    public string? Secret { get; set; }

    /// <summary>The OAuth client (client_id) that owns this endpoint.</summary>
    public string? ClientId { get; set; }

    /// <summary>Don't deliver events caused by <see cref="ClientId"/> itself.</summary>
    public bool SkipOwnClientEvents { get; set; } = true;

    /// <summary>When the endpoint was authorised to receive sensitive event properties; null when it isn't.</summary>
    public DateTime? SensitiveDataAuthorizedAt { get; set; }
    public string? SensitiveDataAuthorizedBy { get; set; }
    public string? SensitiveDataAuthorizedByName { get; set; }
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
    public int AuthType { get; set; }
    public string? AuthCredential { get; set; }

    public string? ApiVersion { get; set; }
    public string? ClientId { get; set; }
    public bool SkipOwnClientEvents { get; set; } = true;

    /// <summary>Authorise the endpoint for sensitive event properties, recorded against the authoriser below.</summary>
    public bool AuthorizeSensitiveData { get; set; }
    public string? SensitiveDataAuthorizedBy { get; set; }
    public string? SensitiveDataAuthorizedByName { get; set; }
}

public class UpdateWebhookEndpoint
{
    public string? Name { get; set; }
    public string? Description { get; set; }
    public string? Url { get; set; }
    public bool? Enabled { get; set; }
    public List<string>? EventTypes { get; set; }
    public Dictionary<string, string>? Headers { get; set; }
    public int? AuthType { get; set; }
    public string? AuthCredential { get; set; }

    public string? ApiVersion { get; set; }

    /// <summary>Empty string unlinks the client; null leaves it unchanged.</summary>
    public string? ClientId { get; set; }
    public bool? SkipOwnClientEvents { get; set; }

    /// <summary>True grants sensitive data (recorded against the authoriser below), false revokes it, null leaves it.</summary>
    public bool? AuthorizeSensitiveData { get; set; }
    public string? SensitiveDataAuthorizedBy { get; set; }
    public string? SensitiveDataAuthorizedByName { get; set; }
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

/// <summary>
/// Store for beta feedback submissions
/// </summary>
public interface IFeedbackStore
{
    Task<FeedbackSubmission> CreateAsync(FeedbackSubmission feedback, CancellationToken cancellationToken = default);

    /// <summary>A null tenant reads across every tenant (the platform's review of beta feedback).</summary>
    Task<FeedbackSubmission?> GetByIdAsync(string? tenantId, string id, CancellationToken cancellationToken = default);

    /// <summary>A null tenant lists every tenant's feedback.</summary>
    Task<(IReadOnlyList<FeedbackSubmission> Items, int Total)> ListAsync(
        string? tenantId, int page = 1, int pageSize = 25,
        string? category = null, string? status = null,
        CancellationToken cancellationToken = default);

    /// <summary>A null tenant finds the submission in any tenant.</summary>
    Task UpdateStatusAsync(string? tenantId, string id, string status, CancellationToken cancellationToken = default);
}

#region Telemetry Logs

/// <summary>
/// Application log entry stored in the database for telemetry purposes
/// </summary>
public class TelemetryLog : Oluso.Core.Domain.Entities.TenantEntity
{
    public long Id { get; set; }
    public DateTime Timestamp { get; set; }
    public string Level { get; set; } = null!; // Trace, Debug, Information, Warning, Error, Critical
    public string Message { get; set; } = null!;
    public string Category { get; set; } = null!; // Logger category (e.g., "Oluso.Token", "Microsoft.AspNetCore")
    public string? Exception { get; set; }
    public string? ExceptionType { get; set; }
    public string? StackTrace { get; set; }
    public string? TraceId { get; set; }
    public string? SpanId { get; set; }
    public string? RequestPath { get; set; }
    public string? RequestMethod { get; set; }
    public string? UserId { get; set; }
    public string? ClientId { get; set; }
    public string? MachineName { get; set; }
    public string? Properties { get; set; } // JSON-serialized additional properties
}

/// <summary>
/// Service for querying telemetry logs (read-only). The default
/// implementation reads from the in-process database; alternative
/// implementations can swap to an external store like Loki when configured.
/// </summary>
public interface ITelemetryLogService
{
    bool IsEnabled { get; }

    /// <summary>
    /// Identifier of the backing store ("database", "loki", "none"). Used by
    /// the admin UI to render the right "no logs" / "not configured" copy
    /// and to label the source of the data the user is looking at.
    /// </summary>
    string Backend { get; }

    Task<TelemetryLogQueryResult> QueryAsync(TelemetryLogQuery query, CancellationToken cancellationToken = default);
    Task<TelemetryLog?> GetByIdAsync(long id, CancellationToken cancellationToken = default);
    Task<IEnumerable<string>> GetCategoriesAsync(string? tenantId, CancellationToken cancellationToken = default);
    Task<int> PurgeOldLogsAsync(string? tenantId, DateTime cutoffDate, CancellationToken cancellationToken = default);
}

/// <summary>
/// Store for writing telemetry logs
/// </summary>
public interface ITelemetryLogStore
{
    /// <summary>
    /// Writes a telemetry log entry
    /// </summary>
    Task WriteAsync(TelemetryLog log, CancellationToken cancellationToken = default);

    /// <summary>
    /// Writes multiple telemetry log entries (for batch processing)
    /// </summary>
    Task WriteBatchAsync(IEnumerable<TelemetryLog> logs, CancellationToken cancellationToken = default);
}

public class TelemetryLogQuery
{
    public string? TenantId { get; set; }
    public string? Level { get; set; }
    public string? Category { get; set; }
    public string? SearchTerm { get; set; }
    public string? TraceId { get; set; }
    public string? UserId { get; set; }
    public string? ClientId { get; set; }
    public bool? HasException { get; set; }
    public DateTime? StartDate { get; set; }
    public DateTime? EndDate { get; set; }
    public string SortBy { get; set; } = "Timestamp";
    public bool SortDescending { get; set; } = true;
    public int Page { get; set; } = 1;
    public int PageSize { get; set; } = 50;
}

public class TelemetryLogQueryResult
{
    public IEnumerable<TelemetryLog> Items { get; set; } = new List<TelemetryLog>();
    public int TotalCount { get; set; }
    public int Page { get; set; }
    public int PageSize { get; set; }
    public int TotalPages => (int)Math.Ceiling((double)TotalCount / PageSize);
}

#endregion

#region File Attachments

public interface IFileAttachmentStore
{
    Task<FileAttachment> CreateAsync(FileAttachment attachment, CancellationToken cancellationToken = default);
    Task<FileAttachment> UpdateAsync(FileAttachment attachment, CancellationToken cancellationToken = default);
    Task<FileAttachment?> GetByIdAsync(string id, CancellationToken cancellationToken = default);

    /// <summary>
    /// Get all attachments for a specific entity (e.g. all files on a work order).
    /// Plaintext path — only matches rows where the registered access provider
    /// did NOT opt in to entity-reference protection.
    /// </summary>
    Task<IReadOnlyList<FileAttachment>> GetByEntityAsync(
        string entityType, string entityId, CancellationToken cancellationToken = default);

    /// <summary>
    /// Get all attachments for a protected entity, looking up by the HMAC
    /// blind index. The caller (typically <c>IFileAttachmentService</c>) is
    /// responsible for computing <paramref name="entityIdHashes"/> from the
    /// plaintext entity ID via <c>IEntityReferenceProtector.BlindIndexCandidatesAsync</c>;
    /// a row matching any of them is returned.
    /// </summary>
    Task<IReadOnlyList<FileAttachment>> GetByEntityHashAsync(
        string entityType, IReadOnlyCollection<string> entityIdHashes, CancellationToken cancellationToken = default);

    /// <summary>
    /// Get attachments for a sub-entity (e.g. files on a specific work order item).
    /// </summary>
    Task<IReadOnlyList<FileAttachment>> GetBySubEntityAsync(
        string entityType, string entityId,
        string subEntityType, string subEntityId, CancellationToken cancellationToken = default);

    /// <summary>
    /// Get personal (unattached) files for a user, optionally filtered by folder.
    /// </summary>
    Task<IReadOnlyList<FileAttachment>> GetPersonalFilesAsync(
        string userId, string? folder = null, CancellationToken cancellationToken = default);

    /// <summary>
    /// Get distinct folder names for a user's personal files.
    /// </summary>
    Task<IReadOnlyList<string>> GetUserFoldersAsync(string userId, CancellationToken cancellationToken = default);

    Task<bool> SoftDeleteAsync(string id, CancellationToken cancellationToken = default);

    /// <summary>
    /// Hard-delete all attachments for an entity (used when the parent entity is permanently deleted).
    /// Returns storage paths so the caller can clean up blob storage.
    /// </summary>
    Task<IReadOnlyList<string>> HardDeleteByEntityAsync(
        string entityType, string entityId, CancellationToken cancellationToken = default);

    /// <summary>
    /// Every attachment of an entity in the tenant, soft-deleted ones among them: matched by the plaintext id, or by
    /// any of <paramref name="entityIdHashes"/> for an entity whose references are protected (empty otherwise).
    /// </summary>
    Task<IReadOnlyList<FileAttachment>> GetAllForEntityAsync(
        string tenantId, string entityType, string entityId, IReadOnlyCollection<string> entityIdHashes, CancellationToken cancellationToken = default);

    /// <summary>Removes the attachment's row for good; false when there is none.</summary>
    Task<bool> HardDeleteAsync(string id, CancellationToken cancellationToken = default);

    /// <summary>
    /// The tenants and plaintext entity ids that attachments of the entity type are kept on, soft-deleted ones among
    /// them, ordered by tenant then id, the first <paramref name="take"/> after <paramref name="after"/>.
    /// </summary>
    Task<IReadOnlyList<Oluso.Core.Storage.FileAttachmentOwner>> GetOwnersAsync(
        string entityType, Oluso.Core.Storage.FileAttachmentOwner? after, int take, CancellationToken cancellationToken = default);
}

#endregion

/// <summary>Filter for <see cref="ITenantStore.SearchAsync"/>.</summary>
public sealed class TenantSearchQuery
{
    /// <summary>Matched against name, display name, identifier and description.</summary>
    public string? Search { get; init; }

    /// <summary>Restrict to these organisations; null means every tenant, an empty set matches nothing.</summary>
    public IReadOnlyCollection<string>? OrganizationIds { get; init; }

    public bool? Enabled { get; init; }
    public int Skip { get; init; }
    public int Take { get; init; } = 50;
}

/// <summary>Filter for <see cref="IOrganizationStore.SearchAsync"/>.</summary>
public sealed class OrganizationSearchQuery
{
    /// <summary>Matched against name, slug and description.</summary>
    public string? Search { get; init; }

    /// <summary>Restrict to these organisations; null means every organisation, an empty set matches nothing.</summary>
    public IReadOnlyCollection<string>? OrganizationIds { get; init; }

    public bool? Enabled { get; init; }
    public int Skip { get; init; }
    public int Take { get; init; } = 50;
}
