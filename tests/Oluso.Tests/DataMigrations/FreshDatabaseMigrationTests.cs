using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Oluso.Core.Domain.Entities;
using Oluso.EntityFramework.Design;
using Oluso.Testing.DataMigrations;
using Xunit;

namespace Oluso.Tests.DataMigrations;

/// <summary>
/// A new core database is built as a deploy builds it: every schema migration in order, each followed by the data
/// migrations bound to it. A tenant user's role assignment from before AddUserRoleTenantId gives the backfill bound to it a row
/// to stamp, and no data migration bound to a schema migration may load or save whole rows.
/// </summary>
public sealed class FreshDatabaseMigrationTests
{
    [Fact]
    public async Task A_new_database_takes_every_schema_and_data_migration()
    {
        await using var database = new FreshSqliteDatabase<OlusoDbContextSqlite>(options => new OlusoDbContextSqlite(options))
            .Before("AddUserRoleTenantId", async rows =>
            {
                await rows.InsertAsync<Organization>(new Dictionary<string, object?> { ["Id"] = "org", ["Name"] = "Acme" });
                await rows.InsertAsync<Tenant>(new Dictionary<string, object?> { ["Id"] = "t1", ["Name"] = "Acme", ["OrganizationId"] = "org" });
                await rows.InsertAsync<OlusoUser>(new Dictionary<string, object?>
                {
                    ["Id"] = "ada", ["TenantId"] = "t1", ["UserName"] = "ada@x.test", ["NormalizedUserName"] = "ADA@X.TEST", ["IsActive"] = true,
                });
                await rows.InsertAsync<OlusoRole>(new Dictionary<string, object?>
                {
                    ["Id"] = "viewer", ["TenantId"] = "t1", ["Name"] = "Viewer", ["NormalizedName"] = "VIEWER",
                });
                await rows.InsertAsync<OlusoUserRole>(new Dictionary<string, object?> { ["UserId"] = "ada", ["RoleId"] = "viewer" });
            });

        await database.MigrateAsync(new ServiceCollection().BuildServiceProvider());

        database.Violations.Should().BeEmpty();
        (await database.AppliedDataMigrationIdsAsync()).Should().Equal(database.DeclaredDataMigrationIds);

        await using var db = database.Context();
        (await db.UserRoles.AsNoTracking().SingleAsync()).TenantId.Should().Be("t1");
    }
}
