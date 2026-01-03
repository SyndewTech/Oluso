using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Oluso.Authentication;
using Oluso.Core.Authentication;
using Oluso.Core.Domain.Entities;
using Oluso.Core.Domain.Interfaces;
using Oluso.Core.Events;
using Oluso.Core.Services;
using Oluso.Core.UserJourneys;
using Oluso.EntityFramework;
using Oluso.EntityFramework.Design;
using Oluso.EntityFramework.Events;
using Oluso.EntityFramework.Services;
using Oluso.EntityFramework.Stores;

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
            .AddDefaultTokenProviders()
            .AddPasswordValidator<TenantPasswordValidator>(); // Tenant-aware password validation

            // Add Oluso claims principal factory for tenant and plugin claims
            identityBuilder.AddClaimsPrincipalFactory<OlusoClaimsPrincipalFactory>();

            // Register default user service (wraps UserManager)
            // Can be overridden by calling AddUserService<T>() after this
            if (!builder.Options.CustomUserServiceRegistered)
            {
                builder.Services.AddScoped<IOlusoUserService, IdentityUserService>();
            }
        }

        return builder;
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
        // Register the built-in context
        builder.Services.AddDbContext<OlusoDbContext>(configureDbContext);
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
            .AddDefaultTokenProviders()
            .AddPasswordValidator<TenantPasswordValidator>(); // Tenant-aware password validation

            // Add Oluso claims principal factory for tenant and plugin claims
            identityBuilder.AddClaimsPrincipalFactory<OlusoClaimsPrincipalFactory>();

            // Register default user service
            if (!builder.Options.CustomUserServiceRegistered)
            {
                builder.Services.AddScoped<IOlusoUserService, IdentityUserService>();
            }
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
        builder.Services.AddDbContext<OlusoDbContextSqlite>(options =>
            options.UseSqlite(connectionString));

        // Register as both the specific type and interfaces
        builder.Services.AddScoped<OlusoDbContext>(sp => sp.GetRequiredService<OlusoDbContextSqlite>());
        builder.Services.AddScoped<IOlusoDbContext>(sp => sp.GetRequiredService<OlusoDbContextSqlite>());

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
        builder.Services.AddDbContext<OlusoDbContextSqlServer>(options =>
            options.UseSqlServer(connectionString));

        // Register as both the specific type and interfaces
        builder.Services.AddScoped<OlusoDbContext>(sp => sp.GetRequiredService<OlusoDbContextSqlServer>());
        builder.Services.AddScoped<IOlusoDbContext>(sp => sp.GetRequiredService<OlusoDbContextSqlServer>());

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
        builder.Services.AddDbContext<OlusoDbContextPostgres>(options =>
            options.UseNpgsql(connectionString));

        // Register as both the specific type and interfaces
        builder.Services.AddScoped<OlusoDbContext>(sp => sp.GetRequiredService<OlusoDbContextPostgres>());
        builder.Services.AddScoped<IOlusoDbContext>(sp => sp.GetRequiredService<OlusoDbContextPostgres>());

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
            .AddDefaultTokenProviders()
            .AddPasswordValidator<TenantPasswordValidator>();

            identityBuilder.AddClaimsPrincipalFactory<OlusoClaimsPrincipalFactory>();

            if (!builder.Options.CustomUserServiceRegistered)
            {
                builder.Services.AddScoped<IOlusoUserService, IdentityUserService>();
            }
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
        builder.Services.AddScoped<IFido2CredentialStore, Fido2CredentialStore>();
        builder.Services.AddScoped<IRoleStore, RoleStore>();
        builder.Services.AddScoped<IIdentityProviderStore, IdentityProviderStore>();
        builder.Services.AddScoped<IExternalProviderConfigStore, ExternalProviderConfigStore>();

        // Audit log stores and service
        builder.Services.AddScoped<IAuditLogStore, AuditLogStore>();
        builder.Services.AddScoped<IAuditLogService, AuditLogService>();

        // Register audit event sink to persist events to the database
        builder.Services.AddScoped<IOlusoEventSink, AuditEventSink>();

        // Webhook stores
        builder.Services.AddScoped<IWebhookStore, WebhookStore>();
        builder.Services.AddScoped<IWebhookSubscriptionStore, WebhookSubscriptionStore>();

        // Signing credentials - use development store by default
        // For production, register a persistent key store or Azure Key Vault implementation
        builder.Services.AddSingleton<ISigningCredentialStore, DevelopmentSigningCredentialStore>();

        // Always register tenant store - needed for SAML IdP, SCIM, and other package-specific settings
        // In single-tenant mode, there's still a default tenant for configuration storage
        builder.Services.AddScoped<ITenantStore, TenantStore>();

        // Server-side sessions store
        builder.Services.AddScoped<IServerSideSessionStore, ServerSideSessionStore>();

        // Server-side session ticket store for cookie authentication
        // This enables session management, revocation, and backchannel logout
        builder.Services.AddScoped<ITicketStore, ServerSideSessionTicketStore>();

        // CIBA store
        builder.Services.AddScoped<ICibaStore, CibaStore>();

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
