using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Oluso.Authentication;
using Oluso.Core.Authentication;
using Oluso.Core.Conditions;
using Oluso.Core.Data;
using Oluso.Core.Domain.Entities;
using Oluso.Core.Domain.Interfaces;
using Oluso.Core.Events;
using Oluso.Core.Notifications;
using Oluso.Core.Licensing;
using Oluso.Core.Services;
using Oluso.Core.UserJourneys;
using Oluso.EntityFramework;
using Oluso.EntityFramework.Identity;
using Oluso.EntityFramework.Design;
using Oluso.EntityFramework.Events;
using Oluso.EntityFramework.Services;
using Oluso.Core.Services;
using Oluso.EntityFramework.Stores;
using Oluso.Core.Storage;
using Oluso.Storage;

namespace Oluso;

/// <summary>
/// Extension methods for adding Entity Framework stores
/// </summary>
public static class EntityFrameworkExtensions
{
    /// <summary>
    /// Adds Entity Framework stores for Oluso using your existing DbContext.
    /// Your DbContext must implement IOlusoDbContext.
    /// </summary>
    /// <typeparam name="TContext">Your DbContext type that implements IOlusoDbContext</typeparam>
    /// <remarks>
    /// This registers:
    /// - Oluso protocol stores (clients, resources, grants)
    /// - ASP.NET Core Identity (unless SkipIdentity() was called)
    /// - Default IOlusoUserService (unless AddUserService() was called)
    ///
    /// To use a custom user store:
    /// <code>
    /// .AddEntityFrameworkStores&lt;AppDbContext&gt;()
    /// .SkipIdentity()
    /// .AddUserService&lt;MyLdapUserService&gt;()
    /// </code>
    /// </remarks>
    public static OlusoBuilder AddEntityFrameworkStores<TContext>(this OlusoBuilder builder)
        where TContext : DbContext, IOlusoDbContext
    {
        // Register the context interface
        builder.Services.AddScoped<IOlusoDbContext>(sp => sp.GetRequiredService<TContext>());

        // Register Oluso protocol stores (clients, resources, etc.)
        RegisterStores(builder);

        // Add ASP.NET Core Identity unless explicitly skipped
        if (!builder.Options.SkipIdentityRegistration)
        {
            // Use minimal password options - actual validation is done by TenantPasswordValidator
            // which enforces tenant-specific password policies at runtime
            var identityBuilder = builder.Services.AddIdentity<OlusoUser, OlusoRole>(options =>
            {
                // Minimal defaults - TenantPasswordValidator handles the actual rules
                options.Password.RequiredLength = 1;
                options.Password.RequireDigit = false;
                options.Password.RequireLowercase = false;
                options.Password.RequireUppercase = false;
                options.Password.RequireNonAlphanumeric = false;
                options.Password.RequiredUniqueChars = 0;
                options.User.RequireUniqueEmail = true;
            })
            .AddEntityFrameworkStores<TContext>()
            .AddUserStore<OlusoUserStore<TContext>>() // tenant-scoped role assignments
            .AddDefaultTokenProviders()
            .AddPasswordValidator<TenantPasswordValidator>(); // Tenant-aware password validation

            // Add Oluso claims principal factory for tenant and plugin claims
            identityBuilder.AddClaimsPrincipalFactory<OlusoClaimsPrincipalFactory>();
            identityBuilder.AddTenantLockout();
            identityBuilder.AddTenantAccountLinkLifetime();
            builder.Services.PreserveSessionAuthenticationClaims();
            TenantPasswordValidator.AddPwnedPasswordsClient(builder.Services);

            // Register default user service (wraps UserManager)
            // Can be overridden by calling AddUserService<T>() after this
            if (!builder.Options.CustomUserServiceRegistered)
            {
                builder.Services.AddScoped<IOlusoUserService, IdentityUserService>();
            }

            // Identity's stock validator treats role names as globally unique;
            // ours are scoped per tenant. Replace rather than add — RoleManager
            // runs every registered validator, so leaving the default in place
            // would still reject the second tenant's "Admin".
            builder.Services.RemoveAll<IRoleValidator<OlusoRole>>();
            builder.Services.AddScoped<IRoleValidator<OlusoRole>, TenantAwareRoleValidator>();

            RestoreOlusoSmartScheme(builder.Services);
        }

        return builder;
    }

    /// <summary>
    /// AddIdentity sets DefaultAuthenticateScheme = ApplicationScheme, which would
    /// make app.UseAuthentication() only ever try cookies — bearer-token requests
    /// would land at TenantResolutionMiddleware with HttpContext.User empty, and
    /// the middleware's tenant-access check (ValidateAuthenticatedAccessAsync)
    /// would silently skip. Re-set the default to Oluso.Smart, the policy scheme
    /// registered in OlusoBuilder that forwards to JWT bearer when an Authorization
    /// header is present and to the cookie scheme otherwise.
    /// </summary>
    private static void RestoreOlusoSmartScheme(IServiceCollection services)
    {
        services.Configure<AuthenticationOptions>(options =>
        {
            options.DefaultScheme = "Oluso.Smart";
            options.DefaultAuthenticateScheme = "Oluso.Smart";
        });
    }

    /// <summary>
    /// Adds Entity Framework stores with the built-in OlusoDbContext.
    /// Use this if you don't have an existing DbContext.
    /// </summary>
    /// <remarks>
    /// Note: For migrations to work correctly, use the provider-specific methods instead:
    /// <list type="bullet">
    ///   <item><see cref="AddEntityFrameworkStoresSqlite"/></item>
    ///   <item><see cref="AddEntityFrameworkStoresSqlServer"/></item>
    ///   <item><see cref="AddEntityFrameworkStoresNpgsql"/></item>
    /// </list>
    /// </remarks>
    public static OlusoBuilder AddEntityFrameworkStores(
        this OlusoBuilder builder,
        Action<DbContextOptionsBuilder> configureDbContext)
    {
        // Register the reserved-auth interceptor as a singleton — stateless,
        // hot path. Any DbContext registered below picks it up via the
        // service-provider-aware AddDbContext overload.
        builder.Services.AddSingleton<EntityFramework.Interceptors.ReservedAuthInterceptor>();
        builder.Services.AddSingleton<EntityFramework.Interceptors.TenantMembershipInterceptor>();

        // Register the built-in context. Use the (sp, options) overload so
        // we can attach the interceptor — guarantees every save through
        // this context goes through the reserved-name / reserved-claim-key
        // checks regardless of caller.
        builder.Services.AddDbContext<OlusoDbContext>((sp, options) =>
        {
            configureDbContext(options);
            options.AddInterceptors(sp.GetRequiredService<EntityFramework.Interceptors.ReservedAuthInterceptor>());
            options.AddInterceptors(sp.GetRequiredService<EntityFramework.Interceptors.TenantMembershipInterceptor>());
        });
        builder.Services.AddScoped<IOlusoDbContext>(sp => sp.GetRequiredService<OlusoDbContext>());

        // Register Oluso protocol stores
        RegisterStores(builder);

        // Add ASP.NET Core Identity unless explicitly skipped
        if (!builder.Options.SkipIdentityRegistration)
        {
            // Use minimal password options - actual validation is done by TenantPasswordValidator
            // which enforces tenant-specific password policies at runtime
            var identityBuilder = builder.Services.AddIdentity<OlusoUser, OlusoRole>(options =>
            {
                // Minimal defaults - TenantPasswordValidator handles the actual rules
                options.Password.RequiredLength = 1;
                options.Password.RequireDigit = false;
                options.Password.RequireLowercase = false;
                options.Password.RequireUppercase = false;
                options.Password.RequireNonAlphanumeric = false;
                options.Password.RequiredUniqueChars = 0;
                options.User.RequireUniqueEmail = true;
            })
            .AddEntityFrameworkStores<OlusoDbContext>()
            .AddUserStore<OlusoUserStore<OlusoDbContext>>() // tenant-scoped role assignments
            .AddDefaultTokenProviders()
            .AddPasswordValidator<TenantPasswordValidator>(); // Tenant-aware password validation

            // Add Oluso claims principal factory for tenant and plugin claims
            identityBuilder.AddClaimsPrincipalFactory<OlusoClaimsPrincipalFactory>();
            identityBuilder.AddTenantLockout();
            identityBuilder.AddTenantAccountLinkLifetime();
            builder.Services.PreserveSessionAuthenticationClaims();
            TenantPasswordValidator.AddPwnedPasswordsClient(builder.Services);

            // Register default user service
            if (!builder.Options.CustomUserServiceRegistered)
            {
                builder.Services.AddScoped<IOlusoUserService, IdentityUserService>();
            }

            // Identity's stock validator treats role names as globally unique;
            // ours are scoped per tenant. Replace rather than add — RoleManager
            // runs every registered validator, so leaving the default in place
            // would still reject the second tenant's "Admin".
            builder.Services.RemoveAll<IRoleValidator<OlusoRole>>();
            builder.Services.AddScoped<IRoleValidator<OlusoRole>, TenantAwareRoleValidator>();

            RestoreOlusoSmartScheme(builder.Services);
        }

        return builder;
    }

    /// <summary>
    /// Adds Entity Framework stores with SQLite.
    /// Uses provider-specific context to ensure migrations are properly discovered.
    /// </summary>
    /// <example>
    /// <code>
    /// builder.Services.AddOluso(configuration)
    ///     .AddEntityFrameworkStoresSqlite("Data Source=oluso.db");
    /// </code>
    /// </example>
    public static OlusoBuilder AddEntityFrameworkStoresSqlite(
        this OlusoBuilder builder,
        string connectionString)
    {
        builder.Services.AddSingleton<EntityFramework.Interceptors.ReservedAuthInterceptor>();
        builder.Services.AddSingleton<EntityFramework.Interceptors.TenantMembershipInterceptor>();
        builder.Services.AddDbContext<OlusoDbContextSqlite>((sp, options) =>
        {
            options.UseSqlite(connectionString);
            options.AddInterceptors(sp.GetRequiredService<EntityFramework.Interceptors.ReservedAuthInterceptor>());
            options.AddInterceptors(sp.GetRequiredService<EntityFramework.Interceptors.TenantMembershipInterceptor>());
        });

        // Register as both the specific type and interfaces
        builder.Services.AddScoped<OlusoDbContext>(sp => sp.GetRequiredService<OlusoDbContextSqlite>());
        builder.Services.AddScoped<IOlusoDbContext>(sp => sp.GetRequiredService<OlusoDbContextSqlite>());
        builder.Services.AddScoped<IMigratableDbContext>(sp => sp.GetRequiredService<OlusoDbContextSqlite>());

        RegisterStores(builder);
        RegisterIdentityWithContext<OlusoDbContextSqlite>(builder);

        return builder;
    }

    /// <summary>
    /// Adds Entity Framework stores with SQL Server.
    /// Uses provider-specific context to ensure migrations are properly discovered.
    /// </summary>
    /// <example>
    /// <code>
    /// builder.Services.AddOluso(configuration)
    ///     .AddEntityFrameworkStoresSqlServer("Server=.;Database=Oluso;Trusted_Connection=True");
    /// </code>
    /// </example>
    public static OlusoBuilder AddEntityFrameworkStoresSqlServer(
        this OlusoBuilder builder,
        string connectionString)
    {
        builder.Services.AddSingleton<EntityFramework.Interceptors.ReservedAuthInterceptor>();
        builder.Services.AddSingleton<EntityFramework.Interceptors.TenantMembershipInterceptor>();
        builder.Services.AddDbContext<OlusoDbContextSqlServer>((sp, options) =>
        {
            options.UseSqlServer(connectionString);
            options.AddInterceptors(sp.GetRequiredService<EntityFramework.Interceptors.ReservedAuthInterceptor>());
            options.AddInterceptors(sp.GetRequiredService<EntityFramework.Interceptors.TenantMembershipInterceptor>());
        });

        // Register as both the specific type and interfaces
        builder.Services.AddScoped<OlusoDbContext>(sp => sp.GetRequiredService<OlusoDbContextSqlServer>());
        builder.Services.AddScoped<IOlusoDbContext>(sp => sp.GetRequiredService<OlusoDbContextSqlServer>());
        builder.Services.AddScoped<IMigratableDbContext>(sp => sp.GetRequiredService<OlusoDbContextSqlServer>());

        RegisterStores(builder);
        RegisterIdentityWithContext<OlusoDbContextSqlServer>(builder);

        return builder;
    }

    /// <summary>
    /// Adds Entity Framework stores with PostgreSQL.
    /// Uses provider-specific context to ensure migrations are properly discovered.
    /// </summary>
    /// <example>
    /// <code>
    /// builder.Services.AddOluso(configuration)
    ///     .AddEntityFrameworkStoresNpgsql("Host=localhost;Database=Oluso;Username=postgres;Password=postgres");
    /// </code>
    /// </example>
    public static OlusoBuilder AddEntityFrameworkStoresNpgsql(
        this OlusoBuilder builder,
        string connectionString)
    {
        builder.Services.AddSingleton<EntityFramework.Interceptors.ReservedAuthInterceptor>();
        builder.Services.AddSingleton<EntityFramework.Interceptors.TenantMembershipInterceptor>();
        builder.Services.AddDbContext<OlusoDbContextPostgres>((sp, options) =>
        {
            options.UseNpgsql(connectionString);
            options.AddInterceptors(sp.GetRequiredService<EntityFramework.Interceptors.ReservedAuthInterceptor>());
            options.AddInterceptors(sp.GetRequiredService<EntityFramework.Interceptors.TenantMembershipInterceptor>());
        });

        // Register as both the specific type and interfaces
        builder.Services.AddScoped<OlusoDbContext>(sp => sp.GetRequiredService<OlusoDbContextPostgres>());
        builder.Services.AddScoped<IOlusoDbContext>(sp => sp.GetRequiredService<OlusoDbContextPostgres>());
        builder.Services.AddScoped<IMigratableDbContext>(sp => sp.GetRequiredService<OlusoDbContextPostgres>());

        RegisterStores(builder);
        RegisterIdentityWithContext<OlusoDbContextPostgres>(builder);

        return builder;
    }

    /// <summary>
    /// Adds Entity Framework stores using the specified database provider.
    /// Convenience method that selects the appropriate provider-specific context.
    /// </summary>
    /// <param name="builder">The Oluso builder</param>
    /// <param name="provider">Provider name: "Sqlite", "SqlServer", "PostgreSQL", etc.</param>
    /// <param name="connectionString">Database connection string</param>
    /// <example>
    /// <code>
    /// var provider = configuration.GetValue&lt;string&gt;("Oluso:Database:Provider", "Sqlite");
    /// var connectionString = configuration.GetConnectionString("OlusoDb");
    /// builder.Services.AddOluso(configuration)
    ///     .AddEntityFrameworkStoresForProvider(provider, connectionString);
    /// </code>
    /// </example>
    public static OlusoBuilder AddEntityFrameworkStoresForProvider(
        this OlusoBuilder builder,
        string provider,
        string connectionString)
    {
        return provider.ToLowerInvariant() switch
        {
            "sqlserver" or "mssql" => builder.AddEntityFrameworkStoresSqlServer(connectionString),
            "postgresql" or "postgres" or "npgsql" => builder.AddEntityFrameworkStoresNpgsql(connectionString),
            _ => builder.AddEntityFrameworkStoresSqlite(connectionString) // Default to SQLite
        };
    }

    private static void RegisterIdentityWithContext<TContext>(OlusoBuilder builder)
        where TContext : OlusoDbContext
    {
        if (!builder.Options.SkipIdentityRegistration)
        {
            var identityBuilder = builder.Services.AddIdentity<OlusoUser, OlusoRole>(options =>
            {
                options.Password.RequiredLength = 1;
                options.Password.RequireDigit = false;
                options.Password.RequireLowercase = false;
                options.Password.RequireUppercase = false;
                options.Password.RequireNonAlphanumeric = false;
                options.Password.RequiredUniqueChars = 0;
                options.User.RequireUniqueEmail = true;
            })
            .AddEntityFrameworkStores<TContext>()
            .AddUserStore<OlusoUserStore<TContext>>() // tenant-scoped role assignments
            .AddDefaultTokenProviders()
            .AddPasswordValidator<TenantPasswordValidator>();

            identityBuilder.AddClaimsPrincipalFactory<OlusoClaimsPrincipalFactory>();
            identityBuilder.AddTenantLockout();
            identityBuilder.AddTenantAccountLinkLifetime();
            builder.Services.PreserveSessionAuthenticationClaims();
            TenantPasswordValidator.AddPwnedPasswordsClient(builder.Services);

            if (!builder.Options.CustomUserServiceRegistered)
            {
                builder.Services.AddScoped<IOlusoUserService, IdentityUserService>();
            }

            // Identity's stock validator treats role names as globally unique;
            // ours are scoped per tenant. Replace rather than add — RoleManager
            // runs every registered validator, so leaving the default in place
            // would still reject the second tenant's "Admin".
            builder.Services.RemoveAll<IRoleValidator<OlusoRole>>();
            builder.Services.AddScoped<IRoleValidator<OlusoRole>, TenantAwareRoleValidator>();

            RestoreOlusoSmartScheme(builder.Services);
        }
    }

    private static void RegisterStores(OlusoBuilder builder)
    {
        // Register all stores from Oluso.EntityFramework
        builder.Services.AddScoped<IClientStore, ClientStore>();
        builder.Services.AddScoped<IResourceStore, ResourceStore>();
        builder.Services.AddScoped<IPersistedGrantStore, PersistedGrantStore>();
        builder.Services.AddScoped<ISigningKeyStore, SigningKeyStore>();
        builder.Services.AddScoped<IConsentStore, ConsentStore>();
        builder.Services.AddScoped<IDeviceFlowStore, DeviceFlowStore>();
        builder.Services.AddScoped<IPushedAuthorizationStore, PushedAuthorizationStore>();
        builder.Services.AddScoped<IAuthorizationCodeStore, AuthorizationCodeStore>();
        builder.Services.AddScoped<IRoleStore, RoleStore>();
        builder.Services.AddScoped<IEffectiveRoleResolver, EffectiveRoleResolver>();
        builder.Services.AddScoped<IModuleAdminAssignmentNotifier, ModuleAdminAssignmentNotifier>();
        builder.Services.AddScoped<IRoleGrantService, RoleGrantService>();
        builder.Services.AddScoped<ITenantDefaultRoleSeeder, TenantDefaultRoleSeeder>();
        builder.Services.AddScoped<IIdentityProviderStore, IdentityProviderStore>();
        builder.Services.AddScoped<IExternalProviderConfigStore, ExternalProviderConfigStore>();

        // Audit log stores and service
        builder.Services.AddScoped<IAuditLogStore, AuditLogStore>();
        builder.Services.AddScoped<IAuditLogService, AuditLogService>();

        // Register audit event sink to persist events to the database
        builder.Services.AddScoped<IOlusoEventSink, AuditEventSink>();

        // Telemetry log stores and service (application logging)
        builder.Services.AddScoped<ITelemetryLogStore, TelemetryLogStore>();
        builder.Services.AddScoped<ITelemetryLogService, TelemetryLogService>();

        // Webhook stores
        builder.Services.AddScoped<IWebhookStore, WebhookStore>();
        builder.Services.AddScoped<IWebhookSubscriptionStore, WebhookSubscriptionStore>();

        // Signing credentials - use development store as fallback only.
        // AddSigningKeys() or AddOlusoAzureKeyVault() register a production SigningCredentialStore
        // that persists keys. TryAdd ensures we don't override a previously registered production store.
        builder.Services.TryAddSingleton<ISigningCredentialStore, DevelopmentSigningCredentialStore>();

        // Always register tenant store - needed for SAML IdP, SCIM, and other package-specific settings
        // In single-tenant mode, there's still a default tenant for configuration storage
        builder.Services.AddScoped<ITenantStore, TenantStore>();
        builder.Services.AddScoped<ITenantMembershipStore, TenantMembershipStore>();
        builder.Services.AddScoped<ICustomDomainVerificationStore, CustomDomainVerificationStore>();
        builder.Services.AddScoped<ITenantEmailDomainStore, TenantEmailDomainStore>();

        // Organization stores (multi-tenant management layer above tenants)
        builder.Services.AddScoped<IOrganizationStore, OrganizationStore>();
        builder.Services.AddScoped<IOrganizationMembershipStore, OrganizationMembershipStore>();

        // Teams — platform primitive available to every tenant. Store + service
        // are scoped because they hold a per-request DbContext.
        builder.Services.AddScoped<ITeamStore, TeamStore>();
        builder.Services.AddScoped<ITeamService, TeamService>();
        builder.Services.AddScoped<IOrganizationInvitationStore, OrganizationInvitationStore>();
        builder.Services.AddScoped<IOrganizationFeatureStore, OrganizationFeatureStore>();
        builder.Services.AddScoped<ITenantFeatureStore, TenantFeatureStore>();
        // Terminology overrides live on the platform DbContext (tenant
        // vocabulary is a platform concern, not any module's).
        builder.Services.AddScoped<Oluso.Core.Terminology.ITerminologyOverrideStore, Oluso.EntityFramework.Stores.TerminologyOverrideStore>();
        builder.Services.AddScoped<ITenantSeedTracker, TenantSeedTracker>();
        builder.Services.AddScoped<IUserModuleAccessStore, UserModuleAccessStore>();
        builder.Services.AddScoped<ITenantConfigurationSectionStore, TenantConfigurationSectionStore>();
        builder.Services.AddScoped<ISenderProfileStore, SenderProfileStore>();
        builder.Services.AddScoped<IWhatsAppContactStore, WhatsAppContactStore>();
        builder.Services.AddScoped<IWhatsAppTemplateStore, WhatsAppTemplateStore>();
        builder.Services.AddScoped<IWhatsAppInboundMessageStore, WhatsAppInboundMessageStore>();
        builder.Services.AddScoped<IWhatsAppAccountStore, WhatsAppAccountStore>();
        builder.Services.AddScoped<IEntitlementStore, EntitlementStore>();
        builder.Services.AddScoped<ITenantConfigurationService, TenantConfigurationService>();

        // Server-side sessions store
        builder.Services.AddScoped<IServerSideSessionStore, ServerSideSessionStore>();

        // Server-side session ticket store for cookie authentication
        // This enables session management, revocation, and backchannel logout
        builder.Services.AddScoped<ITicketStore, ServerSideSessionTicketStore>();

        // CIBA store
        builder.Services.AddScoped<ICibaStore, CibaStore>();

        // Feedback store
        builder.Services.AddScoped<IFeedbackStore, FeedbackStore>();

        // File attachments
        builder.Services.AddScoped<IFileAttachmentStore, FileAttachmentStore>();
        builder.Services.AddScoped<IFileAttachmentService, FileAttachmentService>();
        builder.Services.AddScoped<IFileAttachmentAccessService, FileAttachmentAccessService>();
        // Entity-reference protection (used when an IFileAccessProvider opts in
        // to encrypt the EntityId / SubEntityId / FileName columns; backed by
        // the platform's SigningKey + IKeyMaterialProvider pipeline, so KV-
        // backed deployments inherit KV protection automatically).
        builder.Services.AddScoped<IEntityReferenceProtector, EntityReferenceProtector>();

        // User notification preferences — Core primitive consulted by any
        // user-targeted notification sink (email, push, SMS) before delivery.
        builder.Services.AddScoped<IUserNotificationProfileStore, UserNotificationProfileStore>();
        builder.Services.AddScoped<IUserNotificationGate, DefaultUserNotificationGate>();

        // Single-use action tokens (magic-link decide, vendor confirmation,
        // candidate offer-accept, etc.). Hash-only persistence — raw tokens
        // never reach the DB.
        builder.Services.AddScoped<IPasswordHistoryStore, PasswordHistoryStore>();

        builder.Services.AddScoped<IActionTokenStore, ActionTokenStore>();
        builder.Services.AddScoped<IActionTokenService, DefaultActionTokenService>();
        builder.Services.AddScoped<Oluso.PublicLinks.IPublicLinkUnlockService, Oluso.PublicLinks.PublicLinkUnlockService>();

        // "Sign in with your organisation" on the platform login page. Its
        // hand-off code is an action token, so it lives with the store above.
        builder.Services.AddScoped<Oluso.Authentication.OrganizationSignIn.IOrganizationSignInService,
            Oluso.Authentication.OrganizationSignIn.OrganizationSignInService>();

        // Conversation messages — flat threads keyed by (EntityType, EntityId).
        // Modules attach threads by reusing the pair and registering an
        // IConversationAuthorizer for their entity type.
        builder.Services.AddScoped<IConversationMessageStore, ConversationMessageStore>();
        builder.Services.AddScoped<IConversationMentionReadStore, ConversationMentionReadStore>();

        // Shared expression evaluator — boolean grammar (AND/OR/NOT/parens
        // + the comparison ops the old approval condition parser had).
        // Stateless, singleton. Consumed by the approval workflow service
        // today; available for feature flags / automation rules later.
        builder.Services.AddSingleton<IExpressionEvaluator, DefaultExpressionEvaluator>();

        // MFA service (Identity-based implementation)
        builder.Services.AddScoped<IMfaService, IdentityMfaService>();

        // User Journey stores (replaces in-memory defaults from AddUserJourneys)
        RemoveService<IJourneyPolicyStore>(builder.Services);
        RemoveService<IJourneyStateStore>(builder.Services);
        RemoveService<IJourneySubmissionStore>(builder.Services);
        builder.Services.AddScoped<IJourneyPolicyStore, JourneyPolicyStore>();
        builder.Services.AddScoped<IJourneyStateStore, JourneyStateStore>();
        builder.Services.AddScoped<IJourneySubmissionStore, JourneySubmissionStore>();
    }

    private static void RemoveService<T>(IServiceCollection services)
    {
        var descriptor = services.FirstOrDefault(d => d.ServiceType == typeof(T));
        if (descriptor != null)
        {
            services.Remove(descriptor);
        }
    }
}
