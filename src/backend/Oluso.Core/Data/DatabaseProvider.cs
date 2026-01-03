namespace Oluso.Core.Data;

/// <summary>
/// Supported database providers for Oluso.
/// </summary>
public enum DatabaseProvider
{
    /// <summary>
    /// SQLite database (default for development)
    /// </summary>
    Sqlite,

    /// <summary>
    /// Microsoft SQL Server
    /// </summary>
    SqlServer,

    /// <summary>
    /// PostgreSQL (via Npgsql)
    /// </summary>
    Postgres
}

/// <summary>
/// Helper methods for database provider detection and configuration.
/// </summary>
public static class DatabaseProviderHelper
{
    /// <summary>
    /// Gets the migrations namespace suffix for a database provider.
    /// </summary>
    /// <param name="provider">The database provider.</param>
    /// <returns>Namespace suffix (e.g., "SqlServer", "Postgres", "Sqlite").</returns>
    public static string GetMigrationsNamespaceSuffix(DatabaseProvider provider) => provider switch
    {
        DatabaseProvider.SqlServer => "SqlServer",
        DatabaseProvider.Postgres => "Postgres",
        DatabaseProvider.Sqlite => "Sqlite",
        _ => "Sqlite"
    };

    /// <summary>
    /// Gets the full migrations namespace for a context type and provider.
    /// </summary>
    /// <param name="contextType">The DbContext type.</param>
    /// <param name="provider">The database provider.</param>
    /// <returns>Full namespace for migrations (e.g., "Oluso.EntityFramework.Migrations.SqlServer").</returns>
    public static string GetMigrationsNamespace(Type contextType, DatabaseProvider provider)
    {
        var assemblyName = contextType.Assembly.GetName().Name;
        var suffix = GetMigrationsNamespaceSuffix(provider);
        return $"{assemblyName}.Migrations.{suffix}";
    }

    /// <summary>
    /// Detects the database provider from EF Core provider name.
    /// </summary>
    /// <param name="providerName">The EF Core provider name (e.g., "Microsoft.EntityFrameworkCore.SqlServer").</param>
    /// <returns>The detected database provider.</returns>
    public static DatabaseProvider DetectFromProviderName(string? providerName)
    {
        if (string.IsNullOrEmpty(providerName))
            return DatabaseProvider.Sqlite;

        if (providerName.Contains("SqlServer", StringComparison.OrdinalIgnoreCase) ||
            providerName.Contains("Mssql", StringComparison.OrdinalIgnoreCase))
            return DatabaseProvider.SqlServer;

        if (providerName.Contains("Npgsql", StringComparison.OrdinalIgnoreCase) ||
            providerName.Contains("Postgres", StringComparison.OrdinalIgnoreCase))
            return DatabaseProvider.Postgres;

        if (providerName.Contains("Sqlite", StringComparison.OrdinalIgnoreCase))
            return DatabaseProvider.Sqlite;

        return DatabaseProvider.Sqlite;
    }

    /// <summary>
    /// Parses a provider string (from command line or environment variable) to DatabaseProvider.
    /// </summary>
    /// <param name="providerString">Provider string (e.g., "sqlserver", "postgres", "sqlite").</param>
    /// <returns>The parsed database provider.</returns>
    public static DatabaseProvider ParseProviderString(string? providerString)
    {
        if (string.IsNullOrEmpty(providerString))
            return DatabaseProvider.Sqlite;

        return providerString.ToLowerInvariant() switch
        {
            "sqlserver" or "mssql" => DatabaseProvider.SqlServer,
            "postgres" or "postgresql" or "npgsql" => DatabaseProvider.Postgres,
            "sqlite" => DatabaseProvider.Sqlite,
            _ => DatabaseProvider.Sqlite
        };
    }

    /// <summary>
    /// Gets the provider from command line args or environment variable.
    /// </summary>
    /// <param name="args">Command line arguments.</param>
    /// <returns>The detected database provider.</returns>
    public static DatabaseProvider GetProviderFromArgs(string[] args)
    {
        // Look for --provider argument
        for (int i = 0; i < args.Length - 1; i++)
        {
            if (args[i].Equals("--provider", StringComparison.OrdinalIgnoreCase))
            {
                return ParseProviderString(args[i + 1]);
            }
        }

        // Check environment variable
        var envProvider = Environment.GetEnvironmentVariable("OLUSO_DB_PROVIDER");
        if (!string.IsNullOrEmpty(envProvider))
        {
            return ParseProviderString(envProvider);
        }

        // Default to SQLite
        return DatabaseProvider.Sqlite;
    }
}
