using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Oluso.Core.Domain.Entities;

namespace Oluso.EntityFramework.Configuration;

public class PluginNetworkConfigConfiguration : IEntityTypeConfiguration<PluginNetworkConfig>
{
    public void Configure(EntityTypeBuilder<PluginNetworkConfig> builder)
    {
        builder.ToTable("PluginNetworkConfigs");

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

        builder.Property(x => x.AccessMode)
            .HasConversion<string>()
            .HasMaxLength(20);

        builder.Property(x => x.AllowedHosts)
            .HasMaxLength(4000);

        builder.Property(x => x.ApprovedBy)
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
