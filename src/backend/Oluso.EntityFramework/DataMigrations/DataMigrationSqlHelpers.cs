using Microsoft.EntityFrameworkCore;

namespace Oluso.EntityFramework.DataMigrations;

/// <summary>
/// Provider-aware helpers for writing cross-provider raw SQL inside a
/// <see cref="Oluso.Core.Data.DataMigrationBase"/>.
/// <para>
/// Data migrations call <c>ExecuteSqlRawAsync</c> with literal SQL strings,
/// but table and column names differ by provider — schema/prefix conventions
/// are applied at runtime in <c>PluginDbContextBase.ApplyModuleTableNaming</c>,
/// and PostgreSQL also requires double-quoting to preserve PascalCase identifiers.
/// Use these helpers to resolve names from EF metadata and quote only where needed.
/// </para>
/// </summary>
public static class DataMigrationSqlHelpers
{
    /// <summary>
    /// Returns the identifier quote string required by the active provider:
    /// <c>"</c> for PostgreSQL; empty string for SQL Server and SQLite (both
    /// accept PascalCase unquoted because identifiers are case-insensitive).
    /// Hand-written SQL that may name a reserved word (Key, Order, ...) uses <see cref="Quote"/> instead.
    /// </summary>
    public static string Q(DbContext context) =>
        IsPostgres(context) ? "\"" : "";

    /// <summary>
    /// Wraps a single identifier in the provider's delimiters, so reserved words such as Key are valid
    /// column names: <c>"…"</c> on PostgreSQL and SQLite, <c>[…]</c> on SQL Server.
    /// </summary>
    public static string Quote(DbContext context, string identifier) =>
        IsSqlServer(context) ? $"[{identifier}]" : $"\"{identifier}\"";

    /// <summary>
    /// Returns a fully-qualified, appropriately-quoted table reference for the
    /// given entity type using EF's current model metadata. Handles schema,
    /// prefix-in-name, and provider-specific quoting transparently.
    /// </summary>
    public static string QualifiedTable<TEntity>(DbContext context) where TEntity : class =>
        QualifiedTable(context, typeof(TEntity));

    /// <summary>
    /// Non-generic form of <see cref="QualifiedTable{TEntity}"/> for use when
    /// the entity type is only known at runtime.
    /// </summary>
    public static string QualifiedTable(DbContext context, Type entityType)
    {
        var entity = context.Model.FindEntityType(entityType)
            ?? throw new InvalidOperationException($"Entity type {entityType.Name} not found in the model");

        var table = entity.GetTableName()
            ?? throw new InvalidOperationException($"Entity type {entityType.Name} has no table name");
        var schema = entity.GetSchema();

        return schema is null
            ? Quote(context, table)
            : $"{Quote(context, schema)}.{Quote(context, table)}";
    }

    /// <summary>
    /// Returns the quoted column name of <paramref name="propertyName"/> on
    /// <typeparamref name="TEntity"/>, read from EF's model for the entity's table,
    /// so column renames and provider naming are honoured.
    /// </summary>
    public static string Column<TEntity>(DbContext context, string propertyName) where TEntity : class
    {
        var entity = context.Model.FindEntityType(typeof(TEntity))
            ?? throw new InvalidOperationException($"Entity type {typeof(TEntity).Name} not found in the model");
        var property = entity.FindProperty(propertyName)
            ?? throw new InvalidOperationException($"Property {propertyName} not found on {typeof(TEntity).Name}");
        var table = entity.GetTableName()
            ?? throw new InvalidOperationException($"Entity type {typeof(TEntity).Name} has no table name");
        var column = property.GetColumnName(Microsoft.EntityFrameworkCore.Metadata.StoreObjectIdentifier.Table(table, entity.GetSchema()))
            ?? throw new InvalidOperationException($"Property {propertyName} on {typeof(TEntity).Name} is not mapped to a column");

        return Quote(context, column);
    }

    /// <summary>
    /// Returns a quoted table reference for a bare table name (no schema, no
    /// prefix logic). Use when a data migration targets a table that isn't
    /// exposed through a <see cref="DbSet{TEntity}"/>.
    /// </summary>
    public static string QuoteBareTable(DbContext context, string tableName) =>
        Quote(context, tableName);

    private static bool IsSqlServer(DbContext context) =>
        (context.Database.ProviderName ?? "")
            .Contains("SqlServer", StringComparison.OrdinalIgnoreCase);

    private static bool IsPostgres(DbContext context) =>
        (context.Database.ProviderName ?? "")
            .Contains("Npgsql", StringComparison.OrdinalIgnoreCase);
}
