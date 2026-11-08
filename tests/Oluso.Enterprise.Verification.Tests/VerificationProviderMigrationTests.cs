using FluentAssertions;
using Oluso.Enterprise.Verification;
using Oluso.Testing.ProviderMigrations;

namespace Oluso.Enterprise.Verification.Tests;

/// <summary>
/// Applies every schema and data migration of the verification context to an empty SQL Server and PostgreSQL database.
/// Opt-in: each test is skipped unless its server's environment variable is set.
/// </summary>
public class VerificationProviderMigrationTests
{
    [SqlServerFact]
    public Task Migrates_from_empty_on_SqlServer() => MigratesFromEmptyAsync(MigrationTestProvider.SqlServer);

    [PostgresFact]
    public Task Migrates_from_empty_on_Postgres() => MigratesFromEmptyAsync(MigrationTestProvider.Postgres);

    private static async Task MigratesFromEmptyAsync(MigrationTestProvider target)
    {
        var pending = await ProviderMigrationHarness.MigrateFromEmptyAsync(target, "verification",
            (services, provider, connectionString) => services.AddOlusoVerificationForProvider(provider, connectionString));

        pending.Should().NotBeEmpty();
        pending.Should().AllSatisfy(p => p.Value.Should().BeEmpty($"{p.Key} applied every migration"));
    }
}
