using Microsoft.EntityFrameworkCore;
using Oluso.Core.Domain.Entities;
using Oluso.Core.Domain.Interfaces;

namespace Oluso.EntityFramework;

/// <summary>
/// Interface that your DbContext must implement to use Oluso EF stores.
/// Implement this interface on your existing DbContext to integrate Oluso.
/// </summary>
public interface IOlusoDbContext
{
    // Client configuration
    DbSet<Client> Clients { get; }
    DbSet<ClientSecret> ClientSecrets { get; }
    DbSet<ClientGrantType> ClientGrantTypes { get; }
    DbSet<ClientRedirectUri> ClientRedirectUris { get; }
    DbSet<ClientPostLogoutRedirectUri> ClientPostLogoutRedirectUris { get; }
    DbSet<ClientScope> ClientScopes { get; }
    DbSet<ClientClaim> ClientClaims { get; }
    DbSet<ClientCorsOrigin> ClientCorsOrigins { get; }
    DbSet<ClientProperty> ClientProperties { get; }
    DbSet<ClientIdPRestriction> ClientIdPRestrictions { get; }
    DbSet<ClientAllowedRole> ClientAllowedRoles { get; }
    DbSet<ClientAllowedUser> ClientAllowedUsers { get; }

    // Resources
    DbSet<ApiResource> ApiResources { get; }
    DbSet<ApiResourceSecret> ApiResourceSecrets { get; }
    DbSet<ApiResourceScope> ApiResourceScopes { get; }
    DbSet<ApiResourceClaim> ApiResourceClaims { get; }
    DbSet<ApiResourceProperty> ApiResourceProperties { get; }
    DbSet<ApiScope> ApiScopes { get; }
    DbSet<ApiScopeClaim> ApiScopeClaims { get; }
    DbSet<ApiScopeProperty> ApiScopeProperties { get; }
    DbSet<IdentityResource> IdentityResources { get; }
    DbSet<IdentityResourceClaim> IdentityResourceClaims { get; }
    DbSet<IdentityResourceProperty> IdentityResourceProperties { get; }

    // Operational
    DbSet<PersistedGrant> PersistedGrants { get; }
    DbSet<DeviceFlowCode> DeviceFlowCodes { get; }
    DbSet<Consent> Consents { get; }
    DbSet<SigningKey> SigningKeys { get; }
    DbSet<ServerSideSession> ServerSideSessions { get; }
    DbSet<PushedAuthorizationRequest> PushedAuthorizationRequests { get; }

    // Multi-tenancy (optional - null if not using multi-tenancy)
    DbSet<Tenant>? Tenants { get; }

    // External identity providers
    DbSet<IdentityProvider> IdentityProviders { get; }

    // User Journeys
    DbSet<JourneyPolicyEntity> JourneyPolicies { get; }
    DbSet<JourneyStateEntity> JourneyStates { get; }
    DbSet<JourneySubmissionEntity> JourneySubmissions { get; }

    // Audit logs
    DbSet<AuditLog> AuditLogs { get; }

    // Webhooks
    DbSet<WebhookEndpointEntity> WebhookEndpoints { get; }
    DbSet<WebhookEventSubscriptionEntity> WebhookEventSubscriptions { get; }
    DbSet<WebhookDeliveryEntity> WebhookDeliveries { get; }

    // FIDO2/Passkeys
    DbSet<Fido2CredentialEntity> Fido2Credentials { get; }

    // Plugin metadata
    DbSet<PluginMetadata> PluginMetadata { get; }

    // CIBA (Client Initiated Backchannel Authentication)
    DbSet<CibaRequest> CibaRequests { get; }

    Task<int> SaveChangesAsync(CancellationToken cancellationToken = default);
}
