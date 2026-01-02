using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Oluso.Core.Domain.Entities;

namespace Oluso.EntityFramework.Configuration;

public class PluginMetadataConfiguration : IEntityTypeConfiguration<PluginMetadata>
{
    public void Configure(EntityTypeBuilder<PluginMetadata> builder)
    {
        builder.ToTable("PluginMetadata");

        builder.HasKey(x => x.Id);

        builder.Property(x => x.Id)
            .HasMaxLength(50)
            .IsRequired();

        builder.Property(x => x.Name)
            .HasMaxLength(200)
            .IsRequired();

        builder.Property(x => x.DisplayName)
            .HasMaxLength(500);

        builder.Property(x => x.Description)
            .HasMaxLength(2000);

        builder.Property(x => x.Version)
            .HasMaxLength(50);

        builder.Property(x => x.Author)
            .HasMaxLength(200);

        builder.Property(x => x.Scope)
            .HasConversion<string>()
            .HasMaxLength(20);

        builder.Property(x => x.TenantId)
            .HasMaxLength(50);

        builder.Property(x => x.StorageReference)
            .HasMaxLength(1000)
            .IsRequired();

        builder.Property(x => x.StorageProvider)
            .HasConversion<string>()
            .HasMaxLength(50);

        builder.Property(x => x.ContentHash)
            .HasMaxLength(128);

        builder.Property(x => x.RequiredClaims)
            .HasMaxLength(4000);

        builder.Property(x => x.OutputClaims)
            .HasMaxLength(4000);

        builder.Property(x => x.ConfigSchema);

        builder.Property(x => x.DefaultConfig);

        builder.Property(x => x.Type)
            .HasConversion<string>()
            .HasMaxLength(50);

        builder.Property(x => x.Tags)
            .HasMaxLength(1000);

        builder.Property(x => x.CreatedBy)
            .HasMaxLength(256);

        builder.Property(x => x.UpdatedBy)
            .HasMaxLength(256);

        // Unique index on Name + TenantId
        builder.HasIndex(x => new { x.Name, x.TenantId })
            .IsUnique();

        // Index for searching
        builder.HasIndex(x => x.TenantId);
        builder.HasIndex(x => x.Enabled);
        builder.HasIndex(x => x.Type);
    }
}
