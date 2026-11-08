using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Oluso.Core.Domain.Entities;

namespace Oluso.EntityFramework.Configuration;

public class UserModuleAccessConfiguration : IEntityTypeConfiguration<UserModuleAccess>
{
    public void Configure(EntityTypeBuilder<UserModuleAccess> builder)
    {
        builder.ToTable("UserModuleAccess");
        builder.HasKey(a => a.Id);

        builder.Property(a => a.Id).HasMaxLength(128);
        builder.Property(a => a.TenantId).IsRequired().HasMaxLength(128);
        builder.Property(a => a.UserId).IsRequired().HasMaxLength(128);
        builder.Property(a => a.FeatureKey).IsRequired().HasMaxLength(100);
        builder.Property(a => a.GrantedByUserId).HasMaxLength(128);

        // One seat per user per module per tenant
        builder.HasIndex(a => new { a.TenantId, a.UserId, a.FeatureKey }).IsUnique();

        // Fast lookups: list users in a module, count seats
        builder.HasIndex(a => new { a.TenantId, a.FeatureKey });

        // FK to Tenant (cascade delete)
        builder.HasOne(a => a.Tenant)
            .WithMany()
            .HasForeignKey(a => a.TenantId)
            .OnDelete(DeleteBehavior.Cascade);
    }
}
