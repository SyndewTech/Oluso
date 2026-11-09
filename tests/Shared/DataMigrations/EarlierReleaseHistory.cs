using System.Reflection;
using Microsoft.EntityFrameworkCore;
using Oluso.Core.Data;

namespace Oluso.Testing.DataMigrations;

/// <summary>
/// Records unbound data migrations as applied without running them, as an earlier release's deploy left them: they
/// ran then, last, against that release's model, which a test built on today's model can't reproduce.
/// </summary>
public static class EarlierReleaseHistory
{
    public static async Task RecordUnboundAsAppliedAsync<TContext>(TContext context) where TContext : DbContext, IMigratableDbContext
    {
        var contextType = context.GetType();
        var unbound = contextType.Assembly.GetTypes()
            .Where(t => !t.IsAbstract && typeof(IDataMigration).IsAssignableFrom(t))
            .Where(t => t.GetCustomAttribute<DataMigrationAttribute>()?.ContextType.IsAssignableFrom(contextType) == true)
            .Select(t => (IDataMigration)Activator.CreateInstance(t)!)
            .Where(m => m.AfterSchemaMigration == null && m.RunAt == null)
            .ToList();

        var table = context.MigrationName is { Length: > 0 } name && name != "Oluso" ? $"__DataMigrationHistory_{name}" : "__DataMigrationHistory";
        var applied = (await context.Database.SqlQueryRaw<string>($"SELECT \"MigrationId\" AS \"Value\" FROM \"{table}\"").ToListAsync()).ToHashSet();
        foreach (var migration in unbound.Where(m => !applied.Contains(m.MigrationId)))
        {
            await context.Database.ExecuteSqlRawAsync(
                $"INSERT INTO \"{table}\" (\"MigrationId\", \"ContextType\", \"AppliedAt\", \"Description\") VALUES ({{0}}, {{1}}, {{2}}, {{3}})",
                migration.MigrationId, contextType.FullName!, DateTime.UtcNow, migration.Description);
        }
    }
}
