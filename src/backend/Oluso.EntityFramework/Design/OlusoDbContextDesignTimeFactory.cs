using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;

namespace Oluso.EntityFramework.Design;

/// <summary>
/// Design-time factory for creating OlusoDbContext for EF Core migrations.
///
/// Usage (from repo root):
/// <code>
/// # SQLite migrations
/// dotnet ef migrations add Initial --context OlusoDbContext --project src/backend/Oluso.EntityFramework --startup-project samples/Oluso.Sample --output-dir Migrations/Sqlite -- --provider Sqlite
///
/// # SQL Server migrations
/// dotnet ef migrations add Initial --context OlusoDbContext --project src/backend/Oluso.EntityFramework --startup-project samples/Oluso.Sample --output-dir Migrations/SqlServer -- --provider SqlServer
///
/// # PostgreSQL migrations
/// dotnet ef migrations add Initial --context OlusoDbContext --project src/backend/Oluso.EntityFramework --startup-project samples/Oluso.Sample --output-dir Migrations/Postgres -- --provider Postgres
/// </code>
/// </summary>
public class OlusoDbContextDesignTimeFactory : IDesignTimeDbContextFactory<OlusoDbContext>
{
    public OlusoDbContext CreateDbContext(string[] args)
    {
        var provider = GetProvider(args);
        var optionsBuilder = new DbContextOptionsBuilder<OlusoDbContext>();

        switch (provider.ToLowerInvariant())
        {
            case "sqlserver":
            case "mssql":
                // Use a dummy connection string for migrations - actual connection comes at runtime
                optionsBuilder.UseSqlServer(
                    "Server=.;Database=OlusoMigrations;Trusted_Connection=True;TrustServerCertificate=True",
                    x => x.MigrationsAssembly(typeof(OlusoDbContext).Assembly.FullName));
                break;

            case "postgres":
            case "postgresql":
            case "npgsql":
                optionsBuilder.UseNpgsql(
                    "Host=localhost;Database=OlusoMigrations;Username=postgres;Password=postgres",
                    x => x.MigrationsAssembly(typeof(OlusoDbContext).Assembly.FullName));
                break;

            case "sqlite":
            default:
                optionsBuilder.UseSqlite(
                    "Data Source=Oluso.db",
                    x => x.MigrationsAssembly(typeof(OlusoDbContext).Assembly.FullName));
                break;
        }

        return new OlusoDbContext(optionsBuilder.Options);
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
