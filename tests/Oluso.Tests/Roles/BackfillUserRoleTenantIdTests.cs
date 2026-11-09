using FluentAssertions;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using Microsoft.Extensions.DependencyInjection;
using Oluso.Core;
using Oluso.Core.Domain.Entities;
using Oluso.EntityFramework.DataMigrations;
using Oluso.EntityFramework.Design;
using Xunit;

namespace Oluso.Tests.Roles;

/// <summary>
/// The role-assignment tenant stamp goes in over three steps: schema migration
/// AddUserRoleTenantId (nullable column), the BackfillUserRoleTenantId data
/// migration, then RequireUserRoleTenantId (non-null, part of the key). Runs
/// the real SQLite migrations against representative rows.
/// </summary>
public class BackfillUserRoleTenantIdTests : IDisposable
{
    private readonly SqliteConnection _connection;
    private readonly OlusoDbContextSqlite _context;
    private readonly IServiceProvider _services = new ServiceCollection().BuildServiceProvider();

    public BackfillUserRoleTenantIdTests()
    {
        _connection = new SqliteConnection("DataSource=:memory:;Foreign Keys=False");
        _connection.Open();
        _context = new OlusoDbContextSqlite(new DbContextOptionsBuilder<OlusoDbContextSqlite>()
            .UseSqlite(_connection)
            .Options);
    }

    public void Dispose()
    {
        _context.Dispose();
        _connection.Dispose();
    }

    private string MigrationEndingWith(string name) =>
        _context.Database.GetMigrations().Single(m => m.EndsWith("_" + name, StringComparison.Ordinal));

    private Task MigrateToAsync(string name) =>
        _context.GetService<IMigrator>().MigrateAsync(MigrationEndingWith(name));

    private async Task<OlusoRole> RoleAsync(string name, string? tenantId)
    {
        var role = new OlusoRole
        {
            Id = $"role-{name}-{tenantId ?? "platform"}",
            Name = name,
            NormalizedName = name.ToUpperInvariant(),
            TenantId = tenantId,
            IsSystemRole = tenantId == null,
        };
        _context.Roles.Add(role);
        await _context.SaveChangesAsync();
        return role;
    }

    /// <summary>
    /// A user row as the schema stands at the migration under test. Written with SQL rather than
    /// the current model, which may map columns later migrations add; NOT NULL columns the row
    /// doesn't care about get a neutral value.
    /// </summary>
    private async Task<OlusoUser> UserAsync(string id, string? homeTenantId)
    {
        var values = new Dictionary<string, object>(StringComparer.Ordinal)
        {
            ["Id"] = id,
            ["UserName"] = $"{id}@x.test",
            ["NormalizedUserName"] = $"{id}@X.TEST",
            ["Email"] = $"{id}@x.test",
            ["NormalizedEmail"] = $"{id}@X.TEST",
            ["TenantId"] = (object?)homeTenantId ?? DBNull.Value,
            ["IsActive"] = 1,
        };

        var columns = await _context.Database
            .SqlQueryRaw<ColumnRow>("SELECT name AS Name, \"notnull\" AS IsRequired, type AS Type FROM pragma_table_info('AspNetUsers')")
            .ToListAsync();
        foreach (var column in columns.Where(c => c.IsRequired == 1 && !values.ContainsKey(c.Name)))
        {
            values[column.Name] = column.Type == "INTEGER" ? 0 : column.Type == "TEXT" ? DateTime.UtcNow.ToString("O") : string.Empty;
        }

        var names = values.Keys.ToList();
        var sql = $"INSERT INTO AspNetUsers ({string.Join(", ", names.Select(n => $"\"{n}\""))}) " +
                  $"VALUES ({string.Join(", ", names.Select((_, i) => $"{{{i}}}"))})";
        await _context.Database.ExecuteSqlRawAsync(sql, names.Select(n => values[n]));

        return new OlusoUser { Id = id, TenantId = homeTenantId };
    }

    /// <summary>
    /// A row as it exists before the backfill: no tenant.
    /// </summary>
    private Task HoldUnstampedAsync(OlusoUser user, OlusoRole role) =>
        _context.Database.ExecuteSqlInterpolatedAsync(
            $"INSERT INTO AspNetUserRoles (UserId, RoleId, AssignedAt, AssignedBy) VALUES ({user.Id}, {role.Id}, {DateTime.UtcNow}, 'seed')");

    private async Task<Dictionary<(string UserId, string RoleId), string?>> StampsAsync()
    {
        var rows = await _context.Database
            .SqlQueryRaw<StampRow>("SELECT UserId, RoleId, TenantId FROM AspNetUserRoles")
            .ToListAsync();
        return rows.ToDictionary(r => (r.UserId, r.RoleId), r => r.TenantId);
    }

    [Fact]
    public async Task Backfill_StampsEachKindOfAssignment_ThenTheKeyChangeApplies()
    {
        await MigrateToAsync("AddUserRoleTenantId");

        var viewerT1 = await RoleAsync("Viewer", "t1");
        var viewerT2 = await RoleAsync("Viewer", "t2");
        var platformOps = await RoleAsync("PlatformOps", PlatformConstants.TenantId);
        var hrAdmin = await RoleAsync("HRAdmin", null);
        var superAdmin = await RoleAsync("SuperAdmin", null);
        var orgAdmin = await RoleAsync("OrgAdmin", null);

        var ada = await UserAsync("ada", "t1");
        var root = await UserAsync("root", PlatformConstants.TenantId);
        var owner = await UserAsync("owner", "t2");

        await HoldUnstampedAsync(ada, viewerT1);      // tenant-owned, home tenant
        await HoldUnstampedAsync(ada, viewerT2);      // tenant-owned, org member of t2
        await HoldUnstampedAsync(root, platformOps);  // owned by the platform tenant
        await HoldUnstampedAsync(ada, hrAdmin);       // platform-wide
        await HoldUnstampedAsync(root, superAdmin);   // platform-wide, global by role
        await HoldUnstampedAsync(owner, orgAdmin);    // platform-wide, homed in a real tenant

        await new BackfillUserRoleTenantId().UpAsync(_context, _services);

        var stamps = await StampsAsync();
        stamps.Should().BeEquivalentTo(new Dictionary<(string, string), string?>
        {
            // From the role's own tenant: 3 rows.
            [(ada.Id, viewerT1.Id)] = "t1",
            [(ada.Id, viewerT2.Id)] = "t2",
            [(root.Id, platformOps.Id)] = PlatformConstants.TenantId,
            // From the user's home tenant: 3 rows.
            [(ada.Id, hrAdmin.Id)] = "t1",
            [(root.Id, superAdmin.Id)] = PlatformConstants.TenantId,
            [(owner.Id, orgAdmin.Id)] = "t2",
        });

        // Rerunning is a no-op: every row is already stamped.
        await new BackfillUserRoleTenantId().UpAsync(_context, _services);
        (await StampsAsync()).Should().BeEquivalentTo(stamps);

        await MigrateToAsync("RequireUserRoleTenantId");

        // The same platform-wide role can now be held in a second tenant.
        _context.UserRoles.Add(new OlusoUserRole { UserId = ada.Id, RoleId = hrAdmin.Id, TenantId = "t2" });
        await _context.SaveChangesAsync();
        (await _context.UserRoles.CountAsync(ur => ur.UserId == ada.Id && ur.RoleId == hrAdmin.Id)).Should().Be(2);
    }

    [Fact]
    public async Task Backfill_Throws_NamingTheRowsItCannotStamp()
    {
        await MigrateToAsync("AddUserRoleTenantId");

        var hrAdmin = await RoleAsync("HRAdmin", null);
        var viewer = await RoleAsync("Viewer", "t1");
        // The column is NOT NULL, so a user without a home tenant carries "".
        var homeless = await UserAsync("homeless", "");
        var ada = await UserAsync("ada", "t1");
        await HoldUnstampedAsync(homeless, hrAdmin);
        await HoldUnstampedAsync(ada, viewer);

        var backfill = () => new BackfillUserRoleTenantId().UpAsync(_context, _services);

        (await backfill.Should().ThrowAsync<InvalidOperationException>())
            .Which.Message.Should().Contain($"UserId={homeless.Id}, RoleId={hrAdmin.Id}")
            .And.NotContain($"RoleId={viewer.Id}");
        (await StampsAsync())[(ada.Id, viewer.Id)].Should().Be("t1");
    }

    [Fact]
    public async Task RequireMigration_FailsOnARowLeftWithoutATenant_InsteadOfDefaultingIt()
    {
        await MigrateToAsync("AddUserRoleTenantId");

        var hrAdmin = await RoleAsync("HRAdmin", null);
        var ada = await UserAsync("ada", "t1");
        await HoldUnstampedAsync(ada, hrAdmin);

        var require = () => MigrateToAsync("RequireUserRoleTenantId");

        await require.Should().ThrowAsync<SqliteException>();
        (await StampsAsync())[(ada.Id, hrAdmin.Id)].Should().BeNull();
    }

    private sealed class ColumnRow
    {
        public string Name { get; set; } = default!;
        public long IsRequired { get; set; }
        public string Type { get; set; } = default!;
    }

    private sealed class StampRow
    {
        public string UserId { get; set; } = default!;
        public string RoleId { get; set; } = default!;
        public string? TenantId { get; set; }
    }
}
