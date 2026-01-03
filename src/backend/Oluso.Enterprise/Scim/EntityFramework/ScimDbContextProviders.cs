using Microsoft.EntityFrameworkCore;
using Oluso.Core.Data;

namespace Oluso.Enterprise.Scim.EntityFramework;

/// <summary>
/// SQLite-specific ScimDbContext for migrations and runtime.
/// Register this context when using SQLite to ensure migrations are found.
/// </summary>
public class ScimDbContextSqlite : ScimDbContext
{
    /// <summary>
    /// Design-time constructor for EF Core tooling.
    /// </summary>
    public ScimDbContextSqlite() : base(CreateDesignTimeOptions()) { }

    /// <summary>
    /// Runtime constructor for DI.
    /// </summary>
    public ScimDbContextSqlite(DbContextOptions<ScimDbContextSqlite> options)
        : base(options) { }

    private static DbContextOptions<ScimDbContext> CreateDesignTimeOptions()
    {
        var builder = new DbContextOptionsBuilder<ScimDbContext>();
        builder.UseSqlite("Data Source=:memory:",
            o => o.MigrationsHistoryTable(PluginDbContextExtensions.GetMigrationsTableName(PluginIdentifier)));
        return builder.Options;
    }

    protected override void OnConfiguring(DbContextOptionsBuilder optionsBuilder)
    {
        if (!optionsBuilder.IsConfigured)
        {
            optionsBuilder.UseSqlite("Data Source=Scim.db",
                o => o.MigrationsHistoryTable(PluginDbContextExtensions.GetMigrationsTableName(PluginIdentifier)));
        }
    }
}

/// <summary>
/// SQL Server-specific ScimDbContext for migrations and runtime.
/// Register this context when using SQL Server to ensure migrations are found.
/// </summary>
public class ScimDbContextSqlServer : ScimDbContext
{
    /// <summary>
    /// Design-time constructor for EF Core tooling.
    /// </summary>
    public ScimDbContextSqlServer() : base(CreateDesignTimeOptions()) { }

    /// <summary>
    /// Runtime constructor for DI.
    /// </summary>
    public ScimDbContextSqlServer(DbContextOptions<ScimDbContextSqlServer> options)
        : base(options) { }

    private static DbContextOptions<ScimDbContext> CreateDesignTimeOptions()
    {
        var builder = new DbContextOptionsBuilder<ScimDbContext>();
        builder.UseSqlServer("Server=.;Database=Scim;Trusted_Connection=True;TrustServerCertificate=True",
            o => o.MigrationsHistoryTable(PluginDbContextExtensions.GetMigrationsTableName(PluginIdentifier)));
        return builder.Options;
    }

    protected override void OnConfiguring(DbContextOptionsBuilder optionsBuilder)
    {
        if (!optionsBuilder.IsConfigured)
        {
            optionsBuilder.UseSqlServer("Server=.;Database=Scim;Trusted_Connection=True;TrustServerCertificate=True",
                o => o.MigrationsHistoryTable(PluginDbContextExtensions.GetMigrationsTableName(PluginIdentifier)));
        }
    }
}

/// <summary>
/// PostgreSQL-specific ScimDbContext for migrations and runtime.
/// Register this context when using PostgreSQL to ensure migrations are found.
/// </summary>
public class ScimDbContextPostgres : ScimDbContext
{
    /// <summary>
    /// Design-time constructor for EF Core tooling.
    /// </summary>
    public ScimDbContextPostgres() : base(CreateDesignTimeOptions()) { }

    /// <summary>
    /// Runtime constructor for DI.
    /// </summary>
    public ScimDbContextPostgres(DbContextOptions<ScimDbContextPostgres> options)
        : base(options) { }

    private static DbContextOptions<ScimDbContext> CreateDesignTimeOptions()
    {
        var builder = new DbContextOptionsBuilder<ScimDbContext>();
        builder.UseNpgsql("Host=localhost;Database=Scim;Username=postgres;Password=postgres",
            o => o.MigrationsHistoryTable(PluginDbContextExtensions.GetMigrationsTableNamePostgres(PluginIdentifier)));
        return builder.Options;
    }

    protected override void OnConfiguring(DbContextOptionsBuilder optionsBuilder)
    {
        if (!optionsBuilder.IsConfigured)
        {
            optionsBuilder.UseNpgsql("Host=localhost;Database=Scim;Username=postgres;Password=postgres",
                o => o.MigrationsHistoryTable(PluginDbContextExtensions.GetMigrationsTableNamePostgres(PluginIdentifier)));
        }
    }
}
