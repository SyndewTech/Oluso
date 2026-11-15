using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Oluso.Core.Domain.Entities;

namespace Oluso.EntityFramework.Configuration;

public class AccessTokenRevocationConfiguration : IEntityTypeConfiguration<AccessTokenRevocation>
{
    public void Configure(EntityTypeBuilder<AccessTokenRevocation> builder)
    {
        builder.ToTable("AccessTokenRevocations");
        builder.HasKey(x => x.Id);

        builder.Property(x => x.Id).HasMaxLength(36).IsRequired();
        builder.Property(x => x.TenantId).HasMaxLength(36);
        builder.Property(x => x.ClientId).IsRequired().HasMaxLength(200);
        builder.Property(x => x.SessionId).HasMaxLength(100);
        builder.Property(x => x.TokenId).HasMaxLength(100);

        // Looked up on every request that presents an access token.
        builder.HasIndex(x => new { x.ClientId, x.SessionId });
        builder.HasIndex(x => new { x.ClientId, x.TokenId });
        builder.HasIndex(x => x.ExpiresAt);
    }
}
