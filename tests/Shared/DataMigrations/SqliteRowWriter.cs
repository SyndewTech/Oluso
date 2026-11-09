using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata;

namespace Oluso.Testing.DataMigrations;

/// <summary>
/// Inserts rows as the schema stands at the moment, not as the current model maps it: the values are keyed
/// by the column names of the moment, and every other NOT NULL column without a default gets a neutral value
/// (the property's CLR default through its value converter where the current model still maps the column).
/// </summary>
public sealed class SqliteRowWriter : IMigrationRowWriter
{
    private readonly DbContext _db;

    public SqliteRowWriter(DbContext db) => _db = db;

    /// <summary>Inserts one row into the table the current model maps <typeparamref name="TEntity"/> to.</summary>
    public Task InsertAsync<TEntity>(IReadOnlyDictionary<string, object?> values) where TEntity : class
    {
        var entity = _db.Model.FindEntityType(typeof(TEntity))
            ?? throw new InvalidOperationException($"{typeof(TEntity).Name} isn't in the model");
        return InsertAsync(entity.GetTableName()!, values);
    }

    /// <summary>Inserts one row into <paramref name="table"/>, which must exist at this point.</summary>
    public async Task InsertAsync(string table, IReadOnlyDictionary<string, object?> values)
    {
        var columns = await _db.Database
            .SqlQueryRaw<SqliteTableColumn>(
                "SELECT name AS \"Name\", type AS \"Type\", \"notnull\" AS \"NotNull\", dflt_value AS \"DefaultValue\" FROM pragma_table_info({0})", table)
            .ToListAsync();
        if (columns.Count == 0)
            throw new InvalidOperationException($"Table {table} doesn't exist at this point");

        var unknown = values.Keys.Where(k => columns.All(c => c.Name != k)).ToList();
        if (unknown.Count > 0)
            throw new InvalidOperationException($"Table {table} has no column {string.Join(", ", unknown)} at this point");

        var row = new Dictionary<string, object?>(values, StringComparer.Ordinal);
        foreach (var column in columns.Where(c => c.NotNull == 1 && c.DefaultValue == null && !row.ContainsKey(c.Name)))
            row[column.Name] = Neutral(table, column);

        var names = row.Keys.ToList();
        var sql = $"INSERT INTO \"{table}\" ({string.Join(", ", names.Select(n => $"\"{n}\""))}) " +
                  $"VALUES ({string.Join(", ", names.Select((_, i) => $"{{{i}}}"))})";
        await _db.Database.ExecuteSqlRawAsync(sql, names.Select(n => row[n] ?? DBNull.Value));
    }

    private object Neutral(string table, SqliteTableColumn column)
    {
        var property = _db.Model.GetEntityTypes()
            .Where(e => e.GetTableName() == table)
            .SelectMany(e => e.GetProperties())
            .FirstOrDefault(p => p.GetColumnName(StoreObjectIdentifier.Table(table, null)) == column.Name);
        if (property != null && ClrDefault(property) is { } value)
            return value;

        return column.Type.ToUpperInvariant() switch
        {
            "INTEGER" => 0L,
            "REAL" => 0.0,
            "BLOB" => Array.Empty<byte>(),
            _ => string.Empty,
        };
    }

    private static object? ClrDefault(IProperty property)
    {
        var type = Nullable.GetUnderlyingType(property.ClrType) ?? property.ClrType;
        object? value = type == typeof(string) ? string.Empty
            : type.IsValueType ? Activator.CreateInstance(type)
            : type.GetConstructor(Type.EmptyTypes) != null ? Activator.CreateInstance(type)
            : null;
        if (value == null) return null;
        return property.GetTypeMapping().Converter is { } converter ? converter.ConvertToProvider(value) : value;
    }
}
