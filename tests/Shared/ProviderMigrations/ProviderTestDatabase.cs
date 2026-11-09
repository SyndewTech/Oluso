using Microsoft.Data.SqlClient;
using Npgsql;

namespace Oluso.Testing.ProviderMigrations;

/// <summary>
/// An empty database created on the server named by the provider's environment variable and dropped on dispose.
/// Each instance gets its own uniquely named database, so tests never touch other databases on a shared server.
/// </summary>
public sealed class ProviderTestDatabase : IAsyncDisposable
{
    private readonly string _serverConnectionString;

    private ProviderTestDatabase(MigrationTestProvider provider, string serverConnectionString, string name)
    {
        Provider = provider;
        Name = name;
        _serverConnectionString = serverConnectionString;
        ConnectionString = provider switch
        {
            MigrationTestProvider.SqlServer => new SqlConnectionStringBuilder(serverConnectionString) { InitialCatalog = name }.ConnectionString,
            _ => new NpgsqlConnectionStringBuilder(serverConnectionString) { Database = name }.ConnectionString,
        };
    }

    public MigrationTestProvider Provider { get; }

    public string Name { get; }

    public string ConnectionString { get; }

    /// <summary>The provider name the modules' <c>Add…ForProvider</c> registrations accept.</summary>
    public string ProviderName => Provider == MigrationTestProvider.SqlServer ? "sqlserver" : "postgres";

    public static string EnvironmentVariable(MigrationTestProvider provider) =>
        provider == MigrationTestProvider.SqlServer ? "OLUSO_TEST_SQLSERVER" : "OLUSO_TEST_POSTGRES";

    public static string? ServerConnectionString(MigrationTestProvider provider) =>
        Environment.GetEnvironmentVariable(EnvironmentVariable(provider)) is { Length: > 0 } value ? value : null;

    public static async Task<ProviderTestDatabase> CreateAsync(MigrationTestProvider provider, string label)
    {
        var server = ServerConnectionString(provider)
            ?? throw new InvalidOperationException($"{EnvironmentVariable(provider)} is not set");
        // Postgres truncates identifiers at 63 characters.
        var name = $"oluso_test_{label.ToLowerInvariant()}_{Guid.NewGuid():N}"[..Math.Min(63, 24 + label.Length)];
        var database = new ProviderTestDatabase(provider, server, name);
        await database.ExecuteOnServerAsync(provider == MigrationTestProvider.SqlServer
            ? $"CREATE DATABASE [{name}]"
            : $"CREATE DATABASE \"{name}\"");
        return database;
    }

    public async ValueTask DisposeAsync()
    {
        if (Provider == MigrationTestProvider.SqlServer)
        {
            SqlConnection.ClearAllPools();
            await ExecuteOnServerAsync(
                $"IF DB_ID(N'{Name}') IS NOT NULL BEGIN ALTER DATABASE [{Name}] SET SINGLE_USER WITH ROLLBACK IMMEDIATE; DROP DATABASE [{Name}]; END");
        }
        else
        {
            NpgsqlConnection.ClearAllPools();
            await ExecuteOnServerAsync($"DROP DATABASE IF EXISTS \"{Name}\" WITH (FORCE)");
        }
    }

    private async Task ExecuteOnServerAsync(string sql)
    {
        if (Provider == MigrationTestProvider.SqlServer)
        {
            await using var connection = new SqlConnection(new SqlConnectionStringBuilder(_serverConnectionString) { InitialCatalog = "master" }.ConnectionString);
            await connection.OpenAsync();
            await using var command = new SqlCommand(sql, connection);
            await command.ExecuteNonQueryAsync();
        }
        else
        {
            var builder = new NpgsqlConnectionStringBuilder(_serverConnectionString);
            builder.Database ??= "postgres";
            builder.Pooling = false;
            await using var connection = new NpgsqlConnection(builder.ConnectionString);
            await connection.OpenAsync();
            await using var command = new NpgsqlCommand(sql, connection);
            await command.ExecuteNonQueryAsync();
        }
    }
}
