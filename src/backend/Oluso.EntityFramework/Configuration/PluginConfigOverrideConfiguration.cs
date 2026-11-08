using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Oluso.Core.Domain.Entities;

namespace Oluso.EntityFramework.Configuration;

public class PluginConfigOverrideConfiguration : IEntityTypeConfiguration<PluginConfigOverride>
{
    public void Configure(EntityTypeBuilder<PluginConfigOverride> builder)
    {
        builder.ToTable("PluginConfigOverrides");

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

        builder.Property(x => x.ConfigJson)
            .IsRequired();

        builder.Property(x => x.SecretsJson);

        builder.Property(x => x.UpdatedBy)
            .HasMaxLength(256);

        // Relationships
        builder.HasOne(x => x.Plugin)
            .WithMany()
            .HasForeignKey(x => x.PluginId)
            .OnDelete(DeleteBehavior.Cascade);

        // Indexes
        builder.HasIndex(x => x.TenantId);
        builder.HasIndex(x => new { x.TenantId, x.PluginId })
            .IsUnique();
    }
}
