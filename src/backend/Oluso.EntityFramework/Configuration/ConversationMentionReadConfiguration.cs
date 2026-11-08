using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Oluso.Core.Domain.Entities;

namespace Oluso.EntityFramework.Configuration;

public class ConversationMentionReadConfiguration : IEntityTypeConfiguration<ConversationMentionRead>
{
    public void Configure(EntityTypeBuilder<ConversationMentionRead> builder)
    {
        builder.ToTable("ConversationMentionReads");
        builder.HasKey(x => x.Id);

        builder.Property(x => x.Id).HasMaxLength(36).IsRequired();
        builder.Property(x => x.TenantId).HasMaxLength(36);
        builder.Property(x => x.MessageId).IsRequired().HasMaxLength(36);
        builder.Property(x => x.UserId).IsRequired().HasMaxLength(256);

        // Uniqueness — a user only acks a given mention once. The anti-join
        // for "unread mentions for me" rides on this index.
        builder.HasIndex(x => new { x.TenantId, x.UserId, x.MessageId })
            .IsUnique()
            .HasDatabaseName("IX_ConversationMentionReads_User_Message");
    }
}
