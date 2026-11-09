using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.Extensions.DependencyInjection;
using Oluso.EntityFramework;

namespace Oluso.Testing.ProviderMigrations;

/// <summary>
/// Migrates contexts from an empty database on a real server the way a deploy does: the contexts are registered
/// through the module's own <c>Add…ForProvider</c> call and migrated by <see cref="MigrationExtensions.MigrateOlusoDatabaseAsync(IServiceProvider, CancellationToken)"/>,
/// so every schema migration runs with its bound data migrations straight after, then the remaining data migrations and seeding.
/// </summary>
public static class ProviderMigrationHarness
{
    /// <summary>
    /// Creates an empty database, registers the contexts with <paramref name="register"/> (provider name, connection string),
    /// migrates it and returns the schema migrations still pending per context, which should be none. The database is dropped afterwards.
    /// </summary>
    public static async Task<IReadOnlyDictionary<string, IEnumerable<string>>> MigrateFromEmptyAsync(
        MigrationTestProvider provider,
        string label,
        Action<IServiceCollection, string, string> register)
    {
        await using var database = await ProviderTestDatabase.CreateAsync(provider, label);

        var services = new ServiceCollection();
        services.AddLogging();
        register(services, database.ProviderName, database.ConnectionString);
        IgnoreManyServiceProviders(services);
        await using var root = services.BuildServiceProvider();

        try
        {
            using var scope = root.CreateScope();
            await scope.ServiceProvider.MigrateOlusoDatabaseAsync();
        }
        catch (Exception ex)
        {
            throw new InvalidOperationException(
                $"Migrating from empty on {provider} failed at {await FirstPendingAsync(root)}: {ex.Message}", ex);
        }

        using (var scope = root.CreateScope())
        {
            var pending = await scope.ServiceProvider.GetPendingMigrationsAsync();
            return pending.ToDictionary(p => p.Key, p => (IEnumerable<string>)p.Value.ToList());
        }
    }

    /// <summary>
    /// A test process builds an EF service provider per distinct options set, and EF throws on the twenty-first, so a
    /// suite with many database tests would fail here for reasons unrelated to the migrations.
    /// </summary>
    private static void IgnoreManyServiceProviders(IServiceCollection services)
    {
        var configure = typeof(EntityFrameworkServiceCollectionExtensions).GetMethods()
            .Single(m => m.Name == nameof(EntityFrameworkServiceCollectionExtensions.ConfigureDbContext)
                && m.GetParameters()[1].ParameterType == typeof(Action<DbContextOptionsBuilder>));
        Action<DbContextOptionsBuilder> ignore = o => o.ConfigureWarnings(w => w.Ignore(CoreEventId.ManyServiceProvidersCreatedWarning));
        var contexts = services
            .Select(d => d.ServiceType)
            .Where(t => typeof(DbContext).IsAssignableFrom(t) && !t.IsAbstract)
            .Distinct()
            .ToList();
        foreach (var context in contexts)
            configure.MakeGenericMethod(context).Invoke(null, [services, ignore, ServiceLifetime.Scoped]);
    }

    /// <summary>The first schema migration of each context not yet applied, which is where a failed run stopped.</summary>
    private static async Task<string> FirstPendingAsync(IServiceProvider root)
    {
        try
        {
            using var scope = root.CreateScope();
            var pending = await scope.ServiceProvider.GetPendingMigrationsAsync();
            var stopped = string.Join(", ", pending
                .Where(p => p.Value.Any())
                .Select(p => $"{p.Key}:{p.Value.First()}"));
            return stopped.Length > 0 ? stopped : "the data migrations or seeding after the last schema migration";
        }
        catch (Exception ex)
        {
            return $"(pending migrations unreadable: {ex.Message})";
        }
    }
}
