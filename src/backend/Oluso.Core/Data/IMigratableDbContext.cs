using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;

namespace Oluso.Core.Data;

/// <summary>
/// Marker interface for DbContexts that support automatic migration discovery.
/// Implement this interface on your DbContext to have it automatically discovered
/// and migrated when calling MigrateOlusoDatabaseAsync().
/// </summary>
public interface IMigratableDbContext
{
    /// <summary>
    /// Gets the database facade for this context.
    /// </summary>
    DatabaseFacade Database { get; }

    /// <summary>
    /// A friendly name for this DbContext used in logging.
    /// </summary>
    string MigrationName { get; }

    /// <summary>
    /// Order in which migrations should be applied.
    /// Lower values are migrated first. Core OlusoDbContext uses 0.
    /// Plugin contexts should use higher values (e.g., 100, 200).
    /// </summary>
    int MigrationOrder { get; }
}

/// <summary>
/// Interface for DbContexts that support automatic data seeding after migrations.
/// Implement this interface on your DbContext to have seed data automatically
/// applied after migrations complete.
/// </summary>
public interface ISeedableDbContext
{
    /// <summary>
    /// A friendly name for this DbContext used in logging.
    /// </summary>
    string SeedName { get; }

    /// <summary>
    /// Seeds initial data into the database.
    /// This method is called after migrations are applied.
    /// Implementations should be idempotent (safe to call multiple times).
    /// </summary>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>A task representing the seeding operation.</returns>
    Task SeedAsync(CancellationToken cancellationToken = default);
}
