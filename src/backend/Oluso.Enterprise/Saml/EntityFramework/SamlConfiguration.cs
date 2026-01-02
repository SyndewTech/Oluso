using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Oluso.Enterprise.Saml.Entities;

namespace Oluso.Enterprise.Saml.EntityFramework;

public class SamlServiceProviderConfiguration : IEntityTypeConfiguration<SamlServiceProvider>
{
    public void Configure(EntityTypeBuilder<SamlServiceProvider> builder)
    {
        builder.ToTable("SamlServiceProviders");

        builder.HasKey(x => x.Id);

        builder.Property(x => x.TenantId).HasMaxLength(128).IsRequired();
        builder.Property(x => x.EntityId).HasMaxLength(2000).IsRequired();
        builder.Property(x => x.DisplayName).HasMaxLength(200);
        builder.Property(x => x.Description).HasMaxLength(1000);
        builder.Property(x => x.MetadataUrl).HasMaxLength(2000);
        builder.Property(x => x.AssertionConsumerServiceUrl).HasMaxLength(2000);
        builder.Property(x => x.SingleLogoutServiceUrl).HasMaxLength(2000);
        builder.Property(x => x.NameIdFormat).HasMaxLength(200);
        builder.Property(x => x.SsoBinding).HasMaxLength(50);
        builder.Property(x => x.DefaultRelayState).HasMaxLength(2000);

        // Indexes
        builder.HasIndex(x => x.TenantId);
        builder.HasIndex(x => new { x.TenantId, x.EntityId }).IsUnique();
    }
}
