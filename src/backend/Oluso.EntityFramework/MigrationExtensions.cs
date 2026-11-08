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
        await scope.ServiceProvider.MigrateOlusoDatabaseAsync(cancellationToken);
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

        var migratableContexts = serviceProvider.GetServices<IMigratableDbContext>()
            .OrderBy(c => c.MigrationOrder)
            .ToList();

        if (migratableContexts.Count == 0)
        {
            logger?.LogWarning("No migratable DbContexts found. Ensure OlusoDbContext or plugin contexts are registered.");
            return;
        }

        // Apply schema migrations with interleaved data migrations
        foreach (var context in migratableContexts)
        {
            await MigrateContextAsync(context, serviceProvider, logger, cancellationToken);
        }

        // Run any pending data migrations (including those whose schema migration was already applied)
        await RunPendingDataMigrationsAsync(serviceProvider, logger, cancellationToken);

        // Run seeding for any contexts that implement ISeedableDbContext
        await SeedAllContextsAsync(serviceProvider, logger, cancellationToken);
    }

    /// <summary>
    /// Applies one context's pending schema migrations one at a time, running the data migrations bound to
    /// each straight after it: the per-context step of <see cref="MigrateOlusoDatabaseAsync(IServiceProvider, CancellationToken)"/>,
    /// which afterwards runs the remaining data migrations and seeding for every context.
    /// </summary>
    /// <param name="context">The context to migrate.</param>
    /// <param name="serviceProvider">Services the data migrations resolve.</param>
    /// <param name="beforeSchemaMigration">
    /// Runs before each schema migration is applied, with its full name. Tests use it to insert rows as
    /// they stood at that point, so a data migration bound to the next schema migration has data to change.
    /// </param>
    /// <param name="cancellationToken">Cancellation token.</param>
    public static Task MigrateSchemaWithDataMigrationsAsync(
        this IMigratableDbContext context,
        IServiceProvider serviceProvider,
        Func<string, CancellationToken, Task>? beforeSchemaMigration = null,
        CancellationToken cancellationToken = default) =>
        MigrateContextAsync(context, serviceProvider, serviceProvider.GetService<ILogger<OlusoDbContext>>(), beforeSchemaMigration, cancellationToken);

    private static Task MigrateContextAsync(
        IMigratableDbContext context,
        IServiceProvider serviceProvider,
        ILogger? logger,
        CancellationToken cancellationToken) =>
        MigrateContextAsync(context, serviceProvider, logger, beforeSchemaMigration: null, cancellationToken);

    private static async Task MigrateContextAsync(
        IMigratableDbContext context,
        IServiceProvider serviceProvider,
        ILogger? logger,
        Func<string, CancellationToken, Task>? beforeSchemaMigration,
        CancellationToken cancellationToken)
    {
        var contextName = context.MigrationName;

        try
        {
            // Non-relational providers (e.g. InMemory for tests) don't support migrations.
            // Use EnsureCreated instead so the schema is still available.
            if (!context.Database.IsRelational())
            {
                logger?.LogInformation("{ContextName}: Non-relational provider detected, using EnsureCreated", contextName);
                if (context is DbContext dbContext)
                {
                    await dbContext.Database.EnsureCreatedAsync(cancellationToken);
                }
                return;
            }

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
                if (beforeSchemaMigration != null)
                {
                    await beforeSchemaMigration(migrationName, cancellationToken);
                }

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

        var migratableContexts = serviceProvider.GetServices<IMigratableDbContext>()
            .OrderBy(c => c.MigrationOrder)
            .ToList();

        foreach (var context in migratableContexts)
        {
            if (!context.Database.IsRelational())
            {
                result[context.MigrationName] = Enumerable.Empty<string>();
                continue;
            }

            var pending = await context.Database.GetPendingMigrationsAsync(cancellationToken);
            result[context.MigrationName] = pending;
        }

        return result;
    }

    /// <summary>
    /// Runs all pending data migrations for all registered migratable contexts.
    /// This catches:
    /// 1. Data migrations whose AfterSchemaMigration was already applied in a prior run
    /// 2. Data migrations with AfterSchemaMigration = null (final migrations)
    /// 3. Any other pending data migrations not yet recorded in the history table
    /// </summary>
    private static async Task RunPendingDataMigrationsAsync(
        IServiceProvider serviceProvider,
        ILogger? logger,
        CancellationToken cancellationToken)
    {
        var dataMigrationLogger = serviceProvider.GetService<ILogger<DataMigrationRunner>>();
        var runner = new DataMigrationRunner(serviceProvider, dataMigrationLogger);

        try
        {
            foreach (var migratable in serviceProvider.GetServices<IMigratableDbContext>())
            {
                if (migratable is DbContext dbContext && dbContext.Database.IsRelational())
                {
                    var contextType = dbContext.GetType();
                    var method = typeof(DataMigrationRunner)
                        .GetMethod(nameof(DataMigrationRunner.RunAsync))!
                        .MakeGenericMethod(contextType);

                    await (Task)method.Invoke(runner, new object?[] { dbContext, null, cancellationToken })!;
                }
            }
        }
        catch (Exception ex)
        {
            logger?.LogError(ex, "Pending data migrations failed");
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
