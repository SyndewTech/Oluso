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
    DbSet<ClientAuthorizeParameter> ClientAuthorizeParameters { get; }
    DbSet<ClientCorsOrigin> ClientCorsOrigins { get; }
    DbSet<ClientProperty> ClientProperties { get; }
    DbSet<ClientIdPRestriction> ClientIdPRestrictions { get; }
    DbSet<ClientAllowedRole> ClientAllowedRoles { get; }
    DbSet<ClientAllowedUser> ClientAllowedUsers { get; }

    // Resources (RFC 8707)
    DbSet<Resource> Resources { get; }
    DbSet<ResourceScope> ResourceScopes { get; }
    DbSet<ResourceClaim> ResourceClaims { get; }

    // API Scopes
    DbSet<ApiScope> ApiScopes { get; }
    DbSet<ApiScopeClaim> ApiScopeClaims { get; }
    DbSet<ApiScopeProperty> ApiScopeProperties { get; }

    // Identity Resources
    DbSet<IdentityResource> IdentityResources { get; }
    DbSet<IdentityResourceClaim> IdentityResourceClaims { get; }
    DbSet<IdentityResourceProperty> IdentityResourceProperties { get; }

    // Operational
    DbSet<PersistedGrant> PersistedGrants { get; }
    DbSet<DeviceFlowCode> DeviceFlowCodes { get; }
    DbSet<Consent> Consents { get; }
    DbSet<SigningKey> SigningKeys { get; }
    DbSet<KeyRotationConfig> KeyRotationConfigs { get; }
    DbSet<ServerSideSession> ServerSideSessions { get; }
    DbSet<PushedAuthorizationRequest> PushedAuthorizationRequests { get; }

    // Multi-tenancy (optional - null if not using multi-tenancy)
    DbSet<Tenant>? Tenants { get; }
    DbSet<TenantMembership> TenantMemberships { get; }
    DbSet<CustomDomainVerification> CustomDomainVerifications { get; }
    DbSet<TenantEmailDomain> TenantEmailDomains { get; }

    // Organizations (multi-tenant management)
    DbSet<Organization> Organizations { get; }
    DbSet<OrganizationMembership> OrganizationMemberships { get; }
    DbSet<OrganizationInvitation> OrganizationInvitations { get; }

    // Teams — platform-level addressable groups of users
    DbSet<Team> Teams { get; }
    DbSet<TeamMembership> TeamMemberships { get; }

    // External identity providers
    DbSet<IdentityProvider> IdentityProviders { get; }

    // User Journeys
    DbSet<JourneyPolicyEntity> JourneyPolicies { get; }
    DbSet<JourneyStateEntity> JourneyStates { get; }
    DbSet<JourneySubmissionEntity> JourneySubmissions { get; }

    // Audit logs
    DbSet<AuditLog> AuditLogs { get; }

    // Telemetry logs (application logging)
    DbSet<TelemetryLog> TelemetryLogs { get; }

    // Webhooks
    DbSet<WebhookEndpointEntity> WebhookEndpoints { get; }
    DbSet<WebhookEventSubscriptionEntity> WebhookEventSubscriptions { get; }
    DbSet<WebhookDeliveryEntity> WebhookDeliveries { get; }

    // Plugin metadata
    DbSet<PluginMetadata> PluginMetadata { get; }

    // Plugin hook bindings and execution
    DbSet<PluginHookBinding> PluginHookBindings { get; }
    DbSet<PluginNetworkConfig> PluginNetworkConfigs { get; }
    DbSet<PluginExecutionLog> PluginExecutionLogs { get; }
    DbSet<PluginConfigOverride> PluginConfigOverrides { get; }

    // CIBA (Client Initiated Backchannel Authentication)
    DbSet<CibaRequest> CibaRequests { get; }

    // Feedback
    DbSet<FeedbackSubmission> FeedbackSubmissions { get; }

    // Tenant configuration sections (per-module settings stored as separate rows)
    DbSet<TenantConfigurationSection> TenantConfigurationSections { get; }

    // Outbound sender profiles (replaces TenantEmailSettings singleton)
    DbSet<SenderProfile> SenderProfiles { get; }
    DbSet<Entitlement> Entitlements { get; }

    // File attachments
    DbSet<FileAttachment> FileAttachments { get; }

    // Per-user notification preferences (snooze, mute, quiet hours).
    // Core primitive — meaningful whether any module is installed or not.
    DbSet<UserNotificationProfile> UserNotificationProfiles { get; }

    // Single-use action tokens (magic-link decide, vendor confirmations,
    // candidate offer-accept, etc.). Hash-only — raw tokens never persist.
    DbSet<ActionToken> ActionTokens { get; }

    // Conversation messages — flat threads keyed by (EntityType, EntityId).
    // Modules attach their own threads by reusing the pair.
    DbSet<ConversationMessage> ConversationMessages { get; }

    /// <summary>Tenant terminology renames (Terminology Center overrides).</summary>
    DbSet<Oluso.Core.Terminology.TerminologyOverride> TerminologyOverrides { get; }

    // Per-(message, user) read receipts for @mentions. Anti-joined with
    // ConversationMessages to compute "unread mentions for me" in the
    // Action Center.
    DbSet<ConversationMentionRead> ConversationMentionReads { get; }

    // Hashes of users' recent passwords for the tenant password-history policy.
    DbSet<PasswordHistoryEntry> PasswordHistory { get; }
    DbSet<WhatsAppContact> WhatsAppContacts { get; }
    DbSet<WhatsAppTemplate> WhatsAppTemplates { get; }
    DbSet<WhatsAppInboundMessage> WhatsAppInboundMessages { get; }
    DbSet<WhatsAppAccount> WhatsAppAccounts { get; }

    Task<int> SaveChangesAsync(CancellationToken cancellationToken = default);
}
