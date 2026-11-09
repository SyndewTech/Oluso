using Oluso.Testing.ProviderMigrations;
using System.Reflection;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Oluso.Core.Data;
using Oluso.EntityFramework;
using Oluso.EntityFramework.DataMigrations;

namespace Oluso.Testing.DataMigrations;

/// <summary>
/// A new SQL Server or PostgreSQL database, made for the test on the server <see cref="ProviderTestServer"/> names and
/// dropped when disposed, migrated as a deploy migrates one: each schema migration of the context applied in order
/// with the data migrations bound to it run straight after (<see cref="MigrationExtensions.MigrateSchemaWithDataMigrationsAsync"/>),
/// then the remaining data migrations. Rows can be inserted before a named schema migration, as they stood then.
/// Whole-entity loads and saves by data migrations bound to a schema migration are collected in <see cref="Violations"/>.
/// </summary>
/// <remarks>
/// <paramref name="configure"/> sets the provider up as the module's own registration does (UseSqlServer or UseNpgsql
/// with its migrations history table), so the migrations run are the ones a deploy runs.
/// </remarks>
public sealed class FreshProviderDatabase<TContext> : IMigrationTestDatabase<TContext> where TContext : DbContext, IMigratableDbContext
{
    private readonly Func<DbContextOptions<TContext>, TContext> _create;
    private readonly Action<DbContextOptionsBuilder<TContext>, string> _configure;
    private readonly DataMigrationModelGuard _guard = new();
    private readonly List<(string SchemaMigration, Func<IMigrationRowWriter, Task> Seed)> _seeds = [];

    /// <summary>The seeds that have run, by schema migration, whichever migration call ran them.</summary>
    private readonly HashSet<string> _usedSeeds = new(StringComparer.Ordinal);
    private string? _connectionString;

    public FreshProviderDatabase(
        MigrationTestProvider provider,
        Func<DbContextOptions<TContext>, TContext> create,
        Action<DbContextOptionsBuilder<TContext>, string> configure)
    {
        Provider = provider;
        _create = create;
        _configure = configure;
    }

    public MigrationTestProvider Provider { get; }

    public IReadOnlyList<string> Violations => _guard.Violations;

    /// <summary>The ids of every data migration declared for the context.</summary>
    public IReadOnlyList<string> DeclaredDataMigrationIds { get; } = typeof(TContext).Assembly.GetTypes()
        .Where(t => !t.IsAbstract && typeof(IDataMigration).IsAssignableFrom(t))
        .Where(t => t.GetCustomAttribute<DataMigrationAttribute>()?.ContextType.IsAssignableFrom(typeof(TContext)) == true)
        .Select(t => ((IDataMigration)Activator.CreateInstance(t)!).MigrationId)
        .OrderBy(id => id, StringComparer.Ordinal)
        .ToList();

    /// <summary>Inserts rows before the schema migration whose name ends with <paramref name="schemaMigration"/> is applied.</summary>
    public FreshProviderDatabase<TContext> Before(string schemaMigration, Func<IMigrationRowWriter, Task> seed)
    {
        _seeds.Add((schemaMigration, seed));
        return this;
    }

    void IMigrationTestDatabase<TContext>.SeedBefore(string schemaMigration, Func<IMigrationRowWriter, Task> seed) => Before(schemaMigration, seed);

    /// <summary>
    /// Brings the database to where a deploy of an earlier release left it: the schema migrations up to and including
    /// the one whose name ends with <paramref name="lastApplied"/>, each followed by the data migrations bound to it,
    /// and the unbound data migrations recorded as applied (see <see cref="EarlierReleaseHistory"/>). Every later
    /// schema migration stays pending for <see cref="MigrateAsync"/>.
    /// </summary>
    public async Task MigrateToAsync(IServiceProvider services, string lastApplied)
    {
        _connectionString ??= await ProviderTestServer.CreateDatabaseAsync(Provider, "oluso_verify");
        await using var context = Context(_guard);
        var migrator = context.GetService<IMigrator>();
        var runner = new DataMigrationRunner(services, services.GetService<ILogger<DataMigrationRunner>>());
        var pending = (await context.Database.GetPendingMigrationsAsync()).ToList();
        var last = pending.FindIndex(m => m.EndsWith("_" + lastApplied, StringComparison.Ordinal));
        if (last < 0) throw new InvalidOperationException($"No pending schema migration ends with {lastApplied}");

        foreach (var schemaMigration in pending.Take(last + 1))
        {
            foreach (var (name, seed) in _seeds.Where(s => schemaMigration.EndsWith("_" + s.SchemaMigration, StringComparison.Ordinal)))
            {
                await seed(new ProviderRowWriter(context, Provider));
                _usedSeeds.Add(name);
            }
            await migrator.MigrateAsync(schemaMigration);
            _guard.BoundTo = schemaMigration;
            await runner.RunForSchemaMigrationAsync(context, schemaMigration);
            _guard.BoundTo = null;
        }
        await EarlierReleaseHistory.RecordUnboundAsAppliedAsync(context);
    }

    /// <summary>Inserts rows into the database as its schema stands now.</summary>
    public async Task SeedAsync(Func<IMigrationRowWriter, Task> seed)
    {
        await using var context = Context();
        await seed(new ProviderRowWriter(context, Provider));
    }

    /// <summary>The schema migrations not yet applied, in the order a deploy applies them.</summary>
    public async Task<IReadOnlyList<string>> PendingSchemaMigrationsAsync()
    {
        await using var context = Context();
        return (await context.Database.GetPendingMigrationsAsync()).ToList();
    }

    /// <summary>Creates the database and applies every schema and data migration of the context to it.</summary>
    public async Task MigrateAsync(IServiceProvider services)
    {
        _connectionString ??= await ProviderTestServer.CreateDatabaseAsync(Provider, "oluso_verify");
        var used = _usedSeeds;
        await using var context = Context(_guard);

        await context.MigrateSchemaWithDataMigrationsAsync(services, async (schemaMigration, _) =>
        {
            _guard.BoundTo = null;
            foreach (var (name, seed) in _seeds.Where(s => schemaMigration.EndsWith("_" + s.SchemaMigration, StringComparison.Ordinal)))
            {
                await seed(new ProviderRowWriter(context, Provider));
                used.Add(name);
            }
            _guard.BoundTo = schemaMigration;
        });
        _guard.BoundTo = null;

        await new DataMigrationRunner(services, services.GetService<ILogger<DataMigrationRunner>>()).RunAsync(context);

        var unused = _seeds.Select(s => s.SchemaMigration).Where(s => !used.Contains(s)).ToList();
        if (unused.Count > 0)
            throw new InvalidOperationException($"No schema migration ends with {string.Join(", ", unused)}");
    }

    /// <summary>The ids recorded in the context's data migration history.</summary>
    public async Task<IReadOnlyList<string>> AppliedDataMigrationIdsAsync()
    {
        await using var context = Context();
        var table = context.MigrationName is { Length: > 0 } name && name != "Oluso"
            ? $"__DataMigrationHistory_{name}"
            : "__DataMigrationHistory";
        return (await context.Database
                .SqlQueryRaw<string>($"SELECT \"MigrationId\" AS \"Value\" FROM \"{table}\"")
                .ToListAsync())
            .OrderBy(id => id, StringComparer.Ordinal)
            .ToList();
    }

    /// <summary>A context on the migrated database, for checking what the data migrations did.</summary>
    public TContext Context() => Context(guard: null);

    private TContext Context(DataMigrationModelGuard? guard)
    {
        // Every throwaway database has its own connection string, and so its own EF service provider; a run that
        // makes more than twenty of them is expected here, not a leak.
        var options = new DbContextOptionsBuilder<TContext>()
            .ConfigureWarnings(w => w.Ignore(CoreEventId.ManyServiceProvidersCreatedWarning));
        _configure(options, _connectionString ?? throw new InvalidOperationException("The database hasn't been created yet"));
        if (guard != null) options.AddInterceptors(guard);
        return _create(options.Options);
    }

    public async ValueTask DisposeAsync()
    {
        if (_connectionString != null) await ProviderTestServer.DropDatabaseAsync(Provider, _connectionString);
    }
}
