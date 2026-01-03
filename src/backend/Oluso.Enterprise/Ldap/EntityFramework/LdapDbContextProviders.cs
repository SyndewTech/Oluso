using Microsoft.EntityFrameworkCore;
using Oluso.Core.Data;

namespace Oluso.Enterprise.Ldap.EntityFramework;

/// <summary>
/// SQLite-specific LdapDbContext for migrations and runtime.
/// Register this context when using SQLite to ensure migrations are found.
/// </summary>
public class LdapDbContextSqlite : LdapDbContext
{
    /// <summary>
    /// Design-time constructor for EF Core tooling.
    /// </summary>
    public LdapDbContextSqlite() : base(CreateDesignTimeOptions()) { }

    /// <summary>
    /// Runtime constructor for DI.
    /// </summary>
    public LdapDbContextSqlite(DbContextOptions<LdapDbContextSqlite> options)
        : base(options) { }

    private static DbContextOptions<LdapDbContext> CreateDesignTimeOptions()
    {
        var builder = new DbContextOptionsBuilder<LdapDbContext>();
        builder.UseSqlite("Data Source=:memory:",
            o => o.MigrationsHistoryTable(PluginDbContextExtensions.GetMigrationsTableName(PluginIdentifier)));
        return builder.Options;
    }

    protected override void OnConfiguring(DbContextOptionsBuilder optionsBuilder)
    {
        if (!optionsBuilder.IsConfigured)
        {
            optionsBuilder.UseSqlite("Data Source=Ldap.db",
                o => o.MigrationsHistoryTable(PluginDbContextExtensions.GetMigrationsTableName(PluginIdentifier)));
        }
    }
}

/// <summary>
/// SQL Server-specific LdapDbContext for migrations and runtime.
/// Register this context when using SQL Server to ensure migrations are found.
/// </summary>
public class LdapDbContextSqlServer : LdapDbContext
{
    /// <summary>
    /// Design-time constructor for EF Core tooling.
    /// </summary>
    public LdapDbContextSqlServer() : base(CreateDesignTimeOptions()) { }

    /// <summary>
    /// Runtime constructor for DI.
    /// </summary>
    public LdapDbContextSqlServer(DbContextOptions<LdapDbContextSqlServer> options)
        : base(options) { }

    private static DbContextOptions<LdapDbContext> CreateDesignTimeOptions()
    {
        var builder = new DbContextOptionsBuilder<LdapDbContext>();
        builder.UseSqlServer("Server=.;Database=Ldap;Trusted_Connection=True;TrustServerCertificate=True",
            o => o.MigrationsHistoryTable(PluginDbContextExtensions.GetMigrationsTableName(PluginIdentifier)));
        return builder.Options;
    }

    protected override void OnConfiguring(DbContextOptionsBuilder optionsBuilder)
    {
        if (!optionsBuilder.IsConfigured)
        {
            optionsBuilder.UseSqlServer("Server=.;Database=Ldap;Trusted_Connection=True;TrustServerCertificate=True",
                o => o.MigrationsHistoryTable(PluginDbContextExtensions.GetMigrationsTableName(PluginIdentifier)));
        }
    }
}

/// <summary>
/// PostgreSQL-specific LdapDbContext for migrations and runtime.
/// Register this context when using PostgreSQL to ensure migrations are found.
/// </summary>
public class LdapDbContextPostgres : LdapDbContext
{
    /// <summary>
    /// Design-time constructor for EF Core tooling.
    /// </summary>
    public LdapDbContextPostgres() : base(CreateDesignTimeOptions()) { }

    /// <summary>
    /// Runtime constructor for DI.
    /// </summary>
    public LdapDbContextPostgres(DbContextOptions<LdapDbContextPostgres> options)
        : base(options) { }

    private static DbContextOptions<LdapDbContext> CreateDesignTimeOptions()
    {
        var builder = new DbContextOptionsBuilder<LdapDbContext>();
        builder.UseNpgsql("Host=localhost;Database=Ldap;Username=postgres;Password=postgres",
            o => o.MigrationsHistoryTable(PluginDbContextExtensions.GetMigrationsTableNamePostgres(PluginIdentifier)));
        return builder.Options;
    }

    protected override void OnConfiguring(DbContextOptionsBuilder optionsBuilder)
    {
        if (!optionsBuilder.IsConfigured)
        {
            optionsBuilder.UseNpgsql("Host=localhost;Database=Ldap;Username=postgres;Password=postgres",
                o => o.MigrationsHistoryTable(PluginDbContextExtensions.GetMigrationsTableNamePostgres(PluginIdentifier)));
        }
    }
}
