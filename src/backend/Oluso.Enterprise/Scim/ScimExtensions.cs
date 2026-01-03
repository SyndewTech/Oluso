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
