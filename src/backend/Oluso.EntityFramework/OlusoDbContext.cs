using Microsoft.AspNetCore.Identity.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore;
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

    private readonly ITenantContext? _tenantContext;
    private readonly string? _tenantId;

    public OlusoDbContext(DbContextOptions<OlusoDbContext> options) : base(options)
    {
        EnableWalModeIfSqlite();
    }

    /// <summary>
    /// Constructor for derived provider-specific contexts (e.g., OlusoDbContextSqlite).
    /// </summary>
    protected OlusoDbContext(DbContextOptions options) : base(options)
    {
        EnableWalModeIfSqlite();
    }

    public OlusoDbContext(
        DbContextOptions<OlusoDbContext> options,
        ITenantContext tenantContext) : base(options)
    {
        _tenantContext = tenantContext;
        _tenantId = tenantContext.TenantId;
        EnableWalModeIfSqlite();
    }

    /// <summary>
    /// Enables WAL mode for SQLite databases to prevent locking issues.
    /// WAL mode allows concurrent reads and writes.
    /// </summary>
    private void EnableWalModeIfSqlite()
    {
        // Check if using SQLite by looking at the provider name
        var providerName = Database.ProviderName;
        if (providerName?.Contains("Sqlite", StringComparison.OrdinalIgnoreCase) == true)
        {
            // Enable WAL mode for better concurrency
            Database.ExecuteSqlRaw("PRAGMA journal_mode=WAL;");
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
    public DbSet<ClientCorsOrigin> ClientCorsOrigins => Set<ClientCorsOrigin>();
    public DbSet<ClientProperty> ClientProperties => Set<ClientProperty>();
    public DbSet<ClientIdPRestriction> ClientIdPRestrictions => Set<ClientIdPRestriction>();
    public DbSet<ClientAllowedRole> ClientAllowedRoles => Set<ClientAllowedRole>();
    public DbSet<ClientAllowedUser> ClientAllowedUsers => Set<ClientAllowedUser>();

    // Resources
    public DbSet<ApiResource> ApiResources => Set<ApiResource>();
    public DbSet<ApiResourceSecret> ApiResourceSecrets => Set<ApiResourceSecret>();
    public DbSet<ApiResourceScope> ApiResourceScopes => Set<ApiResourceScope>();
    public DbSet<ApiResourceClaim> ApiResourceClaims => Set<ApiResourceClaim>();
    public DbSet<ApiResourceProperty> ApiResourceProperties => Set<ApiResourceProperty>();
    public DbSet<ApiScope> ApiScopes => Set<ApiScope>();
    public DbSet<ApiScopeClaim> ApiScopeClaims => Set<ApiScopeClaim>();
    public DbSet<ApiScopeProperty> ApiScopeProperties => Set<ApiScopeProperty>();
    public DbSet<IdentityResource> IdentityResources => Set<IdentityResource>();
    public DbSet<IdentityResourceClaim> IdentityResourceClaims => Set<IdentityResourceClaim>();
    public DbSet<IdentityResourceProperty> IdentityResourceProperties => Set<IdentityResourceProperty>();

    // Operational
    public DbSet<PersistedGrant> PersistedGrants => Set<PersistedGrant>();
    public DbSet<DeviceFlowCode> DeviceFlowCodes => Set<DeviceFlowCode>();
    public DbSet<Consent> Consents => Set<Consent>();
    public DbSet<SigningKey> SigningKeys => Set<SigningKey>();
    public DbSet<ServerSideSession> ServerSideSessions => Set<ServerSideSession>();
    public DbSet<PushedAuthorizationRequest> PushedAuthorizationRequests => Set<PushedAuthorizationRequest>();

    // Multi-tenancy
    public DbSet<Tenant>? Tenants => Set<Tenant>();

    // External identity providers
    public DbSet<IdentityProvider> IdentityProviders => Set<IdentityProvider>();

    // User Journeys
    public DbSet<JourneyPolicyEntity> JourneyPolicies => Set<JourneyPolicyEntity>();
    public DbSet<JourneyStateEntity> JourneyStates => Set<JourneyStateEntity>();
    public DbSet<JourneySubmissionEntity> JourneySubmissions => Set<JourneySubmissionEntity>();

    // Audit logs
    public DbSet<AuditLog> AuditLogs => Set<AuditLog>();

    // Webhooks
    public DbSet<WebhookEndpointEntity> WebhookEndpoints => Set<WebhookEndpointEntity>();
    public DbSet<WebhookEventSubscriptionEntity> WebhookEventSubscriptions => Set<WebhookEventSubscriptionEntity>();
    public DbSet<WebhookDeliveryEntity> WebhookDeliveries => Set<WebhookDeliveryEntity>();

    // FIDO2/Passkeys
    public DbSet<Fido2CredentialEntity> Fido2Credentials => Set<Fido2CredentialEntity>();

    // Plugin metadata
    public DbSet<PluginMetadata> PluginMetadata => Set<PluginMetadata>();

    // CIBA (Client Initiated Backchannel Authentication)
    public DbSet<CibaRequest> CibaRequests => Set<CibaRequest>();

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
        builder.ApplyConfiguration(new ApiResourceConfiguration());
        builder.ApplyConfiguration(new ApiScopeConfiguration());
        builder.ApplyConfiguration(new IdentityResourceConfiguration());
        builder.ApplyConfiguration(new PersistedGrantConfiguration());
        builder.ApplyConfiguration(new DeviceFlowCodeConfiguration());
        builder.ApplyConfiguration(new ConsentConfiguration());
        builder.ApplyConfiguration(new SigningKeyConfiguration());
        builder.ApplyConfiguration(new PushedAuthorizationRequestConfiguration());
        builder.ApplyConfiguration(new TenantConfiguration());
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
        builder.ApplyConfiguration(new WebhookEndpointConfiguration());
        builder.ApplyConfiguration(new WebhookEventSubscriptionConfiguration());
        builder.ApplyConfiguration(new WebhookDeliveryConfiguration());
        builder.ApplyConfiguration(new Fido2CredentialEntityConfiguration());
        builder.ApplyConfiguration(new PluginMetadataConfiguration());

        // Apply global tenant filters to all tenant entities
        ApplyTenantQueryFilters(builder);
    }

    private void ApplyTenantQueryFilters(ModelBuilder builder)
    {
        // Apply filter to TenantEntity derived types
        foreach (var entityType in builder.Model.GetEntityTypes())
        {
            if (typeof(TenantEntity).IsAssignableFrom(entityType.ClrType))
            {
                var parameter = Expression.Parameter(entityType.ClrType, "e");
                var tenantProperty = Expression.Property(parameter, nameof(TenantEntity.TenantId));
                var tenantValue = Expression.Constant(_tenantId, typeof(string));
                var tenantIdIsNull = Expression.Constant(_tenantId == null, typeof(bool));

                // Filter: _tenantId == null || e.TenantId == _tenantId || e.TenantId == null
                // This allows all entities when no tenant context (e.g., during admin login)
                var equalTenant = Expression.Equal(tenantProperty, tenantValue);
                var entityIsNull = Expression.Equal(tenantProperty, Expression.Constant(null, typeof(string)));
                var tenantFilter = Expression.OrElse(equalTenant, entityIsNull);
                var filter = Expression.OrElse(tenantIdIsNull, tenantFilter);

                var lambda = Expression.Lambda(filter, parameter);
                builder.Entity(entityType.ClrType).HasQueryFilter(lambda);
            }

            // Special handling for OlusoUser (not derived from TenantEntity)
            // Allow all users when no tenant context (for login), otherwise filter by tenant
            if (entityType.ClrType == typeof(OlusoUser))
            {
                builder.Entity<OlusoUser>().HasQueryFilter(u =>
                    _tenantId == null || u.TenantId == _tenantId || u.TenantId == null);
            }

            // Special handling for OlusoRole (not derived from TenantEntity)
            // Allow all roles when no tenant context, otherwise filter by tenant
            if (entityType.ClrType == typeof(OlusoRole))
            {
                builder.Entity<OlusoRole>().HasQueryFilter(r =>
                    _tenantId == null || r.TenantId == _tenantId || r.TenantId == null);
            }
        }
    }

    public override int SaveChanges()
    {
        SetTenantId();
        return base.SaveChanges();
    }

    public override Task<int> SaveChangesAsync(CancellationToken cancellationToken = default)
    {
        SetTenantId();
        return base.SaveChangesAsync(cancellationToken);
    }

    private void SetTenantId()
    {
        var tenantEntities = ChangeTracker.Entries<TenantEntity>()
            .Where(e => e.State == EntityState.Added);

        foreach (var entry in tenantEntities)
        {
            if (entry.Entity.TenantId == null && _tenantId != null)
            {
                entry.Entity.TenantId = _tenantId;
            }
        }

        // Handle OlusoUser separately
        var userEntities = ChangeTracker.Entries<OlusoUser>()
            .Where(e => e.State == EntityState.Added);

        foreach (var entry in userEntities)
        {
            if (entry.Entity.TenantId == null && _tenantId != null)
            {
                entry.Entity.TenantId = _tenantId;
            }
        }

        // Handle OlusoRole separately
        var roleEntities = ChangeTracker.Entries<OlusoRole>()
            .Where(e => e.State == EntityState.Added);

        foreach (var entry in roleEntities)
        {
            if (entry.Entity.TenantId == null && _tenantId != null)
            {
                entry.Entity.TenantId = _tenantId;
            }
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
