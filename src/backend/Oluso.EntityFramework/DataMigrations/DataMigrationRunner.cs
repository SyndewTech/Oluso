using System.Reflection;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Oluso.Core.Data;

namespace Oluso.EntityFramework.DataMigrations;

/// <summary>
/// Discovers and runs data migrations for a DbContext.
/// </summary>
public class DataMigrationRunner
{
    private readonly IServiceProvider _services;
    private readonly ILogger<DataMigrationRunner>? _logger;

    public DataMigrationRunner(IServiceProvider services, ILogger<DataMigrationRunner>? logger = null)
    {
        _services = services;
        _logger = logger;
    }

    /// <summary>
    /// Runs all pending data migrations for the specified DbContext.
    /// Data migrations only run if their associated schema migration has been applied.
    /// </summary>
    /// <typeparam name="TContext">The DbContext type.</typeparam>
    /// <param name="context">The DbContext instance.</param>
    /// <param name="appliedSchemaMigrations">List of schema migrations that have been applied. If null, queries the database.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    public async Task RunAsync<TContext>(
        TContext context,
        IEnumerable<string>? appliedSchemaMigrations = null,
        CancellationToken cancellationToken = default)
        where TContext : DbContext
    {
        var contextType = typeof(TContext);
        var contextTypeName = contextType.FullName ?? contextType.Name;

        // Ensure the history table exists
        await EnsureHistoryTableAsync(context, cancellationToken);

        // Get already applied data migrations
        var appliedDataMigrations = await GetAppliedMigrationsAsync(context, contextTypeName, cancellationToken);

        // Get applied schema migrations (to know which data migrations are eligible to run)
        var appliedSchema = appliedSchemaMigrations?.ToHashSet()
            ?? (await context.Database.GetAppliedMigrationsAsync(cancellationToken)).ToHashSet();

        // Discover all data migrations for this context
        var allMigrations = DiscoverDataMigrations(contextType);

        // Filter to pending migrations where:
        // 1. Data migration hasn't been applied yet
        // 2. Either AfterSchemaMigration is null (run after all), or the schema migration has been applied
        var pendingMigrations = allMigrations
            .Where(m => !appliedDataMigrations.Contains(m.MigrationId))
            .Where(m => m.AfterSchemaMigration == null || appliedSchema.Contains(m.AfterSchemaMigration))
            .OrderBy(m => GetSchemaMigrationIndex(m.AfterSchemaMigration, appliedSchema))
            .ThenBy(m => m.Order)
            .ToList();

        if (pendingMigrations.Count == 0)
        {
            _logger?.LogDebug("{ContextType}: No pending data migrations", contextTypeName);
            return;
        }

        _logger?.LogInformation("{ContextType}: Running {Count} pending data migration(s)",
            contextTypeName, pendingMigrations.Count);

        foreach (var migration in pendingMigrations)
        {
            await RunMigrationAsync(context, migration, contextTypeName, cancellationToken);
        }
    }

    /// <summary>
    /// Gets the index of a schema migration for ordering.
    /// Returns the position in the applied migrations list, or int.MaxValue for null (run last).
    /// </summary>
    private static int GetSchemaMigrationIndex(string? schemaMigration, HashSet<string> appliedSchema)
    {
        if (schemaMigration == null)
            return int.MaxValue; // null = run after all schema migrations

        // Find the index by sorting and finding position
        var sortedMigrations = appliedSchema.OrderBy(m => m).ToList();
        var index = sortedMigrations.IndexOf(schemaMigration);
        return index >= 0 ? index : int.MaxValue;
    }

    /// <summary>
    /// Runs data migrations for all IMigratableDbContext instances.
    /// </summary>
    public async Task RunAllAsync(CancellationToken cancellationToken = default)
    {
        // Get all migratable contexts
        var migratableContexts = _services.GetServices<IMigratableDbContext>().ToList();

        foreach (var migratable in migratableContexts)
        {
            if (migratable is DbContext context)
            {
                var contextType = context.GetType();
                var method = typeof(DataMigrationRunner)
                    .GetMethod(nameof(RunAsync))!
                    .MakeGenericMethod(contextType);

                await (Task)method.Invoke(this, new object?[] { context, null, cancellationToken })!;
            }
        }
    }

    /// <summary>
    /// Runs data migrations that are tied to a specific schema migration.
    /// Called immediately after that schema migration is applied, enabling true interleaving.
    ///
    /// This runs:
    /// 1. Data migrations where AfterSchemaMigration matches exactly
    /// 2. Data migrations where RunAt &lt;= schema migration timestamp
    /// </summary>
    /// <param name="context">The DbContext instance.</param>
    /// <param name="schemaMigrationName">The name of the schema migration that was just applied (e.g., "20240115143022_AddDisplayName").</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    public async Task RunForSchemaMigrationAsync(
        DbContext context,
        string schemaMigrationName,
        CancellationToken cancellationToken = default)
    {
        var contextType = context.GetType();
        var contextTypeName = contextType.FullName ?? contextType.Name;

        // Ensure the history table exists
        await EnsureHistoryTableAsync(context, cancellationToken);

        // Get already applied data migrations
        var appliedDataMigrations = await GetAppliedMigrationsAsync(context, contextTypeName, cancellationToken);

        // Discover all data migrations for this context
        var allMigrations = DiscoverDataMigrations(contextType);

        // Extract timestamp from schema migration name (e.g., "20240115143022" from "20240115143022_AddDisplayName")
        var schemaTimestamp = ExtractTimestamp(schemaMigrationName);

        // Filter to pending migrations that should run after this schema migration:
        // 1. AfterSchemaMigration matches exactly, OR
        // 2. RunAt is set and <= schema timestamp (and AfterSchemaMigration is null)
        var pendingMigrations = allMigrations
            .Where(m => !appliedDataMigrations.Contains(m.MigrationId))
            .Where(m => ShouldRunAfterSchema(m, schemaMigrationName, schemaTimestamp))
            .OrderBy(m => m.RunAt ?? "9999999999999999") // RunAt first, then AfterSchemaMigration
            .ThenBy(m => m.Order)
            .ToList();

        if (pendingMigrations.Count == 0)
        {
            _logger?.LogDebug("{ContextType}: No data migrations for schema migration '{SchemaMigration}'",
                contextTypeName, schemaMigrationName);
            return;
        }

        _logger?.LogInformation("{ContextType}: Running {Count} data migration(s) for schema migration '{SchemaMigration}'",
            contextTypeName, pendingMigrations.Count, schemaMigrationName);

        foreach (var migration in pendingMigrations)
        {
            await RunMigrationAsync(context, migration, contextTypeName, cancellationToken);
        }
    }

    /// <summary>
    /// Determines if a data migration should run after the given schema migration.
    /// </summary>
    private static bool ShouldRunAfterSchema(IDataMigration migration, string schemaMigrationName, string? schemaTimestamp)
    {
        // Case 1: Exact AfterSchemaMigration match
        if (migration.AfterSchemaMigration == schemaMigrationName)
        {
            return true;
        }

        // Case 2: RunAt-based ordering (only if AfterSchemaMigration is not set)
        if (migration.AfterSchemaMigration == null && migration.RunAt != null && schemaTimestamp != null)
        {
            // Compare timestamps lexicographically (works for yyyyMMddHHmmss format)
            return string.CompareOrdinal(migration.RunAt, schemaTimestamp) <= 0;
        }

        return false;
    }

    /// <summary>
    /// Extracts the timestamp portion from a schema migration name.
    /// E.g., "20240115143022_AddDisplayName" -> "20240115143022"
    /// </summary>
    private static string? ExtractTimestamp(string migrationName)
    {
        // EF Core migration names start with a timestamp: yyyyMMddHHmmss_Description
        var underscoreIndex = migrationName.IndexOf('_');
        if (underscoreIndex > 0)
        {
            var timestamp = migrationName[..underscoreIndex];
            // Validate it looks like a timestamp (all digits, 14 chars)
            if (timestamp.Length >= 14 && timestamp.All(char.IsDigit))
            {
                return timestamp;
            }
        }

        // Fallback: if no underscore, check if the whole name is a timestamp
        if (migrationName.Length >= 14 && migrationName.Take(14).All(char.IsDigit))
        {
            return migrationName[..14];
        }

        return null;
    }

    /// <summary>
    /// Runs data migrations that have both AfterSchemaMigration = null and RunAt = null.
    /// These are "final" migrations that run after all schema migrations complete.
    /// Called at the end of the migration process.
    /// </summary>
    /// <param name="context">The DbContext instance.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    public async Task RunFinalDataMigrationsAsync(
        DbContext context,
        CancellationToken cancellationToken = default)
    {
        var contextType = context.GetType();
        var contextTypeName = contextType.FullName ?? contextType.Name;

        // Ensure the history table exists
        await EnsureHistoryTableAsync(context, cancellationToken);

        // Get already applied data migrations
        var appliedDataMigrations = await GetAppliedMigrationsAsync(context, contextTypeName, cancellationToken);

        // Discover all data migrations for this context
        var allMigrations = DiscoverDataMigrations(contextType);

        // Filter to pending migrations with both AfterSchemaMigration = null AND RunAt = null
        // (RunAt-based migrations should have been run during schema migration interleaving)
        var pendingMigrations = allMigrations
            .Where(m => !appliedDataMigrations.Contains(m.MigrationId))
            .Where(m => m.AfterSchemaMigration == null && m.RunAt == null)
            .OrderBy(m => m.Order)
            .ToList();

        if (pendingMigrations.Count == 0)
        {
            _logger?.LogDebug("{ContextType}: No final data migrations to run", contextTypeName);
            return;
        }

        _logger?.LogInformation("{ContextType}: Running {Count} final data migration(s)",
            contextTypeName, pendingMigrations.Count);

        foreach (var migration in pendingMigrations)
        {
            await RunMigrationAsync(context, migration, contextTypeName, cancellationToken);
        }
    }

    private async Task RunMigrationAsync<TContext>(
        TContext context,
        IDataMigration migration,
        string contextTypeName,
        CancellationToken cancellationToken)
        where TContext : DbContext
    {
        _logger?.LogInformation("{ContextType}: Running data migration '{MigrationId}' - {Description}",
            contextTypeName, migration.MigrationId, migration.Description);

        try
        {
            await migration.UpAsync(context, _services, cancellationToken);

            // Record that the migration was applied
            await RecordMigrationAsync(context, migration, contextTypeName, cancellationToken);

            _logger?.LogInformation("{ContextType}: Data migration '{MigrationId}' completed successfully",
                contextTypeName, migration.MigrationId);
        }
        catch (Exception ex)
        {
            _logger?.LogError(ex, "{ContextType}: Data migration '{MigrationId}' failed",
                contextTypeName, migration.MigrationId);
            throw;
        }
    }

    private static List<IDataMigration> DiscoverDataMigrations(Type contextType)
    {
        var migrations = new List<IDataMigration>();

        // Scan the context's assembly and referenced assemblies
        var assemblies = new HashSet<Assembly> { contextType.Assembly };

        // Also check entry assembly
        var entryAssembly = Assembly.GetEntryAssembly();
        if (entryAssembly != null)
        {
            assemblies.Add(entryAssembly);
        }

        foreach (var assembly in assemblies)
        {
            try
            {
                var migrationTypes = assembly.GetTypes()
                    .Where(t => !t.IsAbstract && typeof(IDataMigration).IsAssignableFrom(t))
                    .Where(t =>
                    {
                        var attr = t.GetCustomAttribute<DataMigrationAttribute>();
                        return attr == null || attr.ContextType == contextType || attr.ContextType.IsAssignableFrom(contextType);
                    });

                foreach (var type in migrationTypes)
                {
                    try
                    {
                        if (Activator.CreateInstance(type) is IDataMigration migration)
                        {
                            migrations.Add(migration);
                        }
                    }
                    catch
                    {
                        // Skip types that can't be instantiated
                    }
                }
            }
            catch
            {
                // Skip assemblies that can't be scanned
            }
        }

        return migrations;
    }

    private static async Task EnsureHistoryTableAsync<TContext>(TContext context, CancellationToken cancellationToken)
        where TContext : DbContext
    {
        var tableName = GetHistoryTableName(context);
        var providerName = context.Database.ProviderName ?? "";

        string createSql;

        if (providerName.Contains("Sqlite", StringComparison.OrdinalIgnoreCase))
        {
            createSql = $@"
                CREATE TABLE IF NOT EXISTS ""{tableName}"" (
                    ""MigrationId"" TEXT NOT NULL,
                    ""ContextType"" TEXT NOT NULL,
                    ""AppliedAt"" TEXT NOT NULL,
                    ""Description"" TEXT,
                    PRIMARY KEY (""MigrationId"", ""ContextType"")
                )";
        }
        else if (providerName.Contains("SqlServer", StringComparison.OrdinalIgnoreCase))
        {
            createSql = $@"
                IF NOT EXISTS (SELECT * FROM sys.tables WHERE name = '{tableName}')
                CREATE TABLE [{tableName}] (
                    [MigrationId] NVARCHAR(256) NOT NULL,
                    [ContextType] NVARCHAR(256) NOT NULL,
                    [AppliedAt] DATETIME2 NOT NULL,
                    [Description] NVARCHAR(1024),
                    PRIMARY KEY ([MigrationId], [ContextType])
                )";
        }
        else if (providerName.Contains("Npgsql", StringComparison.OrdinalIgnoreCase) ||
                 providerName.Contains("PostgreSQL", StringComparison.OrdinalIgnoreCase))
        {
            createSql = $@"
                CREATE TABLE IF NOT EXISTS ""{tableName}"" (
                    ""MigrationId"" VARCHAR(256) NOT NULL,
                    ""ContextType"" VARCHAR(256) NOT NULL,
                    ""AppliedAt"" TIMESTAMP NOT NULL,
                    ""Description"" VARCHAR(1024),
                    PRIMARY KEY (""MigrationId"", ""ContextType"")
                )";
        }
        else
        {
            throw new NotSupportedException($"Database provider '{providerName}' is not supported for data migrations.");
        }

        await context.Database.ExecuteSqlRawAsync(createSql, cancellationToken);
    }

    private static async Task<HashSet<string>> GetAppliedMigrationsAsync<TContext>(
        TContext context,
        string contextTypeName,
        CancellationToken cancellationToken)
        where TContext : DbContext
    {
        var tableName = GetHistoryTableName(context);
        var applied = new HashSet<string>();

        try
        {
            var connection = context.Database.GetDbConnection();
            await connection.OpenAsync(cancellationToken);

            using var command = connection.CreateCommand();
            command.CommandText = $"SELECT MigrationId FROM \"{tableName}\" WHERE ContextType = @contextType";

            var param = command.CreateParameter();
            param.ParameterName = "@contextType";
            param.Value = contextTypeName;
            command.Parameters.Add(param);

            using var reader = await command.ExecuteReaderAsync(cancellationToken);
            while (await reader.ReadAsync(cancellationToken))
            {
                applied.Add(reader.GetString(0));
            }
        }
        catch
        {
            // Table might not exist yet, return empty set
        }

        return applied;
    }

    private static async Task RecordMigrationAsync<TContext>(
        TContext context,
        IDataMigration migration,
        string contextTypeName,
        CancellationToken cancellationToken)
        where TContext : DbContext
    {
        var tableName = GetHistoryTableName(context);
        var providerName = context.Database.ProviderName ?? "";

        string insertSql;
        if (providerName.Contains("SqlServer", StringComparison.OrdinalIgnoreCase))
        {
            insertSql = $@"
                INSERT INTO [{tableName}] ([MigrationId], [ContextType], [AppliedAt], [Description])
                VALUES (@migrationId, @contextType, @appliedAt, @description)";
        }
        else
        {
            insertSql = $@"
                INSERT INTO ""{tableName}"" (""MigrationId"", ""ContextType"", ""AppliedAt"", ""Description"")
                VALUES (@migrationId, @contextType, @appliedAt, @description)";
        }

        var connection = context.Database.GetDbConnection();
        using var command = connection.CreateCommand();
        command.CommandText = insertSql;

        var p1 = command.CreateParameter();
        p1.ParameterName = "@migrationId";
        p1.Value = migration.MigrationId;
        command.Parameters.Add(p1);

        var p2 = command.CreateParameter();
        p2.ParameterName = "@contextType";
        p2.Value = contextTypeName;
        command.Parameters.Add(p2);

        var p3 = command.CreateParameter();
        p3.ParameterName = "@appliedAt";
        p3.Value = DateTime.UtcNow;
        command.Parameters.Add(p3);

        var p4 = command.CreateParameter();
        p4.ParameterName = "@description";
        p4.Value = migration.Description;
        command.Parameters.Add(p4);

        await command.ExecuteNonQueryAsync(cancellationToken);
    }

    private static string GetHistoryTableName<TContext>(TContext context) where TContext : DbContext
    {
        // Check if context implements IMigratableDbContext with a plugin name
        if (context is IMigratableDbContext migratable)
        {
            var migrationName = migratable.MigrationName;
            // If it's not the core OlusoDbContext, use the plugin name
            if (!string.IsNullOrEmpty(migrationName) && migrationName != "Oluso")
            {
                return $"__DataMigrationHistory_{migrationName}";
            }
        }

        return "__DataMigrationHistory";
    }
}
