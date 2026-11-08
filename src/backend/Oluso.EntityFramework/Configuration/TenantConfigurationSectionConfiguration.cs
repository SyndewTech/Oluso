using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Oluso.Core.Domain.Entities;

namespace Oluso.EntityFramework.Configuration;

public class TenantConfigurationSectionConfiguration : IEntityTypeConfiguration<TenantConfigurationSection>
{
    public void Configure(EntityTypeBuilder<TenantConfigurationSection> builder)
    {
        builder.ToTable("TenantConfigurationSections");

        builder.HasKey(s => new { s.TenantId, s.SectionKey });

        builder.Property(s => s.TenantId).IsRequired().HasMaxLength(128);
        builder.Property(s => s.SectionKey).IsRequired().HasMaxLength(100);
        builder.Property(s => s.Value).IsRequired();
        builder.Property(s => s.IsEncrypted).HasDefaultValue(false);

        // FK to Tenant (cascade delete — when tenant is deleted, sections go with it)
        builder.HasOne(s => s.Tenant)
            .WithMany()
            .HasForeignKey(s => s.TenantId)
            .OnDelete(DeleteBehavior.Cascade);
    }
}
