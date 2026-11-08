using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.DependencyInjection;
using Oluso.Core.Data;
using Oluso.Core.Domain.Entities;

namespace Oluso.EntityFramework.DataMigrations;

/// <summary>
/// Migrates existing Tenant.Configuration JSON data into the new TenantConfigurationSections table.
/// Each top-level key in the JSON becomes a separate row.
/// Runs after the schema migration that creates the TenantConfigurationSections table.
/// </summary>
[DataMigration(typeof(OlusoDbContext))]
public class MigrateTenantConfigurationToSections : DataMigrationBase
{
    public override string MigrationId => "20260315_MigrateTenantConfigurationToSections";

    public override string? AfterSchemaMigration => "AddTenantConfigurationSections";

    public override string Description => "Migrate Tenant.Configuration JSON sections into TenantConfigurationSections table rows";

    public override async Task UpAsync(DbContext context, IServiceProvider services, CancellationToken cancellationToken = default)
    {
        var logger = services.GetService<ILoggerFactory>()?.CreateLogger<MigrateTenantConfigurationToSections>();

        // Tenant.Configuration is a legacy column that was split into the
        // TenantConfigurationSections table and subsequently removed from the
        // Tenant entity. Fresh deploys never had the column, so if the current
        // model doesn't know about it we skip — there's nothing to migrate.
        if (context.Model.FindEntityType(typeof(Tenant))?.FindProperty("Configuration") is null)
        {
            logger?.LogInformation("Tenant.Configuration no longer exists in the model — skipping legacy data migration");
            return;
        }

        var q = DataMigrationSqlHelpers.Q(context);
        var tenants = DataMigrationSqlHelpers.QualifiedTable<Tenant>(context);
        var sectionsTable = DataMigrationSqlHelpers.QualifiedTable<TenantConfigurationSection>(context);

        // Read all tenants that have configuration data
        var tenantRows = await context.Database
            .SqlQueryRaw<TenantConfigRow>($"SELECT {q}Id{q}, {q}Configuration{q} FROM {tenants} WHERE {q}Configuration{q} IS NOT NULL AND {q}Configuration{q} != ''")
            .ToListAsync(cancellationToken);

        logger?.LogInformation("Migrating configuration for {Count} tenants to TenantConfigurationSections", tenantRows.Count);

        var now = DateTime.UtcNow;
        var migrated = 0;

        // Identifiers (table, column names) are baked into the SQL literal;
        // only values go through ExecuteSqlRaw's {0..N} placeholders.
        var insertSql =
            $"INSERT INTO {sectionsTable} ({q}TenantId{q}, {q}SectionKey{q}, {q}Value{q}, {q}IsEncrypted{q}, {q}Created{q}, {q}Updated{q}) " +
            "VALUES ({0}, {1}, {2}, {3}, {4}, {5})";

        foreach (var tenant in tenantRows)
        {
            if (string.IsNullOrWhiteSpace(tenant.Configuration))
                continue;

            Dictionary<string, JsonElement>? sections;
            try
            {
                sections = JsonSerializer.Deserialize<Dictionary<string, JsonElement>>(tenant.Configuration);
            }
            catch (JsonException ex)
            {
                logger?.LogWarning(ex, "Failed to parse Configuration JSON for tenant {TenantId}, skipping", tenant.Id);
                continue;
            }

            if (sections == null || sections.Count == 0)
                continue;

            foreach (var (sectionKey, sectionValue) in sections)
            {
                var json = sectionValue.GetRawText();

                await context.Database.ExecuteSqlRawAsync(
                    insertSql,
                    new object[] { tenant.Id, sectionKey, json, false, now, now },
                    cancellationToken);

                migrated++;
            }
        }

        logger?.LogInformation("Migrated {Count} configuration sections to TenantConfigurationSections", migrated);
    }

    public override async Task DownAsync(DbContext context, IServiceProvider services, CancellationToken cancellationToken = default)
    {
        // Remove all rows — the original Configuration column still has the data
        var sectionsTable = DataMigrationSqlHelpers.QualifiedTable<TenantConfigurationSection>(context);
        await ExecuteSqlRawAsync(context, $"DELETE FROM {sectionsTable}", cancellationToken);
    }

    // Projection type for the raw SQL query
    private class TenantConfigRow
    {
        public string Id { get; set; } = null!;
        public string? Configuration { get; set; }
    }
}
