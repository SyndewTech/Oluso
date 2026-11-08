namespace Oluso.Testing.DataMigrations;

/// <summary>A row of SQLite's <c>pragma_table_info</c>.</summary>
public sealed class SqliteTableColumn
{
    public string Name { get; set; } = default!;
    public string Type { get; set; } = default!;
    public long NotNull { get; set; }
    public string? DefaultValue { get; set; }
}
