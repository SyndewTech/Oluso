using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Oluso.Core.Terminology;

namespace Oluso.EntityFramework.Configuration;

/// <summary>
/// Entity Framework configuration for the platform-owned
/// <see cref="TerminologyOverride"/> table (one override per tenant + locale + key).
/// </summary>
public class TerminologyOverrideConfiguration : IEntityTypeConfiguration<TerminologyOverride>
{
    public void Configure(EntityTypeBuilder<TerminologyOverride> builder)
    {
        builder.HasKey(o => o.Id);

        builder.Property(o => o.Id)
            .HasMaxLength(36)
            .IsRequired();

        builder.Property(o => o.TenantId)
            .HasMaxLength(36)
            .IsRequired();

        builder.Property(o => o.Locale)
            .HasMaxLength(20)
            .IsRequired();

        builder.Property(o => o.Key)
            .HasMaxLength(200)
            .IsRequired();

        builder.Property(o => o.Value)
            .HasMaxLength(500)
            .IsRequired();

        builder.Property(o => o.CreatedBy)
            .HasMaxLength(256);

        builder.Property(o => o.UpdatedBy)
            .HasMaxLength(256);

        // One override per tenant + locale + key.
        builder.HasIndex(o => new { o.TenantId, o.Locale, o.Key })
            .IsUnique()
            .HasDatabaseName("IX_TerminologyOverrides_Tenant_Locale_Key");
    }
}
