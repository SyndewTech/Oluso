using Microsoft.EntityFrameworkCore;
using Oluso.Core.Data;

namespace Oluso.Testing.DataMigrations;

/// <summary>A throwaway database a test migrates as a deploy does, whatever the provider.</summary>
public interface IMigrationTestDatabase<out TContext> : IAsyncDisposable where TContext : DbContext, IMigratableDbContext
{
    IReadOnlyList<string> Violations { get; }

    IReadOnlyList<string> DeclaredDataMigrationIds { get; }

    Task MigrateToAsync(IServiceProvider services, string lastApplied);

    Task SeedAsync(Func<IMigrationRowWriter, Task> seed);

    /// <summary>Inserts rows before the pending schema migration whose name ends with <paramref name="schemaMigration"/> is applied.</summary>
    void SeedBefore(string schemaMigration, Func<IMigrationRowWriter, Task> seed);

    Task MigrateAsync(IServiceProvider services);

    Task<IReadOnlyList<string>> PendingSchemaMigrationsAsync();

    Task<IReadOnlyList<string>> AppliedDataMigrationIdsAsync();

    TContext Context();
}
