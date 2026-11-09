using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Oluso.Core.Data;
using Oluso.Core.Domain.Entities;

namespace Oluso.EntityFramework.DataMigrations;

/// <summary>
/// Seeds TenantMembership rows from existing data:
/// 1. Every user with a non-null TenantId gets a TenantMembership for their home tenant
/// 2. Org members with null AllowedTenantIdsJson get TenantMembership for all tenants in their org
/// 3. Org members with explicit AllowedTenantIdsJson get TenantMembership for each listed tenant
/// Runs after the AddTenantMembership schema migration creates the table.
/// </summary>
[DataMigration(typeof(OlusoDbContext))]
public class SeedTenantMemberships : DataMigrationBase
{
    public override string MigrationId => "20260321_SeedTenantMemberships";

    public override string? AfterSchemaMigration => "AddTenantMembership";

    public override string Description => "Seed TenantMembership rows from existing user TenantId and OrganizationMembership data";

    public override async Task UpAsync(DbContext context, IServiceProvider services, CancellationToken cancellationToken = default)
    {
        var logger = services.GetService<ILoggerFactory>()?.CreateLogger<SeedTenantMemberships>();
        var providerName = context.Database.ProviderName ?? "";

        var q = DataMigrationSqlHelpers.Q(context);
        var tm = DataMigrationSqlHelpers.QualifiedTable<TenantMembership>(context);
        var users = DataMigrationSqlHelpers.QualifiedTable<OlusoUser>(context);
        var orgMem = DataMigrationSqlHelpers.QualifiedTable<OrganizationMembership>(context);
        var tenants = DataMigrationSqlHelpers.QualifiedTable<Tenant>(context);

        // Provider-specific UUID generation
        var newId = GetNewIdExpression(providerName);

        // Step 1: Bulk insert from AspNetUsers — every user's home tenant
        var step1Sql = $@"
            INSERT INTO {tm} ({q}Id{q}, {q}TenantId{q}, {q}UserId{q}, {q}JoinedAt{q}, {q}InvitedByUserId{q})
            SELECT {newId}, u.{q}TenantId{q}, u.{q}Id{q}, {GetUtcNowExpression(providerName)}, 'data-migration'
            FROM {users} u
            WHERE u.{q}TenantId{q} IS NOT NULL AND u.{q}TenantId{q} != ''
              AND NOT EXISTS (
                SELECT 1 FROM {tm} tm
                WHERE tm.{q}TenantId{q} = u.{q}TenantId{q} AND tm.{q}UserId{q} = u.{q}Id{q}
              )";

        await ExecuteSqlRawAsync(context, step1Sql, cancellationToken);
        logger?.LogInformation("Step 1: Seeded TenantMemberships from user home tenants");

        // Step 2: Bulk insert from OrganizationMemberships where AllowedTenantIdsJson IS NULL
        // (null means access to all tenants in the org)
        var step2Sql = $@"
            INSERT INTO {tm} ({q}Id{q}, {q}TenantId{q}, {q}UserId{q}, {q}JoinedAt{q}, {q}InvitedByUserId{q})
            SELECT {newId}, t.{q}Id{q}, om.{q}UserId{q}, {GetUtcNowExpression(providerName)}, 'data-migration'
            FROM {orgMem} om
            INNER JOIN {tenants} t ON t.{q}OrganizationId{q} = om.{q}OrganizationId{q}
            WHERE om.{q}AllowedTenantIdsJson{q} IS NULL
              AND NOT EXISTS (
                SELECT 1 FROM {tm} tm
                WHERE tm.{q}TenantId{q} = t.{q}Id{q} AND tm.{q}UserId{q} = om.{q}UserId{q}
              )";

        await ExecuteSqlRawAsync(context, step2Sql, cancellationToken);
        logger?.LogInformation("Step 2: Seeded TenantMemberships from org memberships (all-tenant access)");

        // Step 3: Process OrganizationMemberships with explicit AllowedTenantIdsJson
        // Requires C# for JSON parsing — no cross-provider JSON array support in SQL
        var membershipsWithJson = await context.Database
            .SqlQueryRaw<OrgMembershipRow>(
                $"SELECT {q}UserId{q}, {q}AllowedTenantIdsJson{q} FROM {orgMem} WHERE {q}AllowedTenantIdsJson{q} IS NOT NULL AND {q}AllowedTenantIdsJson{q} != ''")
            .ToListAsync(cancellationToken);

        var inserted = 0;
        foreach (var membership in membershipsWithJson)
        {
            List<string>? tenantIds;
            try
            {
                tenantIds = JsonSerializer.Deserialize<List<string>>(membership.AllowedTenantIdsJson!);
            }
            catch (JsonException ex)
            {
                logger?.LogWarning(ex, "Failed to parse AllowedTenantIdsJson for user {UserId}, skipping",
                    membership.UserId);
                continue;
            }

            if (tenantIds == null) continue;

            // Identifiers (tm, q) must be baked into the SQL literal, not
            // passed as parameters — providers can't parameterise table or
            // column names. Values (id, tenantId, UserId, JoinedAt,
            // InvitedByUserId) go through ExecuteSqlRaw's {0..N} placeholders.
            var step3Sql =
                $"INSERT INTO {tm} ({q}Id{q}, {q}TenantId{q}, {q}UserId{q}, {q}JoinedAt{q}, {q}InvitedByUserId{q}) " +
                "SELECT {0}, {1}, {2}, {3}, {4} " +
                $"WHERE NOT EXISTS (SELECT 1 FROM {tm} WHERE {q}TenantId{q} = {{1}} AND {q}UserId{q} = {{2}})";

            foreach (var tenantId in tenantIds)
            {
                var id = Guid.NewGuid().ToString();
                await context.Database.ExecuteSqlRawAsync(
                    step3Sql,
                    new object[] { id, tenantId, membership.UserId, DateTime.UtcNow, "data-migration" },
                    cancellationToken);
                inserted++;
            }
        }

        logger?.LogInformation("Step 3: Seeded {Count} TenantMemberships from explicit AllowedTenantIdsJson", inserted);
    }

    public override async Task DownAsync(DbContext context, IServiceProvider services, CancellationToken cancellationToken = default)
    {
        var q = DataMigrationSqlHelpers.Q(context);
        var tm = DataMigrationSqlHelpers.QualifiedTable<TenantMembership>(context);
        await ExecuteSqlRawAsync(context,
            $"DELETE FROM {tm} WHERE {q}InvitedByUserId{q} = 'data-migration'",
            cancellationToken);
    }

    private static string GetNewIdExpression(string providerName)
    {
        if (providerName.Contains("SqlServer", StringComparison.OrdinalIgnoreCase))
            return "LOWER(NEWID())";
        if (providerName.Contains("Npgsql", StringComparison.OrdinalIgnoreCase) ||
            providerName.Contains("PostgreSQL", StringComparison.OrdinalIgnoreCase))
            return "gen_random_uuid()::text";
        // SQLite
        return "lower(hex(randomblob(4))) || '-' || lower(hex(randomblob(2))) || '-' || lower(hex(randomblob(2))) || '-' || lower(hex(randomblob(2))) || '-' || lower(hex(randomblob(6)))";
    }

    private static string GetUtcNowExpression(string providerName)
    {
        if (providerName.Contains("SqlServer", StringComparison.OrdinalIgnoreCase))
            return "GETUTCDATE()";
        if (providerName.Contains("Npgsql", StringComparison.OrdinalIgnoreCase) ||
            providerName.Contains("PostgreSQL", StringComparison.OrdinalIgnoreCase))
            return "NOW() AT TIME ZONE 'UTC'";
        // SQLite
        return "datetime('now')";
    }

    private class OrgMembershipRow
    {
        public string UserId { get; set; } = null!;
        public string? AllowedTenantIdsJson { get; set; }
    }
}
