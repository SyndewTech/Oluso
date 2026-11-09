using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Oluso.Core.Domain.Entities;

namespace Oluso.EntityFramework.Configuration;

public class PluginHookBindingConfiguration : IEntityTypeConfiguration<PluginHookBinding>
{
    public void Configure(EntityTypeBuilder<PluginHookBinding> builder)
    {
        builder.ToTable("PluginHookBindings");

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

        builder.Property(x => x.Domain)
            .HasMaxLength(50)
            .IsRequired();

        builder.Property(x => x.EntityType)
            .HasMaxLength(100)
            .IsRequired();

        builder.Property(x => x.EventType)
            .HasMaxLength(50)
            .IsRequired();

        builder.Property(x => x.FunctionName)
            .HasMaxLength(200)
            .IsRequired();

        builder.Property(x => x.FailureMode)
            .HasConversion<string>()
            .HasMaxLength(20);

        builder.Property(x => x.FilterConditions)
            .HasMaxLength(4000);

        // Relationships
        builder.HasOne(x => x.Plugin)
            .WithMany()
            .HasForeignKey(x => x.PluginId)
            .OnDelete(DeleteBehavior.Cascade);

        // Indexes
        builder.HasIndex(x => x.TenantId);
        builder.HasIndex(x => x.PluginId);
        builder.HasIndex(x => new { x.TenantId, x.Domain, x.EntityType, x.EventType, x.IsActive })
            .HasDatabaseName("IX_PluginHookBindings_DomainEventLookup");
    }
}
