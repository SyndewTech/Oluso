namespace Oluso.Testing.DataMigrations;

/// <summary>Inserts rows as the schema stands at the moment a seed runs, whatever the provider.</summary>
public interface IMigrationRowWriter
{
    /// <summary>Inserts one row into the table the current model maps <typeparamref name="TEntity"/> to.</summary>
    Task InsertAsync<TEntity>(IReadOnlyDictionary<string, object?> values) where TEntity : class;

    /// <summary>Inserts one row into <paramref name="table"/>, named as the provider names it, which must exist at this point.</summary>
    Task InsertAsync(string table, IReadOnlyDictionary<string, object?> values);
}
