using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Oluso.Core.Data;
using Oluso.Core.Domain.Entities;

namespace Oluso.EntityFramework.DataMigrations;

/// <summary>
/// Second pass of the home-tenant fill first done by <see cref="SeedTenantMemberships"/>.
/// <para>
/// That migration ran once, right after the TenantMembership table was added.
/// Every user created since — SSO/JIT provisioning above all, plus SCIM, LDAP,
/// self-registration and the journey handlers — got no row, because none of
/// those paths created one. So the table drifted back to being partial and
/// readers had to keep unioning <c>user.TenantId</c> back in.
/// </para>
/// <para>
/// <c>TenantMembershipInterceptor</c> now creates the row at SaveChanges, so
/// this closes the historical gap rather than a recurring one. Home tenants only:
/// organization-derived access is maintained through TenantMembership directly,
/// and re-deriving it from AllowedTenantIdsJson would over-grant.
/// </para>
/// </summary>
[DataMigration(typeof(OlusoDbContext))]
public class BackfillHomeTenantMemberships : DataMigrationBase
{
    public override string MigrationId => "20260807_BackfillHomeTenantMemberships";

    public override string Description =>
        "Create TenantMembership rows for users whose home tenant has none";

    public override async Task UpAsync(
        DbContext context, IServiceProvider services, CancellationToken cancellationToken = default)
    {
        var logger = services.GetService<ILoggerFactory>()?.CreateLogger<BackfillHomeTenantMemberships>();
        var providerName = context.Database.ProviderName ?? "";

        var q = DataMigrationSqlHelpers.Q(context);
        var tm = DataMigrationSqlHelpers.QualifiedTable<TenantMembership>(context);
        var users = DataMigrationSqlHelpers.QualifiedTable<OlusoUser>(context);

        var sql = $@"
            INSERT INTO {tm} ({q}Id{q}, {q}TenantId{q}, {q}UserId{q}, {q}JoinedAt{q}, {q}InvitedByUserId{q})
            SELECT {NewId(providerName)}, u.{q}TenantId{q}, u.{q}Id{q}, {UtcNow(providerName)}, 'backfill-home-tenant'
            FROM {users} u
            WHERE u.{q}TenantId{q} IS NOT NULL AND u.{q}TenantId{q} != ''
              AND NOT EXISTS (
                SELECT 1 FROM {tm} tm
                WHERE tm.{q}TenantId{q} = u.{q}TenantId{q} AND tm.{q}UserId{q} = u.{q}Id{q}
              )";

        await ExecuteSqlRawAsync(context, sql, cancellationToken);
        logger?.LogInformation("Backfilled TenantMembership rows for users missing their home tenant");
    }

    public override async Task DownAsync(
        DbContext context, IServiceProvider services, CancellationToken cancellationToken = default)
    {
        var q = DataMigrationSqlHelpers.Q(context);
        var tm = DataMigrationSqlHelpers.QualifiedTable<TenantMembership>(context);

        await ExecuteSqlRawAsync(context,
            $"DELETE FROM {tm} WHERE {q}InvitedByUserId{q} = 'backfill-home-tenant'",
            cancellationToken);
    }

    private static string NewId(string providerName)
    {
        if (providerName.Contains("SqlServer", StringComparison.OrdinalIgnoreCase))
            return "LOWER(NEWID())";
        if (providerName.Contains("Npgsql", StringComparison.OrdinalIgnoreCase) ||
            providerName.Contains("PostgreSQL", StringComparison.OrdinalIgnoreCase))
            return "gen_random_uuid()::text";
        return "lower(hex(randomblob(4))) || '-' || lower(hex(randomblob(2))) || '-' || lower(hex(randomblob(2))) || '-' || lower(hex(randomblob(2))) || '-' || lower(hex(randomblob(6)))";
    }

    private static string UtcNow(string providerName)
    {
        if (providerName.Contains("SqlServer", StringComparison.OrdinalIgnoreCase))
            return "GETUTCDATE()";
        if (providerName.Contains("Npgsql", StringComparison.OrdinalIgnoreCase) ||
            providerName.Contains("PostgreSQL", StringComparison.OrdinalIgnoreCase))
            return "NOW() AT TIME ZONE 'UTC'";
        return "datetime('now')";
    }
}
