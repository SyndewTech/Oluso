using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Oluso.Core.Domain.Entities;

namespace Oluso.EntityFramework.Configuration;

public class ActionTokenConfiguration : IEntityTypeConfiguration<ActionToken>
{
    public void Configure(EntityTypeBuilder<ActionToken> builder)
    {
        builder.ToTable("ActionTokens");
        builder.HasKey(x => x.Id);

        builder.Property(x => x.Id).HasMaxLength(36).IsRequired();
        builder.Property(x => x.TenantId).HasMaxLength(36);
        builder.Property(x => x.UserId).IsRequired().HasMaxLength(256);
        builder.Property(x => x.Action).IsRequired().HasMaxLength(100);
        builder.Property(x => x.SubjectId).IsRequired().HasMaxLength(200);

        // SHA-256 hex = 64 chars. Fixed length keeps the index narrow on
        // SqlServer / Postgres. Unique so a redeem-by-hash lookup is a
        // single index seek.
        builder.Property(x => x.TokenHash).IsRequired().HasMaxLength(64);
        builder.HasIndex(x => x.TokenHash).IsUnique();

        // Covering index for the "find expired and prune" sweep — cheap to
        // add and avoids a full scan when a tenant accumulates a lot of
        // historical tokens.
        builder.HasIndex(x => x.ExpiresAt);
    }
}
