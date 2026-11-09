using Oluso.Testing.ProviderMigrations;
using Xunit;

namespace Oluso.Testing.DataMigrations;

/// <summary>A test that runs against a real SQL Server or PostgreSQL server, skipped when none is configured.</summary>
public sealed class ProviderFactAttribute : FactAttribute
{
    public ProviderFactAttribute(MigrationTestProvider provider)
    {
        if (ProviderTestServer.ConnectionString(provider) == null)
        {
            var variable = provider == MigrationTestProvider.SqlServer ? ProviderTestServer.SqlServerVariable : ProviderTestServer.PostgresVariable;
            Skip = $"Set {variable} to a {provider} server connection string to run";
        }
    }
}
