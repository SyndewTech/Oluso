using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;
using Oluso.Core.Data;

namespace Oluso.Enterprise.Saml.EntityFramework;

/// <summary>
/// Design-time factory for creating SamlDbContext for EF Core migrations.
///
/// Usage (from repo root):
/// <code>
/// # SQLite migrations
/// dotnet ef migrations add Initial --context SamlDbContext --project src/backend/Oluso.Enterprise/Saml --startup-project samples/Oluso.Sample --output-dir Migrations/Sqlite -- --provider Sqlite
///
/// # SQL Server migrations
/// dotnet ef migrations add Initial --context SamlDbContext --project src/backend/Oluso.Enterprise/Saml --startup-project samples/Oluso.Sample --output-dir Migrations/SqlServer -- --provider SqlServer
///
/// # PostgreSQL migrations
/// dotnet ef migrations add Initial --context SamlDbContext --project src/backend/Oluso.Enterprise/Saml --startup-project samples/Oluso.Sample --output-dir Migrations/Postgres -- --provider Postgres
/// </code>
/// </summary>
public class SamlDbContextDesignTimeFactory : IDesignTimeDbContextFactory<SamlDbContext>
{
    public SamlDbContext CreateDbContext(string[] args)
    {
        var provider = GetProvider(args);
        var optionsBuilder = new DbContextOptionsBuilder<SamlDbContext>();
        var migrationsTable = PluginDbContextExtensions.GetMigrationsTableName(SamlDbContext.PluginIdentifier);

        switch (provider.ToLowerInvariant())
        {
            case "sqlserver":
            case "mssql":
                optionsBuilder.UseSqlServer(
                    "Server=.;Database=SamlMigrations;Trusted_Connection=True;TrustServerCertificate=True",
                    x =>
                    {
                        x.MigrationsAssembly(typeof(SamlDbContext).Assembly.FullName);
                        x.MigrationsHistoryTable(migrationsTable);
                    });
                break;

            case "postgres":
            case "postgresql":
            case "npgsql":
                var pgTable = PluginDbContextExtensions.GetMigrationsTableNamePostgres(SamlDbContext.PluginIdentifier);
                optionsBuilder.UseNpgsql(
                    "Host=localhost;Database=SamlMigrations;Username=postgres;Password=postgres",
                    x =>
                    {
                        x.MigrationsAssembly(typeof(SamlDbContext).Assembly.FullName);
                        x.MigrationsHistoryTable(pgTable);
                    });
                break;

            case "sqlite":
            default:
                optionsBuilder.UseSqlite(
                    "Data Source=SamlMigrations.db",
                    x =>
                    {
                        x.MigrationsAssembly(typeof(SamlDbContext).Assembly.FullName);
                        x.MigrationsHistoryTable(migrationsTable);
                    });
                break;
        }

        return new SamlDbContext(optionsBuilder.Options);
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
