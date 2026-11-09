using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;

namespace Oluso.Testing.DataMigrations;

/// <summary>
/// Records every whole entity loaded, and every SaveChanges, while a data migration bound to a schema
/// migration runs. Such a data migration runs against the schema as it stood then, but a whole-entity
/// load selects, and SaveChanges writes, every column the current model maps, including columns later
/// schema migrations add. It must read projections and write with ExecuteUpdate or targeted SQL.
/// </summary>
public sealed class DataMigrationModelGuard : SaveChangesInterceptor, IMaterializationInterceptor
{
    private readonly List<string> _violations = [];

    /// <summary>
    /// The schema migration whose data migrations are running, or null outside them: seeding, and the data
    /// migrations that run after every schema migration, may use the current model.
    /// </summary>
    public string? BoundTo { get; set; }

    public IReadOnlyList<string> Violations => _violations;

    public object InitializedInstance(MaterializationInterceptionData materializationData, object entity)
    {
        // SqlQuery over an unmapped type materializes an ad-hoc type that isn't part of the model.
        if (BoundTo != null && materializationData.Context.Model.FindEntityType(entity.GetType()) != null)
            _violations.Add($"{entity.GetType().Name} loaded whole by a data migration bound to {BoundTo}");
        return entity;
    }

    public override InterceptionResult<int> SavingChanges(DbContextEventData eventData, InterceptionResult<int> result)
    {
        RecordSave();
        return result;
    }

    public override ValueTask<InterceptionResult<int>> SavingChangesAsync(
        DbContextEventData eventData, InterceptionResult<int> result, CancellationToken cancellationToken = default)
    {
        RecordSave();
        return ValueTask.FromResult(result);
    }

    private void RecordSave()
    {
        if (BoundTo != null)
            _violations.Add($"SaveChanges called by a data migration bound to {BoundTo}");
    }
}
