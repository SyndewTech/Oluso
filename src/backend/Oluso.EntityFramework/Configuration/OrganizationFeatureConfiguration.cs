using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Oluso.Core.Domain.Entities;

namespace Oluso.EntityFramework.Configuration;

public class OrganizationFeatureConfiguration : IEntityTypeConfiguration<OrganizationFeature>
{
    public void Configure(EntityTypeBuilder<OrganizationFeature> builder)
    {
        builder.ToTable("OrganizationFeatures");
        builder.HasKey(f => f.Id);

        builder.Property(f => f.Id).HasMaxLength(128);
        builder.Property(f => f.OrganizationId).IsRequired().HasMaxLength(128);
        builder.Property(f => f.FeatureKey).IsRequired().HasMaxLength(100);
        builder.Property(f => f.GrantedByUserId).HasMaxLength(128);
        builder.Property(f => f.Source).IsRequired().HasMaxLength(50).HasDefaultValue("manual");
        builder.Property(f => f.SourceSubscriptionId).HasMaxLength(128);

        // Unique: one feature entry per org
        builder.HasIndex(f => new { f.OrganizationId, f.FeatureKey }).IsUnique();

        // Query by org
        builder.HasIndex(f => f.OrganizationId);

        // FK to Organization (cascade delete)
        builder.HasOne(f => f.Organization)
            .WithMany(o => o.Features)
            .HasForeignKey(f => f.OrganizationId)
            .OnDelete(DeleteBehavior.Cascade);
    }
}
