using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Oluso.Core.Domain.Entities;

namespace Oluso.EntityFramework.Configuration;

public class TenantModuleSeedConfiguration : IEntityTypeConfiguration<TenantModuleSeed>
{
    public void Configure(EntityTypeBuilder<TenantModuleSeed> builder)
    {
        builder.ToTable("TenantModuleSeeds");
        builder.HasKey(s => s.Id);

        builder.Property(s => s.Id).HasMaxLength(128);
        builder.Property(s => s.TenantId).IsRequired().HasMaxLength(128);
        builder.Property(s => s.FeatureKey).IsRequired().HasMaxLength(100);

        builder.HasIndex(s => new { s.TenantId, s.FeatureKey }).IsUnique();
        builder.HasIndex(s => s.TenantId);

        builder.HasOne(s => s.Tenant)
            .WithMany()
            .HasForeignKey(s => s.TenantId)
            .OnDelete(DeleteBehavior.Cascade);
    }
}
