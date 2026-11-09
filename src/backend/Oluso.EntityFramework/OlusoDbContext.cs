using Microsoft.AspNetCore.Identity.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Oluso.Core;
using Oluso.Core.Data;
using Oluso.Core.Domain.Entities;
using Oluso.Core.Domain.Interfaces;
using Oluso.EntityFramework.Configuration;
using System.Linq.Expressions;

namespace Oluso.EntityFramework;

/// <summary>
/// Built-in DbContext for Oluso with multi-tenant support. Use this if you don't have an existing DbContext.
/// If you have an existing DbContext, implement IOlusoDbContext instead.
///
/// Features:
/// - Automatic tenant query filters (entities are filtered by current tenant)
/// - Auto-assignment of TenantId on new entities
/// - Tenant-scoped unique indexes for users and roles
/// </summary>
public class OlusoDbContext : IdentityDbContext<
    OlusoUser,
    OlusoRole,
    string,
    OlusoUserClaim,
    OlusoUserRole,
    OlusoUserLogin,
    OlusoRoleClaim,
    OlusoUserToken>, IOlusoDbContext, IMigratableDbContext
{
    /// <inheritdoc />
    public string MigrationName => "Oluso";

    /// <inheritdoc />
    /// <remarks>
    /// Core OlusoDbContext has order 0 (migrates first before any plugins).
    /// </remarks>
    public int MigrationOrder => 0;

    private ITenantContext? _tenantContext;
    private HashSet<(Type entityType, string id)>? _preValidatedForeignKeys;
    private ILogger? _validationLogger;

    /// <summary>
    /// When true, <see cref="Interceptors.ReservedAuthInterceptor"/> skips its
    /// guard checks. Only for trusted seed/migration code — controllers must
    /// never call this.
    /// </summary>
    public bool SuppressReservedGuard { get; private set; }

    /// <summary>
    /// Temporarily suppresses the reserved-entity interceptor for the duration
    /// of seed or migration code. Returns a disposable that restores the guard.
    /// </summary>
    public IDisposable BeginSeedMode()
    {
        SuppressReservedGuard = true;
        return new SeedModeScope(this);
    }

    private sealed class SeedModeScope(OlusoDbContext ctx) : IDisposable
    {
        public void Dispose() => ctx.SuppressReservedGuard = false;
    }

    public OlusoDbContext(DbContextOptions<OlusoDbContext> options) : base(options)
    {
    }

    /// <summary>
    /// Constructor for derived provider-specific contexts (e.g., OlusoDbContextSqlite).
    /// </summary>
    protected OlusoDbContext(DbContextOptions options) : base(options)
    {
    }

    /// <summary>
    /// Live tenant id read at query time from the application service provider
    /// configured on DbContextOptions. Global query filters reference this via
    /// a closure lambda; EF parameterizes the property access and reads the
    /// current value on every query, so no model cache invalidation is needed
    /// and provider-specific subclasses don't need to take ITenantContext in
    /// their constructors.
    /// </summary>
    public string? CurrentTenantId
    {
        get
        {
            // Cache the resolved accessor — it's a singleton, the lookup is cheap
            // but property accesses fire often during query compilation.
            _tenantContext ??= this.GetService<IDbContextOptions>()
                .FindExtension<CoreOptionsExtension>()?
                .ApplicationServiceProvider?
                .GetService<ITenantContext>();
            return _tenantContext?.TenantId;
        }
    }

    // Client configuration
    public DbSet<Client> Clients => Set<Client>();
    public DbSet<ClientSecret> ClientSecrets => Set<ClientSecret>();
    public DbSet<ClientGrantType> ClientGrantTypes => Set<ClientGrantType>();
    public DbSet<ClientRedirectUri> ClientRedirectUris => Set<ClientRedirectUri>();
    public DbSet<ClientPostLogoutRedirectUri> ClientPostLogoutRedirectUris => Set<ClientPostLogoutRedirectUri>();
    public DbSet<ClientScope> ClientScopes => Set<ClientScope>();
    public DbSet<ClientClaim> ClientClaims => Set<ClientClaim>();
    public DbSet<ClientAuthorizeParameter> ClientAuthorizeParameters => Set<ClientAuthorizeParameter>();
    public DbSet<ClientCorsOrigin> ClientCorsOrigins => Set<ClientCorsOrigin>();
    public DbSet<ClientProperty> ClientProperties => Set<ClientProperty>();
    public DbSet<ClientIdPRestriction> ClientIdPRestrictions => Set<ClientIdPRestriction>();
    public DbSet<ClientAllowedRole> ClientAllowedRoles => Set<ClientAllowedRole>();
    public DbSet<ClientAllowedUser> ClientAllowedUsers => Set<ClientAllowedUser>();

    // Resources (RFC 8707)
    public DbSet<Resource> Resources => Set<Resource>();
    public DbSet<ResourceScope> ResourceScopes => Set<ResourceScope>();
    public DbSet<ResourceClaim> ResourceClaims => Set<ResourceClaim>();

    // API Scopes
    public DbSet<ApiScope> ApiScopes => Set<ApiScope>();
    public DbSet<ApiScopeClaim> ApiScopeClaims => Set<ApiScopeClaim>();
    public DbSet<ApiScopeProperty> ApiScopeProperties => Set<ApiScopeProperty>();

    // Identity Resources
    public DbSet<IdentityResource> IdentityResources => Set<IdentityResource>();
    public DbSet<IdentityResourceClaim> IdentityResourceClaims => Set<IdentityResourceClaim>();
    public DbSet<IdentityResourceProperty> IdentityResourceProperties => Set<IdentityResourceProperty>();

    // Operational
    public DbSet<PersistedGrant> PersistedGrants => Set<PersistedGrant>();
    public DbSet<DeviceFlowCode> DeviceFlowCodes => Set<DeviceFlowCode>();
    public DbSet<Consent> Consents => Set<Consent>();
    public DbSet<SigningKey> SigningKeys => Set<SigningKey>();
    public DbSet<KeyRotationConfig> KeyRotationConfigs => Set<KeyRotationConfig>();
    public DbSet<ServerSideSession> ServerSideSessions => Set<ServerSideSession>();
    public DbSet<PushedAuthorizationRequest> PushedAuthorizationRequests => Set<PushedAuthorizationRequest>();

    // Multi-tenancy
    public DbSet<Tenant>? Tenants => Set<Tenant>();
    public DbSet<TenantMembership> TenantMemberships => Set<TenantMembership>();
    public DbSet<CustomDomainVerification> CustomDomainVerifications => Set<CustomDomainVerification>();
    public DbSet<TenantEmailDomain> TenantEmailDomains => Set<TenantEmailDomain>();

    // Organizations (multi-tenant management)
    public DbSet<Organization> Organizations => Set<Organization>();
    public DbSet<OrganizationMembership> OrganizationMemberships => Set<OrganizationMembership>();
    public DbSet<OrganizationInvitation> OrganizationInvitations => Set<OrganizationInvitation>();
    public DbSet<OrganizationFeature> OrganizationFeatures => Set<OrganizationFeature>();
    public DbSet<TenantFeature> TenantFeatures => Set<TenantFeature>();
    public DbSet<TenantModuleSeed> TenantModuleSeeds => Set<TenantModuleSeed>();
    public DbSet<UserModuleAccess> UserModuleAccess => Set<UserModuleAccess>();

    // Teams — platform-level addressable groups of users
    public DbSet<Team> Teams => Set<Team>();
    public DbSet<TeamMembership> TeamMemberships => Set<TeamMembership>();

    // External identity providers
    public DbSet<IdentityProvider> IdentityProviders => Set<IdentityProvider>();

    // User Journeys
    public DbSet<JourneyPolicyEntity> JourneyPolicies => Set<JourneyPolicyEntity>();
    public DbSet<JourneyStateEntity> JourneyStates => Set<JourneyStateEntity>();
    public DbSet<JourneySubmissionEntity> JourneySubmissions => Set<JourneySubmissionEntity>();

    // Audit logs
    public DbSet<AuditLog> AuditLogs => Set<AuditLog>();

    // Telemetry logs (application logging)
    public DbSet<TelemetryLog> TelemetryLogs => Set<TelemetryLog>();

    // Webhooks
    public DbSet<WebhookEndpointEntity> WebhookEndpoints => Set<WebhookEndpointEntity>();
    public DbSet<WebhookEventSubscriptionEntity> WebhookEventSubscriptions => Set<WebhookEventSubscriptionEntity>();
    public DbSet<WebhookDeliveryEntity> WebhookDeliveries => Set<WebhookDeliveryEntity>();

    // Plugin metadata
    public DbSet<PluginMetadata> PluginMetadata => Set<PluginMetadata>();

    // Plugin hook bindings and execution
    public DbSet<PluginHookBinding> PluginHookBindings => Set<PluginHookBinding>();
    public DbSet<PluginNetworkConfig> PluginNetworkConfigs => Set<PluginNetworkConfig>();
    public DbSet<PluginExecutionLog> PluginExecutionLogs => Set<PluginExecutionLog>();
    public DbSet<PluginConfigOverride> PluginConfigOverrides => Set<PluginConfigOverride>();

    // CIBA (Client Initiated Backchannel Authentication)
    public DbSet<CibaRequest> CibaRequests => Set<CibaRequest>();

    // Feedback
    public DbSet<FeedbackSubmission> FeedbackSubmissions => Set<FeedbackSubmission>();

    // Tenant configuration sections
    public DbSet<TenantConfigurationSection> TenantConfigurationSections => Set<TenantConfigurationSection>();

    // Outbound sender profiles (replaces TenantEmailSettings singleton)
    public DbSet<SenderProfile> SenderProfiles => Set<SenderProfile>();
    public DbSet<Entitlement> Entitlements => Set<Entitlement>();

    // File attachments
    public DbSet<FileAttachment> FileAttachments => Set<FileAttachment>();

    // Per-user notification preferences
    public DbSet<UserNotificationProfile> UserNotificationProfiles => Set<UserNotificationProfile>();

    // Single-use action tokens
    public DbSet<ActionToken> ActionTokens => Set<ActionToken>();

    // Conversation messages
    public DbSet<ConversationMessage> ConversationMessages => Set<ConversationMessage>();

    // Terminology: tenant renames of module vocabulary keys (Terminology Center).
    public DbSet<Oluso.Core.Terminology.TerminologyOverride> TerminologyOverrides => Set<Oluso.Core.Terminology.TerminologyOverride>();

    // @mention read receipts
    public DbSet<ConversationMentionRead> ConversationMentionReads => Set<ConversationMentionRead>();

    // Hashes of users' recent passwords for the tenant password-history policy.
    public DbSet<PasswordHistoryEntry> PasswordHistory => Set<PasswordHistoryEntry>();

    // WhatsApp channel: contacts (consent + session window), synced templates,
    // inbound messages (replay protection) and phone-number-id → tenant map.
    public DbSet<WhatsAppContact> WhatsAppContacts => Set<WhatsAppContact>();
    public DbSet<WhatsAppTemplate> WhatsAppTemplates => Set<WhatsAppTemplate>();
    public DbSet<WhatsAppInboundMessage> WhatsAppInboundMessages => Set<WhatsAppInboundMessage>();
    public DbSet<WhatsAppAccount> WhatsAppAccounts => Set<WhatsAppAccount>();

    protected override void OnModelCreating(ModelBuilder builder)
    {
        base.OnModelCreating(builder);

        // Remove default Identity unique indexes (we replace with tenant-scoped ones)
        // The default IdentityDbContext creates UserNameIndex and RoleNameIndex as unique global indexes
        var userEntity = builder.Entity<OlusoUser>();
        var roleEntity = builder.Entity<OlusoRole>();

        // Remove default global unique index on UserName (replaced by tenant-scoped)
        var existingUserNameIndex = userEntity.Metadata.FindIndex(
            new[] { userEntity.Metadata.FindProperty(nameof(OlusoUser.NormalizedUserName))! });
        if (existingUserNameIndex != null)
        {
            userEntity.Metadata.RemoveIndex(existingUserNameIndex);
        }

        // Remove default global unique index on RoleName (replaced by tenant-scoped)
        var existingRoleNameIndex = roleEntity.Metadata.FindIndex(
            new[] { roleEntity.Metadata.FindProperty(nameof(OlusoRole.NormalizedName))! });
        if (existingRoleNameIndex != null)
        {
            roleEntity.Metadata.RemoveIndex(existingRoleNameIndex);
        }

        // Apply Oluso entity configurations
        builder.ApplyConfiguration(new ClientConfiguration());
        // Exact-URI lookups (IClientStore.IsRegisteredRedirectUriAsync): bounded so every provider can index it.
        builder.Entity<ClientRedirectUri>(redirect =>
        {
            redirect.Property(r => r.RedirectUri).HasMaxLength(2000);
            redirect.HasIndex(r => r.RedirectUri);
        });
        builder.ApplyConfiguration(new ClientAuthorizeParameterConfiguration());
        builder.ApplyConfiguration(new ResourceConfiguration());
        builder.ApplyConfiguration(new ApiScopeConfiguration());
        builder.ApplyConfiguration(new IdentityResourceConfiguration());
        builder.ApplyConfiguration(new PersistedGrantConfiguration());
        builder.ApplyConfiguration(new DeviceFlowCodeConfiguration());
        builder.ApplyConfiguration(new ConsentConfiguration());
        builder.ApplyConfiguration(new SigningKeyConfiguration());
        builder.ApplyConfiguration(new KeyRotationConfigConfiguration());
        builder.ApplyConfiguration(new PushedAuthorizationRequestConfiguration());
        builder.ApplyConfiguration(new TenantConfiguration());
        builder.ApplyConfiguration(new TenantMembershipConfiguration());
        builder.ApplyConfiguration(new CustomDomainVerificationConfiguration());
        builder.ApplyConfiguration(new TenantEmailDomainConfiguration());
        builder.ApplyConfiguration(new OlusoUserConfiguration());
        builder.ApplyConfiguration(new OlusoRoleConfiguration());
        builder.ApplyConfiguration(new OlusoUserLoginConfiguration());
        builder.ApplyConfiguration(new OlusoUserClaimConfiguration());
        builder.ApplyConfiguration(new OlusoUserRoleConfiguration());
        builder.ApplyConfiguration(new OlusoRoleClaimConfiguration());
        builder.ApplyConfiguration(new OlusoUserTokenConfiguration());
        builder.ApplyConfiguration(new IdentityProviderConfiguration());
        builder.ApplyConfiguration(new JourneyPolicyEntityConfiguration());
        builder.ApplyConfiguration(new JourneyStateEntityConfiguration());
        builder.ApplyConfiguration(new JourneySubmissionEntityConfiguration());
        builder.ApplyConfiguration(new AuditLogConfiguration());
        builder.ApplyConfiguration(new TelemetryLogConfiguration());
        builder.ApplyConfiguration(new WebhookEndpointConfiguration());
        builder.ApplyConfiguration(new WebhookEventSubscriptionConfiguration());
        builder.ApplyConfiguration(new WebhookDeliveryConfiguration());
        builder.ApplyConfiguration(new PluginMetadataConfiguration());
        builder.ApplyConfiguration(new PluginHookBindingConfiguration());
        builder.ApplyConfiguration(new PluginNetworkConfigConfiguration());
        builder.ApplyConfiguration(new PluginExecutionLogConfiguration());
        builder.ApplyConfiguration(new PluginConfigOverrideConfiguration());

        // Feedback
        builder.ApplyConfiguration(new FeedbackSubmissionConfiguration());

        // Organization configurations
        builder.ApplyConfiguration(new OrganizationConfiguration());
        builder.ApplyConfiguration(new OrganizationMembershipConfiguration());
        builder.ApplyConfiguration(new OrganizationInvitationConfiguration());
        builder.ApplyConfiguration(new OrganizationFeatureConfiguration());
        builder.ApplyConfiguration(new TeamConfiguration());
        builder.ApplyConfiguration(new TeamMembershipConfiguration());
        builder.ApplyConfiguration(new TenantFeatureConfiguration());
        builder.ApplyConfiguration(new TenantModuleSeedConfiguration());
        builder.ApplyConfiguration(new TenantConfigurationSectionConfiguration());
        builder.ApplyConfiguration(new SenderProfileConfiguration());
        builder.ApplyConfiguration(new EntitlementConfiguration());
        builder.ApplyConfiguration(new FileAttachmentConfiguration());
        builder.ApplyConfiguration(new UserModuleAccessConfiguration());
        builder.ApplyConfiguration(new UserNotificationProfileConfiguration());
        builder.ApplyConfiguration(new ActionTokenConfiguration());
        builder.ApplyConfiguration(new ConversationMessageConfiguration());
        builder.ApplyConfiguration(new TerminologyOverrideConfiguration());
        builder.ApplyConfiguration(new ConversationMentionReadConfiguration());
        builder.ApplyConfiguration(new PasswordHistoryEntryConfiguration());
        builder.ApplyConfiguration(new WhatsAppContactConfiguration());
        builder.ApplyConfiguration(new WhatsAppTemplateConfiguration());
        builder.ApplyConfiguration(new WhatsAppInboundMessageConfiguration());
        builder.ApplyConfiguration(new WhatsAppAccountConfiguration());

        // Apply global tenant filters to all tenant entities
        ApplyTenantQueryFilters(builder);
    }

    private void ApplyTenantQueryFilters(ModelBuilder builder)
    {
        // Reference CurrentTenantId via closure capture of `this`. The C#
        // compiler emits the property access in the lambda body, EF treats
        // that as a parameter, and the value is read at QUERY time from the
        // executing DbContext instance — not baked into the cached model.
        foreach (var entityType in builder.Model.GetEntityTypes())
        {
            if (typeof(TenantEntity).IsAssignableFrom(entityType.ClrType))
            {
                // Build the equivalent of: e => CurrentTenantId == null
                //                            || e.TenantId == CurrentTenantId
                //                            || e.TenantId == null
                // dynamically for any TenantEntity-derived ClrType.
                var parameter = Expression.Parameter(entityType.ClrType, "e");
                var entityTenant = Expression.Property(parameter, nameof(TenantEntity.TenantId));
                var ctxTenant = Expression.Property(
                    Expression.Constant(this, typeof(OlusoDbContext)),
                    nameof(CurrentTenantId));
                var nullString = Expression.Constant(null, typeof(string));

                var ctxIsNull = Expression.Equal(ctxTenant, nullString);
                var equalTenant = Expression.Equal(entityTenant, ctxTenant);
                var entityIsNull = Expression.Equal(entityTenant, nullString);
                var filter = Expression.OrElse(ctxIsNull,
                    Expression.OrElse(equalTenant, entityIsNull));

                builder.Entity(entityType.ClrType)
                    .HasQueryFilter(Expression.Lambda(filter, parameter));
            }

            // Special handling for OlusoUser (not derived from TenantEntity).
            // The platform tenant is a meta-tenant: when the request resolves to
            // _platform, all users are visible so platform clients (admin-portal,
            // workspace-portal) can authenticate users from any tenant.
            if (entityType.ClrType == typeof(OlusoUser))
            {
                builder.Entity<OlusoUser>().HasQueryFilter(u =>
                    CurrentTenantId == null
                    || CurrentTenantId == PlatformConstants.TenantId
                    || u.TenantId == CurrentTenantId);
            }

            // Special handling for OlusoRole (not derived from TenantEntity).
            // Same platform-meta-tenant rule as OlusoUser — platform-scoped
            // requests need to see role assignments across tenants.
            if (entityType.ClrType == typeof(OlusoRole))
            {
                builder.Entity<OlusoRole>().HasQueryFilter(r =>
                    CurrentTenantId == null
                    || CurrentTenantId == PlatformConstants.TenantId
                    || r.TenantId == CurrentTenantId
                    || r.TenantId == null);
            }

            // Same platform-meta-tenant rule for OAuth grants (refresh tokens,
            // authorization codes, reference tokens all live in this table). They are
            // stamped with the user's tenant, but the platform issuer host mints and
            // refreshes tokens for users of every tenant, so a refresh arriving there
            // must find the grant. A tenant host still sees only its own.
            if (entityType.ClrType == typeof(PersistedGrant))
            {
                builder.Entity<PersistedGrant>().HasQueryFilter(g =>
                    CurrentTenantId == null
                    || CurrentTenantId == PlatformConstants.TenantId
                    || g.TenantId == CurrentTenantId
                    || g.TenantId == null);
            }

        }
    }

    /// <summary>
    /// Marks a foreign key reference as already validated by the caller.
    /// </summary>
    public void MarkForeignKeyValidated<TPrincipal>(string? id) where TPrincipal : TenantEntity
    {
        if (id == null) return;
        _preValidatedForeignKeys ??= new();
        _preValidatedForeignKeys.Add((typeof(TPrincipal), id));
    }

    /// <summary>
    /// Sets an optional logger for FK validation warnings.
    /// </summary>
    public void SetValidationLogger(ILogger logger) => _validationLogger = logger;

    public override int SaveChanges()
    {
        SetTenantId();
        ValidateTenantForeignKeysAsync(default).GetAwaiter().GetResult();
        return base.SaveChanges();
    }

    public override async Task<int> SaveChangesAsync(CancellationToken cancellationToken = default)
    {
        SetTenantId();
        await ValidateTenantForeignKeysAsync(cancellationToken);
        return await base.SaveChangesAsync(cancellationToken);
    }

    private async Task ValidateTenantForeignKeysAsync(CancellationToken cancellationToken)
    {
        try
        {
            await TenantForeignKeyValidator.ValidateAsync(
                this, CurrentTenantId, _preValidatedForeignKeys, _validationLogger, cancellationToken);
        }
        finally
        {
            _preValidatedForeignKeys?.Clear();
        }
    }

    private void SetTenantId()
    {
        foreach (var entry in ChangeTracker.Entries<TenantEntity>().Where(e => e.State == EntityState.Added))
        {
            ApplyTenantIdGuard(entry.Entity, e => e.TenantId, (e, v) => e.TenantId = v, isAuditLog: entry.Entity is AuditLog);
        }

        foreach (var entry in ChangeTracker.Entries<OlusoUser>().Where(e => e.State == EntityState.Added))
        {
            ApplyTenantIdGuard(entry.Entity, e => e.TenantId, (e, v) => e.TenantId = v, isAuditLog: false);
        }

        foreach (var entry in ChangeTracker.Entries<OlusoRole>().Where(e => e.State == EntityState.Added))
        {
            ApplyTenantIdGuard(entry.Entity, e => e.TenantId, (e, v) => e.TenantId = v, isAuditLog: false);
        }
    }

    private void ApplyTenantIdGuard<T>(T entity, Func<T, string?> get, Action<T, string> set, bool isAuditLog)
        where T : class
    {
        var rawCurrent = get(entity);
        // Treat empty / whitespace as no value — historical code paths use
        // `?? string.Empty` when reading a tenant claim that may be absent
        // (e.g. login before tenant resolution). An empty string isn't a
        // meaningful TenantId; collapsing it to null keeps the guard from
        // logging false-positive cross-tenant warnings on those paths.
        var current = string.IsNullOrWhiteSpace(rawCurrent) ? null : rawCurrent;
        var requestTenant = CurrentTenantId;

        // The platform tenant ('_platform') is a cross-tenant context, not
        // a regular tenant scope — login pages, SuperAdmin operations, and
        // system flows resolve to it. Treat it the same as "no context"
        // here so callers can legitimately stamp a target tenant (e.g. a
        // session for a user signing into tenant-X via the platform login
        // URL records TenantId = tenant-X, not _platform).
        var isCrossTenantContext = requestTenant == null
            || string.Equals(requestTenant, PlatformConstants.TenantId, StringComparison.Ordinal);

        if (!isCrossTenantContext)
        {
            // Real tenant-scoped context. Stamp authoritatively from the
            // resolved tenant — this is ground truth regardless of what
            // the caller put on the entity. A mismatch is logged (could be
            // an attack attempt or a stale pre-stamp) but the write is
            // coerced rather than rejected, so the request still completes
            // and the row lands in the correct tenant. Alert on the
            // warning to investigate the caller.
            if (current != null && !string.Equals(current, requestTenant, StringComparison.Ordinal))
            {
                _validationLogger?.LogWarning(
                    "Cross-tenant write coerced on {EntityType}: caller-supplied TenantId '{Supplied}' replaced with current tenant '{Actual}'. Investigate caller.",
                    typeof(T).Name, current, requestTenant);
            }
            set(entity, requestTenant!);
            return;
        }

        // Cross-tenant context (platform / null). Caller-supplied TenantId
        // is honoured because legitimate code paths (SuperAdmin cross-
        // tenant ops, platform-login session creation, startup seeds,
        // system jobs targeting a specific tenant by id) need to specify
        // the target tenant explicitly.
        if (current != null) return;

        // No tenant context AND no caller-supplied TenantId. AuditLog
        // falls back to the _platform sentinel so those rows can never
        // match a tenant-scoped read filter; other entities keep null
        // (preserves the existing "global system role" convention used
        // by OlusoRole and journey policies).
        if (isAuditLog)
        {
            set(entity, PlatformConstants.TenantId);
        }
    }

    /// <summary>
    /// Ignore tenant filter for specific queries (admin operations).
    /// Use this when you need to query across all tenants.
    /// </summary>
    public IQueryable<T> IgnoreTenantFilter<T>() where T : class
    {
        return Set<T>().IgnoreQueryFilters();
    }
}
