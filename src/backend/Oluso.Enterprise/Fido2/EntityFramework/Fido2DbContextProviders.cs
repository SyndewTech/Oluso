using Microsoft.EntityFrameworkCore;
using Oluso.Core.Data;

namespace Oluso.Enterprise.Fido2.EntityFramework;

/// <summary>
/// SQLite-specific Fido2DbContext for migrations and runtime.
/// Register this context when using SQLite to ensure migrations are found.
/// </summary>
public class Fido2DbContextSqlite : Fido2DbContext
{
    /// <summary>
    /// Design-time constructor for EF Core tooling.
    /// </summary>
    public Fido2DbContextSqlite() : base(CreateDesignTimeOptions()) { }

    /// <summary>
    /// Runtime constructor for DI.
    /// </summary>
    public Fido2DbContextSqlite(DbContextOptions<Fido2DbContextSqlite> options)
        : base(options) { }

    private static DbContextOptions<Fido2DbContext> CreateDesignTimeOptions()
    {
        var builder = new DbContextOptionsBuilder<Fido2DbContext>();
        builder.UseSqlite("Data Source=:memory:",
            o => o.MigrationsHistoryTable(PluginDbContextExtensions.GetMigrationsTableName(PluginIdentifier)));
        return builder.Options;
    }

    protected override void OnConfiguring(DbContextOptionsBuilder optionsBuilder)
    {
        if (!optionsBuilder.IsConfigured)
        {
            optionsBuilder.UseSqlite("Data Source=Fido2.db",
                o => o.MigrationsHistoryTable(PluginDbContextExtensions.GetMigrationsTableName(PluginIdentifier)));
        }
    }
}

/// <summary>
/// SQL Server-specific Fido2DbContext for migrations and runtime.
/// Register this context when using SQL Server to ensure migrations are found.
/// </summary>
public class Fido2DbContextSqlServer : Fido2DbContext
{
    /// <summary>
    /// Design-time constructor for EF Core tooling.
    /// </summary>
    public Fido2DbContextSqlServer() : base(CreateDesignTimeOptions()) { }

    /// <summary>
    /// Runtime constructor for DI.
    /// </summary>
    public Fido2DbContextSqlServer(DbContextOptions<Fido2DbContextSqlServer> options)
        : base(options) { }

    private static DbContextOptions<Fido2DbContext> CreateDesignTimeOptions()
    {
        var builder = new DbContextOptionsBuilder<Fido2DbContext>();
        builder.UseSqlServer("Server=.;Database=Fido2;Trusted_Connection=True;TrustServerCertificate=True",
            o => o.MigrationsHistoryTable(PluginDbContextExtensions.GetMigrationsTableName(PluginIdentifier)));
        return builder.Options;
    }

    protected override void OnConfiguring(DbContextOptionsBuilder optionsBuilder)
    {
        if (!optionsBuilder.IsConfigured)
        {
            optionsBuilder.UseSqlServer("Server=.;Database=Fido2;Trusted_Connection=True;TrustServerCertificate=True",
                o => o.MigrationsHistoryTable(PluginDbContextExtensions.GetMigrationsTableName(PluginIdentifier)));
        }
    }
}

/// <summary>
/// PostgreSQL-specific Fido2DbContext for migrations and runtime.
/// Register this context when using PostgreSQL to ensure migrations are found.
/// </summary>
public class Fido2DbContextPostgres : Fido2DbContext
{
    /// <summary>
    /// Design-time constructor for EF Core tooling.
    /// </summary>
    public Fido2DbContextPostgres() : base(CreateDesignTimeOptions()) { }

    /// <summary>
    /// Runtime constructor for DI.
    /// </summary>
    public Fido2DbContextPostgres(DbContextOptions<Fido2DbContextPostgres> options)
        : base(options) { }

    private static DbContextOptions<Fido2DbContext> CreateDesignTimeOptions()
    {
        var builder = new DbContextOptionsBuilder<Fido2DbContext>();
        builder.UseNpgsql("Host=localhost;Database=Fido2;Username=postgres;Password=postgres",
            o => o.MigrationsHistoryTable(PluginDbContextExtensions.GetMigrationsTableNamePostgres(PluginIdentifier)));
        return builder.Options;
    }

    protected override void OnConfiguring(DbContextOptionsBuilder optionsBuilder)
    {
        if (!optionsBuilder.IsConfigured)
        {
            optionsBuilder.UseNpgsql("Host=localhost;Database=Fido2;Username=postgres;Password=postgres",
                o => o.MigrationsHistoryTable(PluginDbContextExtensions.GetMigrationsTableNamePostgres(PluginIdentifier)));
        }
    }
}
