using Oluso.Testing.ProviderMigrations;
using Microsoft.Data.SqlClient;
using Npgsql;

namespace Oluso.Testing.DataMigrations;

/// <summary>
/// A SQL Server or PostgreSQL server that migration tests may create throwaway databases on, named by an environment
/// variable holding a connection string to the server (no database): OLUSO_TEST_SQLSERVER and OLUSO_TEST_POSTGRES.
/// Without one the provider's tests are skipped, so CI runs on SQLite alone.
/// </summary>
public static class ProviderTestServer
{
    public const string SqlServerVariable = "OLUSO_TEST_SQLSERVER";
    public const string PostgresVariable = "OLUSO_TEST_POSTGRES";

    public static string? ConnectionString(MigrationTestProvider provider) =>
        Environment.GetEnvironmentVariable(provider == MigrationTestProvider.SqlServer ? SqlServerVariable : PostgresVariable) is { Length: > 0 } value
            ? value
            : null;

    /// <summary>Creates an empty database with a name of its own and returns a connection string to it.</summary>
    public static async Task<string> CreateDatabaseAsync(MigrationTestProvider provider, string prefix)
    {
        var server = ConnectionString(provider) ?? throw new InvalidOperationException($"No {provider} test server is configured");
        var name = $"{prefix}_{Guid.NewGuid():N}"[..Math.Min(prefix.Length + 13, 60)];
        if (provider == MigrationTestProvider.SqlServer)
        {
            await using var connection = new SqlConnection(new SqlConnectionStringBuilder(server) { InitialCatalog = "master" }.ConnectionString);
            await connection.OpenAsync();
            await using var command = new SqlCommand($"CREATE DATABASE [{name}]", connection);
            await command.ExecuteNonQueryAsync();
            return new SqlConnectionStringBuilder(server) { InitialCatalog = name }.ConnectionString;
        }

        await using (var connection = new NpgsqlConnection(new NpgsqlConnectionStringBuilder(server) { Database = "postgres" }.ConnectionString))
        {
            await connection.OpenAsync();
            await using var command = new NpgsqlCommand($"CREATE DATABASE \"{name}\"", connection);
            await command.ExecuteNonQueryAsync();
        }
        return new NpgsqlConnectionStringBuilder(server) { Database = name }.ConnectionString;
    }

    /// <summary>Drops a database <see cref="CreateDatabaseAsync"/> made, closing whatever connections it still has.</summary>
    public static async Task DropDatabaseAsync(MigrationTestProvider provider, string connectionString)
    {
        if (provider == MigrationTestProvider.SqlServer)
        {
            var builder = new SqlConnectionStringBuilder(connectionString);
            var name = builder.InitialCatalog;
            SqlConnection.ClearAllPools();
            await using var connection = new SqlConnection(new SqlConnectionStringBuilder(connectionString) { InitialCatalog = "master" }.ConnectionString);
            await connection.OpenAsync();
            await using var command = new SqlCommand(
                $"IF DB_ID(N'{name}') IS NOT NULL BEGIN ALTER DATABASE [{name}] SET SINGLE_USER WITH ROLLBACK IMMEDIATE; DROP DATABASE [{name}]; END", connection);
            await command.ExecuteNonQueryAsync();
            return;
        }

        var database = new NpgsqlConnectionStringBuilder(connectionString).Database;
        NpgsqlConnection.ClearAllPools();
        await using var admin = new NpgsqlConnection(new NpgsqlConnectionStringBuilder(connectionString) { Database = "postgres" }.ConnectionString);
        await admin.OpenAsync();
        await using var drop = new NpgsqlCommand($"DROP DATABASE IF EXISTS \"{database}\" WITH (FORCE)", admin);
        await drop.ExecuteNonQueryAsync();
    }
}
