using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Oluso.Core.Domain.Entities;

namespace Oluso.EntityFramework.Configuration;

public class ConversationMessageConfiguration : IEntityTypeConfiguration<ConversationMessage>
{
    public void Configure(EntityTypeBuilder<ConversationMessage> builder)
    {
        builder.ToTable("ConversationMessages");
        builder.HasKey(x => x.Id);

        builder.Property(x => x.Id).HasMaxLength(36).IsRequired();
        builder.Property(x => x.TenantId).HasMaxLength(36);
        builder.Property(x => x.EntityType).IsRequired().HasMaxLength(100);
        builder.Property(x => x.EntityId).IsRequired().HasMaxLength(200);
        builder.Property(x => x.AuthorUserId).IsRequired().HasMaxLength(256);
        builder.Property(x => x.Body).IsRequired().HasMaxLength(4000);
        builder.Property(x => x.DeletedByUserId).HasMaxLength(256);
        builder.Property(x => x.ClientId).HasMaxLength(200);

        // The thread-list query is the hot path: WHERE TenantId=? AND
        // EntityType=? AND EntityId=? ORDER BY CreatedAt. The covering
        // index keeps it a single seek + range scan.
        builder.HasIndex(x => new { x.TenantId, x.EntityType, x.EntityId, x.CreatedAt })
            .HasDatabaseName("IX_ConversationMessages_Thread");
    }
}
