using Microsoft.AspNetCore.Builder;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Oluso.Core.Data;
using Oluso.Enterprise.Scim.EntityFramework;
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
        services.AddScoped<IScimContextAccessor, ScimContextAccessor>();
        services.AddScoped<IScimUserMapper, DefaultScimUserMapper>();

        // Register EF stores
        services.AddScoped<IScimClientStore, ScimClientStore>();
        services.AddScoped<IScimProvisioningLogStore, ScimProvisioningLogStore>();
        services.AddScoped<IScimResourceMappingStore, ScimResourceMappingStore>();
        services.AddScoped<IScimAttributeMappingStore, ScimAttributeMappingStore>();

        return services;
    }

    /// <summary>
    /// Add SCIM DbContext using the same connection as the host application.
    /// Uses a separate migrations history table.
    /// </summary>
    /// <typeparam name="THostContext">The host application's DbContext type</typeparam>
    /// <param name="services">Service collection</param>
    public static IServiceCollection AddScimDbContext<THostContext>(this IServiceCollection services)
        where THostContext : DbContext
    {
        var migrationsTable = PluginDbContextExtensions.GetMigrationsTableName(ScimDbContext.PluginIdentifier);

        services.AddDbContext<ScimDbContext>((serviceProvider, options) =>
        {
            // Get the host context's connection
            var hostContext = serviceProvider.GetRequiredService<THostContext>();
            var connection = hostContext.Database.GetDbConnection();

            // Use the same connection with separate migrations table
            // Detect provider and configure appropriately
            var providerName = hostContext.Database.ProviderName ?? "";

            if (providerName.Contains("SqlServer", StringComparison.OrdinalIgnoreCase))
            {
                options.UseSqlServer(connection, sql =>
                    sql.MigrationsHistoryTable(migrationsTable));
            }
            else if (providerName.Contains("Npgsql", StringComparison.OrdinalIgnoreCase) ||
                     providerName.Contains("PostgreSQL", StringComparison.OrdinalIgnoreCase))
            {
                var pgTable = PluginDbContextExtensions.GetMigrationsTableNamePostgres(ScimDbContext.PluginIdentifier);
                options.UseNpgsql(connection, npg =>
                    npg.MigrationsHistoryTable(pgTable));
            }
            else if (providerName.Contains("Sqlite", StringComparison.OrdinalIgnoreCase))
            {
                options.UseSqlite(connection, sqlite =>
                    sqlite.MigrationsHistoryTable(migrationsTable));
            }
            else
            {
                // Fallback - try to use the same options builder pattern
                throw new InvalidOperationException(
                    $"Unsupported database provider: {providerName}. " +
                    "Use AddScimDbContext with explicit configuration instead.");
            }
        });

        // Register for automatic migration discovery
        services.AddScoped<IMigratableDbContext>(sp => sp.GetRequiredService<ScimDbContext>());

        // Register for automatic seeding
        services.AddScoped<ISeedableDbContext>(sp => sp.GetRequiredService<ScimDbContext>());

        return services;
    }

    /// <summary>
    /// Add SCIM DbContext with a custom connection string (separate database).
    /// Uses a separate migrations history table.
    /// </summary>
    /// <param name="services">Service collection</param>
    /// <param name="configureDb">DbContext options configuration</param>
    public static IServiceCollection AddScimDbContext(
        this IServiceCollection services,
        Action<DbContextOptionsBuilder> configureDb)
    {
        services.AddDbContext<ScimDbContext>(configureDb);

        // Register for automatic migration discovery
        services.AddScoped<IMigratableDbContext>(sp => sp.GetRequiredService<ScimDbContext>());

        // Register for automatic seeding
        services.AddScoped<ISeedableDbContext>(sp => sp.GetRequiredService<ScimDbContext>());

        return services;
    }

    /// <summary>
    /// Add SCIM DbContext with SQLite.
    /// </summary>
    public static IServiceCollection AddScimSqlite(
        this IServiceCollection services,
        string connectionString)
    {
        return services.AddScimDbContext(options =>
            options.UseSqlite(connectionString, o =>
                o.MigrationsHistoryTable(PluginDbContextExtensions.GetMigrationsTableName(ScimDbContext.PluginIdentifier))));
    }

    /// <summary>
    /// Add SCIM DbContext with SQL Server.
    /// </summary>
    public static IServiceCollection AddScimSqlServer(
        this IServiceCollection services,
        string connectionString)
    {
        return services.AddScimDbContext(options =>
            options.UseSqlServer(connectionString, o =>
                o.MigrationsHistoryTable(PluginDbContextExtensions.GetMigrationsTableName(ScimDbContext.PluginIdentifier))));
    }

    /// <summary>
    /// Add SCIM DbContext with PostgreSQL.
    /// </summary>
    public static IServiceCollection AddScimNpgsql(
        this IServiceCollection services,
        string connectionString)
    {
        return services.AddScimDbContext(options =>
            options.UseNpgsql(connectionString, o =>
                o.MigrationsHistoryTable(PluginDbContextExtensions.GetMigrationsTableNamePostgres(ScimDbContext.PluginIdentifier))));
    }

    /// <summary>
    /// Map SCIM 2.0 endpoints
    /// </summary>
    public static IApplicationBuilder UseScim(this IApplicationBuilder app)
    {
        // The SCIM controllers are auto-discovered by MVC
        // This method is for any middleware that needs to run before SCIM endpoints
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
    /// How long to retain provisioning logs (null = forever)
    /// </summary>
    public TimeSpan? LogRetention { get; set; } = TimeSpan.FromDays(90);
}
