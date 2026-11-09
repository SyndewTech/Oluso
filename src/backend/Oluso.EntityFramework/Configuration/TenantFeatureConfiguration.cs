using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Oluso.Core.Domain.Entities;

namespace Oluso.EntityFramework.Configuration;

public class TenantFeatureConfiguration : IEntityTypeConfiguration<TenantFeature>
{
    public void Configure(EntityTypeBuilder<TenantFeature> builder)
    {
        builder.ToTable("TenantFeatures");
        builder.HasKey(f => f.Id);

        builder.Property(f => f.Id).HasMaxLength(128);
        builder.Property(f => f.TenantId).IsRequired().HasMaxLength(128);
        builder.Property(f => f.FeatureKey).IsRequired().HasMaxLength(100);
        builder.Property(f => f.GrantedByUserId).HasMaxLength(128);

        // Unique: one feature entry per tenant
        builder.HasIndex(f => new { f.TenantId, f.FeatureKey }).IsUnique();

        // Query by tenant
        builder.HasIndex(f => f.TenantId);

        // FK to Tenant (cascade delete)
        builder.HasOne(f => f.Tenant)
            .WithMany(t => t.Features)
            .HasForeignKey(f => f.TenantId)
            .OnDelete(DeleteBehavior.Cascade);
    }
}
