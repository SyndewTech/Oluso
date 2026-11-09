using Xunit;

namespace Oluso.Testing.ProviderMigrations;

/// <summary>
/// A test that needs a SQL Server instance. Skipped unless <c>OLUSO_TEST_SQLSERVER</c> holds a connection
/// string to a server the test may create and drop databases on, e.g.
/// <c>Server=127.0.0.1,1433;User Id=sa;Password=...;TrustServerCertificate=True</c>.
/// </summary>
public sealed class SqlServerFactAttribute : FactAttribute
{
    public SqlServerFactAttribute()
    {
        if (ProviderTestDatabase.ServerConnectionString(MigrationTestProvider.SqlServer) == null)
            Skip = $"Set {ProviderTestDatabase.EnvironmentVariable(MigrationTestProvider.SqlServer)} to run against SQL Server";
    }
}
