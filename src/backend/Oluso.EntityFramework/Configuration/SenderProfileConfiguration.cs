using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Oluso.Core.Domain.Entities;

namespace Oluso.EntityFramework.Configuration;

public class SenderProfileConfiguration : IEntityTypeConfiguration<SenderProfile>
{
    public void Configure(EntityTypeBuilder<SenderProfile> builder)
    {
        builder.ToTable("SenderProfiles");

        builder.HasKey(s => s.Id);

        builder.Property(s => s.Id).IsRequired().HasMaxLength(64);
        builder.Property(s => s.TenantId).IsRequired().HasMaxLength(128);
        builder.Property(s => s.Name).IsRequired().HasMaxLength(200);
        builder.Property(s => s.Provider).IsRequired().HasConversion<string>().HasMaxLength(32);
        builder.Property(s => s.FromAddress).IsRequired().HasMaxLength(320);
        builder.Property(s => s.FromName).IsRequired().HasMaxLength(200);
        builder.Property(s => s.ReplyToAddress).HasMaxLength(320);
        builder.Property(s => s.LastTestError).HasMaxLength(2000);
        builder.Property(s => s.CreatedBy).HasMaxLength(128);
        builder.Property(s => s.UpdatedBy).HasMaxLength(128);

        builder.HasIndex(s => new { s.TenantId, s.IsActive });
        builder.HasIndex(s => new { s.TenantId, s.IsDefault });
        builder.HasIndex(s => new { s.TenantId, s.Name }).IsUnique();
    }
}
