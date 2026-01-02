using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Oluso.Enterprise.Ldap.Entities;

namespace Oluso.Enterprise.Ldap.EntityFramework;

public class LdapServiceAccountConfiguration : IEntityTypeConfiguration<LdapServiceAccount>
{
    public void Configure(EntityTypeBuilder<LdapServiceAccount> builder)
    {
        builder.ToTable("LdapServiceAccounts");

        builder.HasKey(x => x.Id);

        builder.Property(x => x.Id).HasMaxLength(128);
        builder.Property(x => x.TenantId).HasMaxLength(128).IsRequired();
        builder.Property(x => x.Name).HasMaxLength(200).IsRequired();
        builder.Property(x => x.Description).HasMaxLength(1000);
        builder.Property(x => x.BindDn).HasMaxLength(500).IsRequired();
        builder.Property(x => x.PasswordHash).HasMaxLength(256).IsRequired();
        builder.Property(x => x.AllowedOus).HasMaxLength(2000);
        builder.Property(x => x.AllowedIpRanges).HasMaxLength(2000);

        builder.HasIndex(x => x.TenantId);
        builder.HasIndex(x => x.BindDn).IsUnique();
        builder.HasIndex(x => new { x.TenantId, x.Name }).IsUnique();
    }
}
