using Microsoft.EntityFrameworkCore;
using Oluso.Core.Data;

namespace Oluso.Enterprise.Verification.EntityFramework;

/// <summary>SQLite context; its migrations live in Migrations/Sqlite.</summary>
public class VerificationDbContextSqlite : VerificationDbContext
{
    /// <summary>Design-time constructor for EF Core tooling.</summary>
    public VerificationDbContextSqlite() : base(CreateDesignTimeOptions()) { }

    public VerificationDbContextSqlite(DbContextOptions<VerificationDbContextSqlite> options) : base(options) { }

    private static DbContextOptions<VerificationDbContext> CreateDesignTimeOptions()
    {
        var builder = new DbContextOptionsBuilder<VerificationDbContext>();
        builder.UseSqlite("Data Source=:memory:",
            o => o.MigrationsHistoryTable(PluginDbContextExtensions.GetMigrationsTableName(PluginIdentifier)));
        return builder.Options;
    }
}

/// <summary>SQL Server context; its migrations live in Migrations/SqlServer.</summary>
public class VerificationDbContextSqlServer : VerificationDbContext
{
    public VerificationDbContextSqlServer() : base(CreateDesignTimeOptions()) { }

    public VerificationDbContextSqlServer(DbContextOptions<VerificationDbContextSqlServer> options) : base(options) { }

    private static DbContextOptions<VerificationDbContext> CreateDesignTimeOptions()
    {
        var builder = new DbContextOptionsBuilder<VerificationDbContext>();
        builder.UseSqlServer("Server=.;Database=Verification;Trusted_Connection=True;TrustServerCertificate=True",
            o => o.MigrationsHistoryTable(PluginDbContextExtensions.GetMigrationsTableName(PluginIdentifier)));
        return builder.Options;
    }
}

/// <summary>PostgreSQL context; its migrations live in Migrations/Postgres.</summary>
public class VerificationDbContextPostgres : VerificationDbContext
{
    public VerificationDbContextPostgres() : base(CreateDesignTimeOptions()) { }

    public VerificationDbContextPostgres(DbContextOptions<VerificationDbContextPostgres> options) : base(options) { }

    private static DbContextOptions<VerificationDbContext> CreateDesignTimeOptions()
    {
        var builder = new DbContextOptionsBuilder<VerificationDbContext>();
        builder.UseNpgsql("Host=localhost;Database=Verification;Username=postgres;Password=postgres",
            o => o.MigrationsHistoryTable(PluginDbContextExtensions.GetMigrationsTableNamePostgres(PluginIdentifier)));
        return builder.Options;
    }
}
