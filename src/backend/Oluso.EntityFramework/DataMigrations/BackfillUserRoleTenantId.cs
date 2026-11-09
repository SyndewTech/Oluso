using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Oluso.Core.Data;
using Oluso.Core.Domain.Entities;

namespace Oluso.EntityFramework.DataMigrations;

/// <summary>
/// Stamps every role assignment with the tenant it applies in, between the
/// schema migration that adds the nullable <c>TenantId</c> column
/// (AddUserRoleTenantId) and the one that makes it required and part of the key.
/// <list type="bullet">
///   <item>Tenant-owned role: the role's tenant.</item>
///   <item>Platform-wide role (<c>TenantId IS NULL</c>): the user's home tenant.
///   SuperAdmin and SystemAdmin get the same stamp; they apply in every tenant
///   by role, whatever the stamp.</item>
/// </list>
/// A row that can't be stamped — its role or user is gone, or the user has no
/// home tenant — fails the migration and is named, rather than left null for
/// the next schema migration to trip over.
/// </summary>
[DataMigration(typeof(OlusoDbContext))]
public class BackfillUserRoleTenantId : DataMigrationBase
{
    public override string MigrationId => "20260929_BackfillUserRoleTenantId";

    public override string? AfterSchemaMigration => "AddUserRoleTenantId";

    public override string Description =>
        "Stamp each role assignment with the tenant it applies in";

    public override async Task UpAsync(
        DbContext context, IServiceProvider services, CancellationToken cancellationToken = default)
    {
        var logger = services.GetService<ILoggerFactory>()?.CreateLogger<BackfillUserRoleTenantId>();

        var q = DataMigrationSqlHelpers.Q(context);
        var userRoles = DataMigrationSqlHelpers.QualifiedTable<OlusoUserRole>(context);
        var roles = DataMigrationSqlHelpers.QualifiedTable<OlusoRole>(context);
        var users = DataMigrationSqlHelpers.QualifiedTable<OlusoUser>(context);

        // Outer-row columns are left unqualified inside the subqueries: neither
        // the roles nor the users table has a RoleId/UserId column, so they bind
        // to the row being updated on every provider without aliasing the target.
        var tenantOwned = await context.Database.ExecuteSqlRawAsync($@"
            UPDATE {userRoles}
            SET {q}TenantId{q} = (
                SELECT r.{q}TenantId{q} FROM {roles} r WHERE r.{q}Id{q} = {q}RoleId{q})
            WHERE {q}TenantId{q} IS NULL
              AND EXISTS (
                SELECT 1 FROM {roles} r
                WHERE r.{q}Id{q} = {q}RoleId{q} AND r.{q}TenantId{q} IS NOT NULL)",
            cancellationToken);

        var platformWide = await context.Database.ExecuteSqlRawAsync($@"
            UPDATE {userRoles}
            SET {q}TenantId{q} = (
                SELECT u.{q}TenantId{q} FROM {users} u WHERE u.{q}Id{q} = {q}UserId{q})
            WHERE {q}TenantId{q} IS NULL
              AND EXISTS (
                SELECT 1 FROM {roles} r
                WHERE r.{q}Id{q} = {q}RoleId{q} AND r.{q}TenantId{q} IS NULL)
              AND EXISTS (
                SELECT 1 FROM {users} u
                WHERE u.{q}Id{q} = {q}UserId{q} AND u.{q}TenantId{q} IS NOT NULL AND u.{q}TenantId{q} <> '')",
            cancellationToken);

        var unstamped = await context.Database
            .SqlQueryRaw<UnstampedAssignment>(
                $"SELECT {q}UserId{q} AS {q}UserId{q}, {q}RoleId{q} AS {q}RoleId{q} FROM {userRoles} WHERE {q}TenantId{q} IS NULL")
            .ToListAsync(cancellationToken);

        if (unstamped.Count > 0)
        {
            var rows = string.Join(", ", unstamped.Select(r => $"(UserId={r.UserId}, RoleId={r.RoleId})"));
            throw new InvalidOperationException(
                $"{unstamped.Count} role assignment(s) have no tenant to apply in: their role or user no longer " +
                $"exists, or the user has no home tenant. Fix or delete them, then rerun: {rows}");
        }

        logger?.LogInformation(
            "Stamped role assignments with their tenant: {TenantOwned} from a tenant-owned role, {PlatformWide} from the user's home tenant",
            tenantOwned, platformWide);
    }

    public override async Task DownAsync(
        DbContext context, IServiceProvider services, CancellationToken cancellationToken = default)
    {
        var q = DataMigrationSqlHelpers.Q(context);
        var userRoles = DataMigrationSqlHelpers.QualifiedTable<OlusoUserRole>(context);
        await ExecuteSqlRawAsync(context, $"UPDATE {userRoles} SET {q}TenantId{q} = NULL", cancellationToken);
    }

    private sealed class UnstampedAssignment
    {
        public string UserId { get; set; } = default!;
        public string RoleId { get; set; } = default!;
    }
}
