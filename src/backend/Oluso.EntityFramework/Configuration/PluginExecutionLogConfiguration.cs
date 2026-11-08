using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Oluso.Core.Domain.Entities;

namespace Oluso.EntityFramework.Configuration;

public class PluginExecutionLogConfiguration : IEntityTypeConfiguration<PluginExecutionLog>
{
    public void Configure(EntityTypeBuilder<PluginExecutionLog> builder)
    {
        builder.ToTable("PluginExecutionLogs");

        builder.HasKey(x => x.Id);

        builder.Property(x => x.Id)
            .HasMaxLength(50)
            .IsRequired();

        builder.Property(x => x.TenantId)
            .HasMaxLength(50)
            .IsRequired();

        builder.Property(x => x.PluginId)
            .HasMaxLength(50)
            .IsRequired();

        builder.Property(x => x.HookBindingId)
            .HasMaxLength(50);

        builder.Property(x => x.ExecutionId)
            .HasMaxLength(50)
            .IsRequired();

        builder.Property(x => x.Domain)
            .HasMaxLength(50)
            .IsRequired();

        builder.Property(x => x.EventType)
            .HasMaxLength(50)
            .IsRequired();

        builder.Property(x => x.EntityType)
            .HasMaxLength(100);

        builder.Property(x => x.EntityId)
            .HasMaxLength(100);

        builder.Property(x => x.Result)
            .HasConversion<string>()
            .HasMaxLength(20);

        builder.Property(x => x.ResultAction)
            .HasMaxLength(50);

        builder.Property(x => x.InputSummary)
            .HasMaxLength(4000);

        builder.Property(x => x.OutputSummary)
            .HasMaxLength(4000);

        builder.Property(x => x.ErrorMessage)
            .HasMaxLength(2000);

        builder.Property(x => x.ErrorStackTrace);

        builder.Property(x => x.UserId)
            .HasMaxLength(256);

        // Indexes for querying
        builder.HasIndex(x => x.TenantId);
        builder.HasIndex(x => x.PluginId);
        builder.HasIndex(x => x.ExecutionId);
        builder.HasIndex(x => x.ExecutedAt);
        builder.HasIndex(x => new { x.TenantId, x.PluginId, x.ExecutedAt })
            .HasDatabaseName("IX_PluginExecutionLogs_TenantPluginDate");
    }
}
