using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Oluso.Core.Data;
using Oluso.EntityFramework.DataMigrations;

namespace Oluso.EntityFramework;

/// <summary>
/// Extension methods for applying Oluso database migrations.
/// </summary>
public static class MigrationExtensions
{
    /// <summary>
    /// Applies all pending migrations for Oluso and any registered plugin DbContexts,
    /// with true interleaved data migrations and seed data.
    ///
    /// Order of operations:
    /// 1. For each schema migration (in pending order):
    ///    a. Apply the schema migration
    ///    b. Run data migrations tied to that schema migration (via AfterSchemaMigration)
    /// 2. Final data migrations (AfterSchemaMigration=null) - run after all schema migrations
    /// 3. Seed data (ISeedableDbContext) - idempotent seeding
    ///
    /// This interleaved approach ensures that data migrations can safely reference columns
    /// that might be removed in subsequent schema migrations.
    /// </summary>
    /// <param name="host">The application host.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>A task representing the migration operation.</returns>
    /// <example>
    /// <code>
    /// var app = builder.Build();
    /// await app.MigrateOlusoDatabaseAsync();
    /// app.Run();
    /// </code>
    /// </example>
    public static async Task MigrateOlusoDatabaseAsync(
        this IHost host,
        CancellationToken cancellationToken = default)
    {
        using var scope = host.Services.CreateScope();
        var logger = scope.ServiceProvider.GetService<ILogger<OlusoDbContext>>();

        // Discover all registered IMigratableDbContext implementations
        var migratableContexts = DiscoverMigratableContexts(scope.ServiceProvider, logger);

        if (migratableContexts.Count == 0)
        {
            logger?.LogWarning("No migratable DbContexts found. Ensure OlusoDbContext or plugin contexts are registered.");
            return;
        }

        // Sort by MigrationOrder and apply schema migrations with interleaved data migrations
        foreach (var context in migratableContexts.OrderBy(c => c.MigrationOrder))
        {
            await MigrateContextAsync(context, scope.ServiceProvider, logger, cancellationToken);
        }

        // Run final data migrations (those with AfterSchemaMigration = null)
        await RunFinalDataMigrationsAsync(scope.ServiceProvider, logger, cancellationToken);

        // Run seeding for any contexts that implement ISeedableDbContext
        await SeedAllContextsAsync(scope.ServiceProvider, logger, cancellationToken);
    }

    /// <summary>
    /// Applies all pending migrations for a specific DbContext type.
    /// </summary>
    /// <typeparam name="TContext">The DbContext type to migrate.</typeparam>
    /// <param name="host">The application host.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    public static async Task MigrateOlusoDatabaseAsync<TContext>(
        this IHost host,
        CancellationToken cancellationToken = default)
        where TContext : DbContext, IMigratableDbContext
    {
        using var scope = host.Services.CreateScope();
        var logger = scope.ServiceProvider.GetService<ILogger<TContext>>();
        var context = scope.ServiceProvider.GetService<TContext>();

        if (context == null)
        {
            logger?.LogWarning("DbContext {ContextType} is not registered", typeof(TContext).Name);
            return;
        }

        await MigrateContextAsync(context, scope.ServiceProvider, logger, cancellationToken);
    }

    /// <summary>
    /// Applies migrations using a service provider (for use in DI scenarios),
    /// then runs data migrations and seed data.
    /// </summary>
    /// <param name="serviceProvider">The service provider.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    public static async Task MigrateOlusoDatabaseAsync(
        this IServiceProvider serviceProvider,
        CancellationToken cancellationToken = default)
    {
        var logger = serviceProvider.GetService<ILogger<OlusoDbContext>>();

        var migratableContexts = DiscoverMigratableContexts(serviceProvider, logger);

        foreach (var context in migratableContexts.OrderBy(c => c.MigrationOrder))
        {
            await MigrateContextAsync(context, serviceProvider, logger, cancellationToken);
        }

        // Run final data migrations (those with AfterSchemaMigration = null)
        await RunFinalDataMigrationsAsync(serviceProvider, logger, cancellationToken);

        // Run seeding for any contexts that implement ISeedableDbContext
        await SeedAllContextsAsync(serviceProvider, logger, cancellationToken);
    }

    private static List<IMigratableDbContext> DiscoverMigratableContexts(
        IServiceProvider serviceProvider,
        ILogger? logger)
    {
        var contexts = new List<IMigratableDbContext>();

        // Get all registered DbContext types from the service collection
        // We check for common DbContext types that might implement IMigratableDbContext
        var dbContextTypes = new[]
        {
            typeof(OlusoDbContext),
            // Add known plugin context types here, or use assembly scanning
        };

        foreach (var contextType in dbContextTypes)
        {
            try
            {
                var service = serviceProvider.GetService(contextType);
                if (service is IMigratableDbContext migratable)
                {
                    contexts.Add(migratable);
                    logger?.LogDebug("Discovered migratable context: {ContextName} (Order: {Order})",
                        migratable.MigrationName, migratable.MigrationOrder);
                }
            }
            catch (Exception ex)
            {
                logger?.LogDebug(ex, "Could not resolve {ContextType}", contextType.Name);
            }
        }

        // Also try to resolve any IMigratableDbContext directly registered
        // This catches plugin contexts registered with their interface
        try
        {
            var allMigratable = serviceProvider.GetServices<IMigratableDbContext>();
            foreach (var migratable in allMigratable)
            {
                if (!contexts.Any(c => c.MigrationName == migratable.MigrationName))
                {
                    contexts.Add(migratable);
                    logger?.LogDebug("Discovered migratable context via interface: {ContextName} (Order: {Order})",
                        migratable.MigrationName, migratable.MigrationOrder);
                }
            }
        }
        catch (Exception ex)
        {
            logger?.LogDebug(ex, "Could not resolve IMigratableDbContext services");
        }

        return contexts;
    }

    private static async Task MigrateContextAsync(
        IMigratableDbContext context,
        IServiceProvider serviceProvider,
        ILogger? logger,
        CancellationToken cancellationToken)
    {
        var contextName = context.MigrationName;

        try
        {
            var pendingMigrations = await context.Database.GetPendingMigrationsAsync(cancellationToken);
            var pendingList = pendingMigrations.ToList();

            if (pendingList.Count == 0)
            {
                logger?.LogInformation("{ContextName}: Database is up to date", contextName);
                return;
            }

            logger?.LogInformation("{ContextName}: Applying {Count} pending migration(s) with interleaved data migrations",
                contextName, pendingList.Count);

            // Get the migrator service to apply migrations one-by-one
            var migrator = context.Database.GetInfrastructure().GetService<IMigrator>();
            if (migrator == null)
            {
                // Fallback to batch migration if migrator not available
                logger?.LogWarning("{ContextName}: IMigrator not available, using batch migration", contextName);
                await context.Database.MigrateAsync(cancellationToken);
                return;
            }

            // Get data migration runner
            var dataMigrationLogger = serviceProvider.GetService<ILogger<DataMigrationRunner>>();
            var runner = new DataMigrationRunner(serviceProvider, dataMigrationLogger);

            // Apply each schema migration one-by-one, running data migrations after each
            foreach (var migrationName in pendingList)
            {
                logger?.LogInformation("{ContextName}: Applying schema migration: {Migration}", contextName, migrationName);

                // Apply this single schema migration
                await migrator.MigrateAsync(migrationName, cancellationToken);

                // Run any data migrations tied to this schema migration
                if (context is DbContext dbContext)
                {
                    await runner.RunForSchemaMigrationAsync(dbContext, migrationName, cancellationToken);
                }
            }

            logger?.LogInformation("{ContextName}: All migrations applied successfully", contextName);
        }
        catch (Exception ex)
        {
            logger?.LogError(ex, "{ContextName}: Migration failed", contextName);
            throw;
        }
    }

    /// <summary>
    /// Checks if there are any pending migrations for all registered migratable contexts.
    /// </summary>
    /// <param name="serviceProvider">The service provider.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>A dictionary of context names to their pending migrations.</returns>
    public static async Task<Dictionary<string, IEnumerable<string>>> GetPendingMigrationsAsync(
        this IServiceProvider serviceProvider,
        CancellationToken cancellationToken = default)
    {
        var result = new Dictionary<string, IEnumerable<string>>();
        var logger = serviceProvider.GetService<ILogger<OlusoDbContext>>();

        var migratableContexts = DiscoverMigratableContexts(serviceProvider, logger);

        foreach (var context in migratableContexts.OrderBy(c => c.MigrationOrder))
        {
            var pending = await context.Database.GetPendingMigrationsAsync(cancellationToken);
            result[context.MigrationName] = pending;
        }

        return result;
    }

    /// <summary>
    /// Runs final data migrations (those with AfterSchemaMigration = null) for all registered migratable contexts.
    /// These run after all schema migrations are complete.
    /// </summary>
    private static async Task RunFinalDataMigrationsAsync(
        IServiceProvider serviceProvider,
        ILogger? logger,
        CancellationToken cancellationToken)
    {
        var dataMigrationLogger = serviceProvider.GetService<ILogger<DataMigrationRunner>>();
        var runner = new DataMigrationRunner(serviceProvider, dataMigrationLogger);

        try
        {
            // Get all migratable contexts and run final data migrations
            var migratableContexts = serviceProvider.GetServices<IMigratableDbContext>().ToList();

            foreach (var migratable in migratableContexts)
            {
                if (migratable is DbContext dbContext)
                {
                    await runner.RunFinalDataMigrationsAsync(dbContext, cancellationToken);
                }
            }
        }
        catch (Exception ex)
        {
            logger?.LogError(ex, "Final data migrations failed");
            throw;
        }
    }

    /// <summary>
    /// Seeds data for all registered seedable contexts.
    /// </summary>
    private static async Task SeedAllContextsAsync(
        IServiceProvider serviceProvider,
        ILogger? logger,
        CancellationToken cancellationToken)
    {
        var seedableContexts = DiscoverSeedableContexts(serviceProvider, logger);

        foreach (var context in seedableContexts)
        {
            await SeedContextAsync(context, logger, cancellationToken);
        }
    }

    /// <summary>
    /// Discovers all registered ISeedableDbContext implementations.
    /// </summary>
    private static List<ISeedableDbContext> DiscoverSeedableContexts(
        IServiceProvider serviceProvider,
        ILogger? logger)
    {
        var contexts = new List<ISeedableDbContext>();
        var seenNames = new HashSet<string>();

        // Check OlusoDbContext
        try
        {
            var olusContext = serviceProvider.GetService<OlusoDbContext>();
            if (olusContext is ISeedableDbContext seedable && seenNames.Add(seedable.SeedName))
            {
                contexts.Add(seedable);
                logger?.LogDebug("Discovered seedable context: {ContextName}", seedable.SeedName);
            }
        }
        catch (Exception ex)
        {
            logger?.LogDebug(ex, "Could not resolve OlusoDbContext for seeding");
        }

        // Discover via ISeedableDbContext registrations
        try
        {
            var allSeedable = serviceProvider.GetServices<ISeedableDbContext>();
            foreach (var seedable in allSeedable)
            {
                if (seenNames.Add(seedable.SeedName))
                {
                    contexts.Add(seedable);
                    logger?.LogDebug("Discovered seedable context via interface: {ContextName}", seedable.SeedName);
                }
            }
        }
        catch (Exception ex)
        {
            logger?.LogDebug(ex, "Could not resolve ISeedableDbContext services");
        }

        return contexts;
    }

    /// <summary>
    /// Seeds data for a single context.
    /// </summary>
    private static async Task SeedContextAsync(
        ISeedableDbContext context,
        ILogger? logger,
        CancellationToken cancellationToken)
    {
        var contextName = context.SeedName;

        try
        {
            logger?.LogInformation("{ContextName}: Running seed data...", contextName);
            await context.SeedAsync(cancellationToken);
            logger?.LogInformation("{ContextName}: Seed data completed", contextName);
        }
        catch (Exception ex)
        {
            logger?.LogError(ex, "{ContextName}: Seeding failed", contextName);
            throw;
        }
    }
}
