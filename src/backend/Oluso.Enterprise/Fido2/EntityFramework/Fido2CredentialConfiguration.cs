using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Oluso.Enterprise.Fido2.Entities;

namespace Oluso.Enterprise.Fido2.EntityFramework;

public class Fido2CredentialConfiguration : IEntityTypeConfiguration<Fido2CredentialEntity>
{
    public void Configure(EntityTypeBuilder<Fido2CredentialEntity> builder)
    {
        builder.ToTable("Fido2Credentials");
        builder.HasKey(c => c.Id);

        builder.Property(c => c.Id).HasMaxLength(64);
        builder.Property(c => c.TenantId).HasMaxLength(128);
        builder.Property(c => c.UserId).IsRequired().HasMaxLength(200);
        builder.Property(c => c.CredentialId).IsRequired().HasMaxLength(1024);
        builder.Property(c => c.PublicKey).IsRequired();
        builder.Property(c => c.UserHandle).IsRequired().HasMaxLength(200);
        builder.Property(c => c.DisplayName).HasMaxLength(200);
        builder.Property(c => c.AttestationFormat).HasMaxLength(50);
        builder.Property(c => c.Transports).HasMaxLength(200);

        // Credential ID must be unique per tenant
        builder.HasIndex(c => new { c.TenantId, c.CredentialId }).IsUnique();

        // Query by user
        builder.HasIndex(c => new { c.TenantId, c.UserId });

        // Query active credentials
        builder.HasIndex(c => new { c.TenantId, c.UserId, c.IsActive });
    }
}
