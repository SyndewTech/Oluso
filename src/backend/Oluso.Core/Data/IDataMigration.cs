using Microsoft.EntityFrameworkCore;

namespace Oluso.Core.Data;

/// <summary>
/// Represents a data migration that runs after a specific schema migration.
/// Data migrations can perform complex operations like:
/// - Populating new columns with computed data from existing columns
/// - Splitting/merging tables with data transformation
/// - Loading seed data from external files (JSON, CSV)
/// - Creating lookup table data
/// - Any operation requiring C# logic beyond raw SQL
/// </summary>
/// <remarks>
/// <para>
/// Data migrations are tracked in a separate history table (__DataMigrationHistory)
/// and run exactly once per migration ID.
/// </para>
/// <para>
/// <b>Ordering Options:</b>
/// 1. <c>AfterSchemaMigration</c> - Exact match: runs immediately after that schema migration
/// 2. <c>RunAt</c> - Timestamp-based: runs after schema migrations up to that timestamp
/// 3. Both null - Runs after all schema migrations complete
/// </para>
/// <para>
/// <b>Execution Order (True Interleaving):</b>
/// For each pending schema migration (e.g., "20240115143022_AddDisplayName"):
/// 1. Apply the schema migration
/// 2. Run data migrations where AfterSchemaMigration matches exactly
/// 3. Run data migrations where RunAt &lt;= "20240115143022" (timestamp portion)
/// After all schema migrations complete:
/// 4. Run remaining data migrations (AfterSchemaMigration=null and RunAt=null)
/// </para>
/// <para>
/// <b>Why Interleaving Matters:</b>
/// If schema A adds a column and schema B removes it, a data migration for A
/// must run immediately after A (before B removes the column).
/// </para>
/// <para>
/// <b>Example Timeline:</b>
/// - Schema: 20240101000000_Initial → Applied
///   - Data: SetupBaseData (RunAt="20240101000000") → Runs
/// - Schema: 20240115143022_AddDisplayName → Applied
///   - Data: PopulateDisplayName (AfterSchemaMigration="20240115143022_AddDisplayName") → Runs
///   - Data: MoreSetup (RunAt="20240115000000") → Runs (timestamp &lt;= schema)
/// - Schema: 20240201000000_RemoveOldColumn → Applied
/// - Final data migrations (both null) → Runs at end
/// </para>
/// </remarks>
public interface IDataMigration
{
    /// <summary>
    /// Unique identifier for this data migration.
    /// Convention: Use the same name as the schema migration it's tied to,
    /// or use format "{MigrationName}_{Description}" for multiple data migrations per schema migration.
    /// Example: "20240115_AddUserDisplayName" or "20240115_AddUserDisplayName_PopulateFromFirstLast"
    /// </summary>
    string MigrationId { get; }

    /// <summary>
    /// The schema migration this data migration should run after.
    /// If null, uses RunAt for ordering (or runs after all schema migrations if RunAt is also null).
    /// Example: "20240115143022_AddUserDisplayName"
    /// </summary>
    string? AfterSchemaMigration { get; }

    /// <summary>
    /// Timestamp for ordering data migrations. Format: "yyyyMMddHHmmss" (matches EF Core migration naming).
    /// When a schema migration is applied, all pending data migrations with RunAt &lt;= schema migration timestamp run.
    /// This allows data migrations without explicit schema dependencies to run at the right time.
    /// Example: "20240115143022" means this runs after schema migrations up to that timestamp.
    /// If null, uses AfterSchemaMigration for ordering, or runs at the end if both are null.
    /// </summary>
    string? RunAt => null;

    /// <summary>
    /// Order for data migrations that run at the same time (same RunAt or same AfterSchemaMigration).
    /// Lower values run first. Default is 0.
    /// </summary>
    int Order => 0;

    /// <summary>
    /// Description of what this data migration does (for logging).
    /// </summary>
    string Description { get; }

    /// <summary>
    /// Executes the data migration.
    /// This method receives the DbContext and a service provider for accessing
    /// other services (file system, configuration, etc.).
    /// </summary>
    /// <param name="context">The database context.</param>
    /// <param name="services">Service provider for accessing additional services.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    Task UpAsync(DbContext context, IServiceProvider services, CancellationToken cancellationToken = default);

    /// <summary>
    /// Reverts the data migration (optional).
    /// Called when rolling back migrations.
    /// </summary>
    /// <param name="context">The database context.</param>
    /// <param name="services">Service provider for accessing additional services.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    Task DownAsync(DbContext context, IServiceProvider services, CancellationToken cancellationToken = default) => Task.CompletedTask;
}

/// <summary>
/// Base class for data migrations with common functionality.
/// </summary>
public abstract class DataMigrationBase : IDataMigration
{
    public abstract string MigrationId { get; }
    public virtual string? AfterSchemaMigration => null;
    public virtual string? RunAt => null;
    public virtual int Order => 0;
    public abstract string Description { get; }

    public abstract Task UpAsync(DbContext context, IServiceProvider services, CancellationToken cancellationToken = default);

    public virtual Task DownAsync(DbContext context, IServiceProvider services, CancellationToken cancellationToken = default)
        => Task.CompletedTask;

    /// <summary>
    /// Helper to execute raw SQL with parameters.
    /// </summary>
    protected static async Task ExecuteSqlAsync(DbContext context, FormattableString sql, CancellationToken cancellationToken = default)
    {
        await context.Database.ExecuteSqlInterpolatedAsync(sql, cancellationToken);
    }

    /// <summary>
    /// Helper to execute raw SQL.
    /// </summary>
    protected static async Task ExecuteSqlRawAsync(DbContext context, string sql, CancellationToken cancellationToken = default)
    {
        await context.Database.ExecuteSqlRawAsync(sql, cancellationToken);
    }
}

/// <summary>
/// Attribute to mark a class as a data migration for automatic discovery.
/// </summary>
[AttributeUsage(AttributeTargets.Class, AllowMultiple = false, Inherited = false)]
public class DataMigrationAttribute : Attribute
{
    /// <summary>
    /// The DbContext type this data migration applies to.
    /// </summary>
    public Type ContextType { get; }

    public DataMigrationAttribute(Type contextType)
    {
        ContextType = contextType;
    }
}
