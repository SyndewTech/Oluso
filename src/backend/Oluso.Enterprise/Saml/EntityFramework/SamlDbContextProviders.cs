using Microsoft.EntityFrameworkCore;
using Oluso.Core.Data;

namespace Oluso.Enterprise.Saml.EntityFramework;

/// <summary>
/// SQLite-specific SamlDbContext for migrations and runtime.
/// Register this context when using SQLite to ensure migrations are found.
/// </summary>
public class SamlDbContextSqlite : SamlDbContext
{
    /// <summary>
    /// Design-time constructor for EF Core tooling.
    /// </summary>
    public SamlDbContextSqlite() : base(CreateDesignTimeOptions()) { }

    /// <summary>
    /// Runtime constructor for DI.
    /// </summary>
    public SamlDbContextSqlite(DbContextOptions<SamlDbContextSqlite> options)
        : base(options) { }

    private static DbContextOptions<SamlDbContext> CreateDesignTimeOptions()
    {
        var builder = new DbContextOptionsBuilder<SamlDbContext>();
        builder.UseSqlite("Data Source=:memory:",
            o => o.MigrationsHistoryTable(PluginDbContextExtensions.GetMigrationsTableName(PluginIdentifier)));
        return builder.Options;
    }

    protected override void OnConfiguring(DbContextOptionsBuilder optionsBuilder)
    {
        if (!optionsBuilder.IsConfigured)
        {
            optionsBuilder.UseSqlite("Data Source=Saml.db",
                o => o.MigrationsHistoryTable(PluginDbContextExtensions.GetMigrationsTableName(PluginIdentifier)));
        }
    }
}

/// <summary>
/// SQL Server-specific SamlDbContext for migrations and runtime.
/// Register this context when using SQL Server to ensure migrations are found.
/// </summary>
public class SamlDbContextSqlServer : SamlDbContext
{
    /// <summary>
    /// Design-time constructor for EF Core tooling.
    /// </summary>
    public SamlDbContextSqlServer() : base(CreateDesignTimeOptions()) { }

    /// <summary>
    /// Runtime constructor for DI.
    /// </summary>
    public SamlDbContextSqlServer(DbContextOptions<SamlDbContextSqlServer> options)
        : base(options) { }

    private static DbContextOptions<SamlDbContext> CreateDesignTimeOptions()
    {
        var builder = new DbContextOptionsBuilder<SamlDbContext>();
        builder.UseSqlServer("Server=.;Database=Saml;Trusted_Connection=True;TrustServerCertificate=True",
            o => o.MigrationsHistoryTable(PluginDbContextExtensions.GetMigrationsTableName(PluginIdentifier)));
        return builder.Options;
    }

    protected override void OnConfiguring(DbContextOptionsBuilder optionsBuilder)
    {
        if (!optionsBuilder.IsConfigured)
        {
            optionsBuilder.UseSqlServer("Server=.;Database=Saml;Trusted_Connection=True;TrustServerCertificate=True",
                o => o.MigrationsHistoryTable(PluginDbContextExtensions.GetMigrationsTableName(PluginIdentifier)));
        }
    }
}

/// <summary>
/// PostgreSQL-specific SamlDbContext for migrations and runtime.
/// Register this context when using PostgreSQL to ensure migrations are found.
/// </summary>
public class SamlDbContextPostgres : SamlDbContext
{
    /// <summary>
    /// Design-time constructor for EF Core tooling.
    /// </summary>
    public SamlDbContextPostgres() : base(CreateDesignTimeOptions()) { }

    /// <summary>
    /// Runtime constructor for DI.
    /// </summary>
    public SamlDbContextPostgres(DbContextOptions<SamlDbContextPostgres> options)
        : base(options) { }

    private static DbContextOptions<SamlDbContext> CreateDesignTimeOptions()
    {
        var builder = new DbContextOptionsBuilder<SamlDbContext>();
        builder.UseNpgsql("Host=localhost;Database=Saml;Username=postgres;Password=postgres",
            o => o.MigrationsHistoryTable(PluginDbContextExtensions.GetMigrationsTableNamePostgres(PluginIdentifier)));
        return builder.Options;
    }

    protected override void OnConfiguring(DbContextOptionsBuilder optionsBuilder)
    {
        if (!optionsBuilder.IsConfigured)
        {
            optionsBuilder.UseNpgsql("Host=localhost;Database=Saml;Username=postgres;Password=postgres",
                o => o.MigrationsHistoryTable(PluginDbContextExtensions.GetMigrationsTableNamePostgres(PluginIdentifier)));
        }
    }
}
