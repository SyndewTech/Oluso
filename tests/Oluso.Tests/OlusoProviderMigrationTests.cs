using FluentAssertions;
using Microsoft.Extensions.Configuration;
using Oluso;
using Oluso.Testing.ProviderMigrations;

namespace Oluso.Tests;

/// <summary>
/// Applies every schema and data migration of the Oluso core context to an empty SQL Server and PostgreSQL database.
/// Opt-in: each test is skipped unless its server's environment variable is set.
/// </summary>
public class OlusoProviderMigrationTests
{
    [SqlServerFact]
    public Task Migrates_from_empty_on_SqlServer() => MigratesFromEmptyAsync(MigrationTestProvider.SqlServer);

    [PostgresFact]
    public Task Migrates_from_empty_on_Postgres() => MigratesFromEmptyAsync(MigrationTestProvider.Postgres);

    private static async Task MigratesFromEmptyAsync(MigrationTestProvider target)
    {
        var pending = await ProviderMigrationHarness.MigrateFromEmptyAsync(target, "oluso",
            (services, provider, connectionString) => services.AddOluso(new ConfigurationBuilder().Build()).AddEntityFrameworkStoresForProvider(provider, connectionString));

        pending.Should().NotBeEmpty();
        pending.Should().AllSatisfy(p => p.Value.Should().BeEmpty($"{p.Key} applied every migration"));
    }
}
