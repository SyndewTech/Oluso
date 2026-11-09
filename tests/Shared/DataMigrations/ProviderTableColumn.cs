namespace Oluso.Testing.DataMigrations;

/// <summary>A column of a SQL Server or PostgreSQL table, from <c>information_schema.columns</c>.</summary>
public sealed class ProviderTableColumn
{
    public string Name { get; set; } = default!;
    public string Type { get; set; } = default!;
    public int NotNull { get; set; }
    public int HasDefault { get; set; }
    public int IsIdentity { get; set; }
}
