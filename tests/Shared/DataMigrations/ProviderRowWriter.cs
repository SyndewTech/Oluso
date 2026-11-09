using Oluso.Testing.ProviderMigrations;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata;

namespace Oluso.Testing.DataMigrations;

/// <summary>
/// Inserts rows into a SQL Server or PostgreSQL database as the schema stands at the moment, not as the current model
/// maps it: the values are keyed by the column names of the moment, and every other NOT NULL column without a default
/// gets a neutral value (the property's CLR default through its value converter where the current model still maps
/// the column). Times go in as the column takes them: UTC for timestamptz, which refuses any other kind.
/// </summary>
public sealed class ProviderRowWriter : IMigrationRowWriter
{
    private readonly DbContext _db;
    private readonly MigrationTestProvider _provider;

    public ProviderRowWriter(DbContext db, MigrationTestProvider provider)
    {
        _db = db;
        _provider = provider;
    }

    public Task InsertAsync<TEntity>(IReadOnlyDictionary<string, object?> values) where TEntity : class
    {
        var entity = _db.Model.FindEntityType(typeof(TEntity))
            ?? throw new InvalidOperationException($"{typeof(TEntity).Name} isn't in the model");
        return InsertAsync(entity.GetSchema(), entity.GetTableName()!, values);
    }

    /// <summary>
    /// Inserts into <paramref name="table"/>: "schema.table", or a bare name in the schema of the entity mapped to it,
    /// or, for a table the model no longer maps, in the schema the context's tables are in.
    /// </summary>
    public Task InsertAsync(string table, IReadOnlyDictionary<string, object?> values)
    {
        var dot = table.IndexOf('.');
        if (dot > 0) return InsertAsync(table[..dot], table[(dot + 1)..], values);

        var entities = _db.Model.GetEntityTypes().ToList();
        var schema = entities.FirstOrDefault(e => e.GetTableName() == table)?.GetSchema()
            ?? entities.Select(e => e.GetSchema()).FirstOrDefault(s => s != null);
        return InsertAsync(schema, table, values);
    }

    private async Task InsertAsync(string? schema, string table, IReadOnlyDictionary<string, object?> values)
    {
        schema ??= _provider == MigrationTestProvider.SqlServer ? "dbo" : "public";
        var columns = await ColumnsAsync(schema, table);
        if (columns.Count == 0)
            throw new InvalidOperationException($"Table {schema}.{table} doesn't exist at this point");

        var unknown = values.Keys.Where(k => columns.All(c => c.Name != k)).ToList();
        if (unknown.Count > 0)
            throw new InvalidOperationException($"Table {schema}.{table} has no column {string.Join(", ", unknown)} at this point");

        var row = new Dictionary<string, object?>(values, StringComparer.Ordinal);
        foreach (var column in columns.Where(c => c.NotNull == 1 && c.HasDefault == 0 && c.IsIdentity == 0 && !row.ContainsKey(c.Name)))
            row[column.Name] = Neutral(table, column);

        var names = row.Keys.ToList();
        var parameters = new List<object>();
        var placeholders = new List<string>();
        foreach (var name in names)
        {
            // A bare null takes its type from the column it goes in; a null parameter would need one of its own.
            if (row[name] is null)
            {
                placeholders.Add("NULL");
                continue;
            }
            placeholders.Add($"{{{parameters.Count}}}");
            parameters.Add(ForColumn(row[name]!, columns.Single(c => c.Name == name)));
        }

        var sql = $"INSERT INTO {Quote(schema)}.{Quote(table)} ({string.Join(", ", names.Select(Quote))}) VALUES ({string.Join(", ", placeholders)})";
        await _db.Database.ExecuteSqlRawAsync(sql, parameters);
    }

    private Task<List<ProviderTableColumn>> ColumnsAsync(string schema, string table) =>
        _provider == MigrationTestProvider.SqlServer
            ? _db.Database.SqlQueryRaw<ProviderTableColumn>("""
                SELECT c.COLUMN_NAME AS Name, c.DATA_TYPE AS Type,
                       CASE WHEN c.IS_NULLABLE = 'NO' THEN 1 ELSE 0 END AS NotNull,
                       CASE WHEN c.COLUMN_DEFAULT IS NULL THEN 0 ELSE 1 END AS HasDefault,
                       ISNULL(COLUMNPROPERTY(OBJECT_ID(QUOTENAME(c.TABLE_SCHEMA) + '.' + QUOTENAME(c.TABLE_NAME)), c.COLUMN_NAME, 'IsIdentity'), 0) AS IsIdentity
                FROM INFORMATION_SCHEMA.COLUMNS c
                WHERE c.TABLE_SCHEMA = {0} AND c.TABLE_NAME = {1}
                """, schema, table).ToListAsync()
            : _db.Database.SqlQueryRaw<ProviderTableColumn>("""
                SELECT column_name::text AS "Name", data_type::text AS "Type",
                       CASE WHEN is_nullable = 'NO' THEN 1 ELSE 0 END AS "NotNull",
                       CASE WHEN column_default IS NULL THEN 0 ELSE 1 END AS "HasDefault",
                       CASE WHEN is_identity = 'YES' THEN 1 ELSE 0 END AS "IsIdentity"
                FROM information_schema.columns
                WHERE table_schema = {0} AND table_name = {1}
                """, schema, table).ToListAsync();

    private string Quote(string name) => _provider == MigrationTestProvider.SqlServer ? $"[{name}]" : $"\"{name}\"";

    private static object ForColumn(object value, ProviderTableColumn column) => value switch
    {
        DateTime d when column.Type == "timestamp with time zone" => d.Kind == DateTimeKind.Utc ? d : DateTime.SpecifyKind(d, DateTimeKind.Utc),
        DateTime d when column.Type == "timestamp without time zone" => DateTime.SpecifyKind(d, DateTimeKind.Unspecified),
        _ => value,
    };

    private object Neutral(string table, ProviderTableColumn column)
    {
        var property = _db.Model.GetEntityTypes()
            .Where(e => e.GetTableName() == table)
            .SelectMany(e => e.GetProperties())
            .FirstOrDefault(p => p.GetColumnName(StoreObjectIdentifier.Table(table, p.DeclaringType.GetSchema())) == column.Name);
        if (property != null && ClrDefault(property) is { } value)
            return ForColumn(value, column);

        return column.Type.ToLowerInvariant() switch
        {
            "int" or "integer" or "smallint" or "tinyint" => 0,
            "bigint" => 0L,
            "bit" or "boolean" => false,
            "decimal" or "numeric" or "money" => 0m,
            "float" or "real" or "double precision" => 0.0,
            "datetime2" or "datetime" or "date" or "timestamp with time zone" => DateTime.SpecifyKind(DateTime.UnixEpoch, DateTimeKind.Utc),
            "timestamp without time zone" => DateTime.SpecifyKind(DateTime.UnixEpoch, DateTimeKind.Unspecified),
            "datetimeoffset" => DateTimeOffset.UnixEpoch,
            "uniqueidentifier" or "uuid" => Guid.Empty,
            "varbinary" or "bytea" => Array.Empty<byte>(),
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
