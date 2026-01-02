using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;
using Oluso.Core.Data;

namespace Oluso.Enterprise.Scim.EntityFramework;

/// <summary>
/// Design-time factory for creating ScimDbContext for EF Core migrations.
///
/// Usage (from repo root):
/// <code>
/// # SQLite migrations
/// dotnet ef migrations add Initial --context ScimDbContext --project src/backend/Oluso.Enterprise/Scim --startup-project samples/Oluso.Sample --output-dir Migrations/Sqlite -- --provider Sqlite
///
/// # SQL Server migrations
/// dotnet ef migrations add Initial --context ScimDbContext --project src/backend/Oluso.Enterprise/Scim --startup-project samples/Oluso.Sample --output-dir Migrations/SqlServer -- --provider SqlServer
///
/// # PostgreSQL migrations
/// dotnet ef migrations add Initial --context ScimDbContext --project src/backend/Oluso.Enterprise/Scim --startup-project samples/Oluso.Sample --output-dir Migrations/Postgres -- --provider Postgres
/// </code>
/// </summary>
public class ScimDbContextDesignTimeFactory : IDesignTimeDbContextFactory<ScimDbContext>
{
    public ScimDbContext CreateDbContext(string[] args)
    {
        var provider = GetProvider(args);
        var optionsBuilder = new DbContextOptionsBuilder<ScimDbContext>();
        var migrationsTable = PluginDbContextExtensions.GetMigrationsTableName(ScimDbContext.PluginIdentifier);

        switch (provider.ToLowerInvariant())
        {
            case "sqlserver":
            case "mssql":
                optionsBuilder.UseSqlServer(
                    "Server=.;Database=ScimMigrations;Trusted_Connection=True;TrustServerCertificate=True",
                    x =>
                    {
                        x.MigrationsAssembly(typeof(ScimDbContext).Assembly.FullName);
                        x.MigrationsHistoryTable(migrationsTable);
                    });
                break;

            case "postgres":
            case "postgresql":
            case "npgsql":
                var pgTable = PluginDbContextExtensions.GetMigrationsTableNamePostgres(ScimDbContext.PluginIdentifier);
                optionsBuilder.UseNpgsql(
                    "Host=localhost;Database=ScimMigrations;Username=postgres;Password=postgres",
                    x =>
                    {
                        x.MigrationsAssembly(typeof(ScimDbContext).Assembly.FullName);
                        x.MigrationsHistoryTable(pgTable);
                    });
                break;

            case "sqlite":
            default:
                optionsBuilder.UseSqlite(
                    "Data Source=ScimMigrations.db",
                    x =>
                    {
                        x.MigrationsAssembly(typeof(ScimDbContext).Assembly.FullName);
                        x.MigrationsHistoryTable(migrationsTable);
                    });
                break;
        }

        return new ScimDbContext(optionsBuilder.Options);
    }

    private static string GetProvider(string[] args)
    {
        // Look for --provider argument
        for (int i = 0; i < args.Length - 1; i++)
        {
            if (args[i].Equals("--provider", StringComparison.OrdinalIgnoreCase))
            {
                return args[i + 1];
            }
        }

        // Check environment variable
        var envProvider = Environment.GetEnvironmentVariable("OLUSO_DB_PROVIDER");
        if (!string.IsNullOrEmpty(envProvider))
        {
            return envProvider;
        }

        // Default to SQLite
        return "Sqlite";
    }
}
