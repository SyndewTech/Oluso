using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Oluso.EntityFramework.DataMigrations;

/// <summary>
/// Tracks which data migrations have been applied.
/// </summary>
public class DataMigrationHistory
{
    /// <summary>
    /// The unique identifier of the data migration.
    /// </summary>
    public required string MigrationId { get; set; }

    /// <summary>
    /// The DbContext type name this migration was applied to.
    /// </summary>
    public required string ContextType { get; set; }

    /// <summary>
    /// When the migration was applied.
    /// </summary>
    public DateTime AppliedAt { get; set; } = DateTime.UtcNow;

    /// <summary>
    /// Description of what the migration did.
    /// </summary>
    public string? Description { get; set; }
}

/// <summary>
/// Configuration for the DataMigrationHistory entity.
/// </summary>
public class DataMigrationHistoryConfiguration : IEntityTypeConfiguration<DataMigrationHistory>
{
    private readonly string _tableName;

    public DataMigrationHistoryConfiguration(string? pluginName = null)
    {
        _tableName = string.IsNullOrEmpty(pluginName)
            ? "__DataMigrationHistory"
            : $"__DataMigrationHistory_{pluginName}";
    }

    public void Configure(EntityTypeBuilder<DataMigrationHistory> builder)
    {
        builder.ToTable(_tableName);
        builder.HasKey(h => new { h.MigrationId, h.ContextType });
        builder.Property(h => h.MigrationId).HasMaxLength(256);
        builder.Property(h => h.ContextType).HasMaxLength(256);
        builder.Property(h => h.Description).HasMaxLength(1024);
    }
}
