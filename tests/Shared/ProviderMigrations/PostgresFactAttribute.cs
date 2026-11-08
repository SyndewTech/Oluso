using Xunit;

namespace Oluso.Testing.ProviderMigrations;

/// <summary>
/// A test that needs a PostgreSQL server. Skipped unless <c>OLUSO_TEST_POSTGRES</c> holds a connection
/// string to a server the test may create and drop databases on, e.g.
/// <c>Host=localhost;Port=5432;Username=postgres;Password=...</c>.
/// </summary>
public sealed class PostgresFactAttribute : FactAttribute
{
    public PostgresFactAttribute()
    {
        if (ProviderTestDatabase.ServerConnectionString(MigrationTestProvider.Postgres) == null)
            Skip = $"Set {ProviderTestDatabase.EnvironmentVariable(MigrationTestProvider.Postgres)} to run against PostgreSQL";
    }
}
