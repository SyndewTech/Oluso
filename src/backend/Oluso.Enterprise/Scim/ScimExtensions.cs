using Microsoft.AspNetCore.Builder;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Oluso.Core.Authorization;
using Oluso.Core.Data;
using Oluso.Core.Events;
using Oluso.Core.Services;
using Oluso.Core.UserJourneys;
using Oluso.Enterprise.Scim.EntityFramework;
using Oluso.Enterprise.Scim.Outbound;
using Oluso.Enterprise.Scim.Services;
using Oluso.Enterprise.Scim.Stores;

namespace Oluso.Enterprise.Scim;

/// <summary>
/// Extension methods for configuring SCIM support
/// </summary>
public static class ScimExtensions
{
    /// <summary>
    /// Add SCIM 2.0 server services and Entity Framework stores.
    /// By default uses the same database as the host application with a separate migrations table.
    /// </summary>
    /// <param name="services">Service collection</param>
    /// <param name="configure">Optional configuration callback</param>
    public static IServiceCollection AddScim(
        this IServiceCollection services,
        Action<ScimOptions>? configure = null)
    {
        var options = new ScimOptions();
        configure?.Invoke(options);

        services.AddSingleton(options);

        PermissionRegistry.Instance.RegisterPermissions(new[]
        {
            new PermissionDefinition("scim.read", "View SCIM settings", "SCIM", "View SCIM provisioning configuration"),
            new PermissionDefinition("scim.write", "Manage SCIM settings", "SCIM", "Configure SCIM provisioning settings"),
        });
        services.AddScoped<IScimContextAccessor, ScimContextAccessor>();
        services.AddSingleton<ScimExpressionEngine>();
        services.AddSingleton<ScimInboundMapper>();
        services.AddScoped<IScimUserMapper, DefaultScimUserMapper>();
        services.AddScoped<IScimProvisioningService, ScimProvisioningService>();
        services.AddScoped<IUserDeletionParticipant, ScimUserDeletionParticipant>();

        // Register EF stores
        services.AddScoped<IScimClientStore, ScimClientStore>();
        services.AddScoped<IScimProvisioningLogStore, ScimProvisioningLogStore>();
        services.AddScoped<IScimResourceMappingStore, ScimResourceMappingStore>();
        services.AddScoped<IScimAttributeMappingStore, ScimAttributeMappingStore>();

        // Outbound provisioning: this tenant pushing to downstream SCIM providers
        services.AddScoped<IScimProviderStore, ScimProviderStore>();
        services.AddScoped<IScimProviderResourceMappingStore, ScimProviderResourceMappingStore>();
        services.AddScoped<IScimProviderAttributeMappingStore, ScimProviderAttributeMappingStore>();
        services.AddScoped<IScimProviderOutboxStore, ScimProviderOutboxStore>();
        services.AddScoped<IScimProviderLogStore, ScimProviderLogStore>();
        services.AddScoped<IScimProviderSecretStore, ScimProviderSecretStore>();
        services.Configure<TenantConfigurationOptions>(o =>
            o.EncryptedSectionPrefixes.Add(ScimProviderCredentials.SectionKeyPrefix));

        services.AddHttpClient(ScimProviderHttpClient.ClientName, client =>
            client.DefaultRequestHeaders.UserAgent.ParseAdd("Oluso-Scim/1.0"));
        services.TryAddSingleton<IClientCredentialsTokenCache, ClientCredentialsTokenCache>();

        services.AddSingleton<IScimOutboundMapper, ScimOutboundMapper>();
        services.AddScoped<IScimGroupSource, RoleScimGroupSource>();
        services.AddScoped<IScimGroupSource, TeamScimGroupSource>();
        services.AddScoped<ScimGroupSourceRegistry>();
        services.AddSingleton(ScimProviderFlavorProfiles.Generic);
        services.AddSingleton(ScimProviderFlavorProfiles.Stalwart);
        services.TryAddSingleton<ScimProviderFlavorRegistry>();
        services.AddScoped<IScimProviderHttpClient, ScimProviderHttpClient>();
        services.AddScoped<IScimProviderEnqueuer, ScimProviderEnqueuer>();
        services.AddScoped<IScimOutboundProvisioningService, ScimOutboundProvisioningService>();
        services.AddScoped<IScimProviderReconcileService, ScimProviderReconcileService>();
        services.AddScoped<IScimProviderConnectionTester, ScimProviderConnectionTester>();
        services.AddEventSink<ScimOutboundEventSink>();

        if (options.Outbound.Enabled)
            services.AddHostedService<ScimOutboundWorker>();

        return services;
    }

    /// <summary>
    /// Add SCIM DbContext with SQLite.
    /// Uses provider-specific context to ensure migrations are properly discovered.
    /// </summary>
    public static IServiceCollection AddScimSqlite(
        this IServiceCollection services,
        string connectionString)
    {
        services.AddDbContext<ScimDbContextSqlite>(options =>
            options.UseSqlite(connectionString, o =>
                o.MigrationsHistoryTable(PluginDbContextExtensions.GetMigrationsTableName(ScimDbContext.PluginIdentifier))));

        // Register as both the specific type and base type
        services.AddScoped<ScimDbContext>(sp => sp.GetRequiredService<ScimDbContextSqlite>());
        services.AddScoped<IMigratableDbContext>(sp => sp.GetRequiredService<ScimDbContextSqlite>());
        services.AddScoped<ISeedableDbContext>(sp => sp.GetRequiredService<ScimDbContextSqlite>());

        return services;
    }

    /// <summary>
    /// Add SCIM DbContext with SQL Server.
    /// Uses provider-specific context to ensure migrations are properly discovered.
    /// </summary>
    public static IServiceCollection AddScimSqlServer(
        this IServiceCollection services,
        string connectionString)
    {
        services.AddDbContext<ScimDbContextSqlServer>(options =>
            options.UseSqlServer(connectionString, o =>
                o.MigrationsHistoryTable(PluginDbContextExtensions.GetMigrationsTableName(ScimDbContext.PluginIdentifier))));

        // Register as both the specific type and base type
        services.AddScoped<ScimDbContext>(sp => sp.GetRequiredService<ScimDbContextSqlServer>());
        services.AddScoped<IMigratableDbContext>(sp => sp.GetRequiredService<ScimDbContextSqlServer>());
        services.AddScoped<ISeedableDbContext>(sp => sp.GetRequiredService<ScimDbContextSqlServer>());

        return services;
    }

    /// <summary>
    /// Add SCIM DbContext with PostgreSQL.
    /// Uses provider-specific context to ensure migrations are properly discovered.
    /// </summary>
    public static IServiceCollection AddScimNpgsql(
        this IServiceCollection services,
        string connectionString)
    {
        services.AddDbContext<ScimDbContextPostgres>(options =>
            options.UseNpgsql(connectionString, o =>
                o.MigrationsHistoryTable(PluginDbContextExtensions.GetMigrationsTableNamePostgres(ScimDbContext.PluginIdentifier))));

        // Register as both the specific type and base type
        services.AddScoped<ScimDbContext>(sp => sp.GetRequiredService<ScimDbContextPostgres>());
        services.AddScoped<IMigratableDbContext>(sp => sp.GetRequiredService<ScimDbContextPostgres>());
        services.AddScoped<ISeedableDbContext>(sp => sp.GetRequiredService<ScimDbContextPostgres>());

        return services;
    }

    /// <summary>
    /// Add SCIM DbContext using the specified database provider.
    /// Convenience method that selects the appropriate provider-specific context.
    /// </summary>
    public static IServiceCollection AddScimForProvider(
        this IServiceCollection services,
        string provider,
        string connectionString)
    {
        return provider.ToLowerInvariant() switch
        {
            "sqlserver" or "mssql" => services.AddScimSqlServer(connectionString),
            "postgresql" or "postgres" or "npgsql" => services.AddScimNpgsql(connectionString),
            _ => services.AddScimSqlite(connectionString)
        };
    }

    /// <summary>
    /// Wire SCIM request authentication. Must run after UseAuthentication so a
    /// client-credentials JWT validated by the host's access-token scheme is
    /// available. The SCIM controllers themselves are auto-discovered by MVC.
    /// </summary>
    public static IApplicationBuilder UseScim(this IApplicationBuilder app)
    {
        app.UseMiddleware<Authentication.ScimAuthenticationMiddleware>();
        return app;
    }
}

/// <summary>
/// Options for SCIM server configuration
/// </summary>
public class ScimOptions
{
    /// <summary>
    /// Base path for SCIM endpoints (default: /scim/v2)
    /// </summary>
    public string BasePath { get; set; } = ScimConstants.DefaultBasePath;

    /// <summary>
    /// Name of the host's JWT access-token authentication scheme, used to
    /// validate OAuth 2.0 client-credentials tokens presented to SCIM
    /// endpoints. Defaults to the platform's "AccessToken" scheme.
    /// </summary>
    public string AccessTokenScheme { get; set; } = "AccessToken";

    /// <summary>
    /// Maximum number of resources returned in a single response
    /// </summary>
    public int MaxResults { get; set; } = 200;

    /// <summary>
    /// Maximum number of bulk operations per request
    /// </summary>
    public int MaxBulkOperations { get; set; } = 1000;

    /// <summary>
    /// Whether to enable soft delete (deactivate) instead of hard delete
    /// </summary>
    public bool SoftDeleteUsers { get; set; } = true;

    /// <summary>
    /// Whether to log request/response bodies (may contain sensitive data)
    /// </summary>
    public bool LogRequestBodies { get; set; } = false;

    /// <summary>
    /// How long to retain provisioning logs (null = forever). Applies to
    /// inbound client logs and outbound provider logs alike.
    /// </summary>
    public TimeSpan? LogRetention { get; set; } = TimeSpan.FromDays(90);

    /// <summary>
    /// Outbound provisioning (pushing to downstream SCIM providers).
    /// </summary>
    public ScimOutboundOptions Outbound { get; set; } = new();
}

/// <summary>
/// Options for the outbound provisioning queue and worker.
/// </summary>
public class ScimOutboundOptions
{
    /// <summary>
    /// Runs the in-process queue worker. Turn off on hosts that must not do
    /// background work; queue items then wait for a host that does.
    /// </summary>
    public bool Enabled { get; set; } = true;

    public TimeSpan InitialDelay { get; set; } = TimeSpan.FromSeconds(20);

    /// <summary>How often the worker looks for due items when the queue is idle.</summary>
    public TimeSpan PollInterval { get; set; } = TimeSpan.FromSeconds(10);

    /// <summary>How long a claimed item stays locked before another worker may take it over.</summary>
    public TimeSpan LockDuration { get; set; } = TimeSpan.FromMinutes(5);

    /// <summary>Attempts before a retryable failure is abandoned.</summary>
    public int MaxAttempts { get; set; } = 8;

    /// <summary>Providers processed at the same time. Items within a provider run in order.</summary>
    public int MaxConcurrentProviders { get; set; } = 4;

    /// <summary>Items taken from one provider's queue per cycle before yielding to others.</summary>
    public int MaxItemsPerProviderPerCycle { get; set; } = 100;

    /// <summary>Per-request timeout for calls to a provider.</summary>
    public TimeSpan RequestTimeout { get; set; } = TimeSpan.FromSeconds(30);

    /// <summary>Page size used when a reconcile enumerates a tenant's users.</summary>
    public int ReconcilePageSize { get; set; } = 200;

    /// <summary>How long succeeded and superseded queue items are kept for inspection.</summary>
    public TimeSpan CompletedItemRetention { get; set; } = TimeSpan.FromDays(7);
}
