using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Oluso.Core.Domain.Entities;

namespace Oluso.EntityFramework.Configuration;

public class FileAttachmentConfiguration : IEntityTypeConfiguration<FileAttachment>
{
    public void Configure(EntityTypeBuilder<FileAttachment> builder)
    {
        builder.ToTable("FileAttachments");
        builder.HasKey(x => x.Id);

        builder.Property(x => x.UploadedByUserId).IsRequired().HasMaxLength(200);
        builder.Property(x => x.UploadedByUserName).HasMaxLength(200);

        builder.Property(x => x.StoragePath).IsRequired().HasMaxLength(1000);
        builder.Property(x => x.FileName).IsRequired().HasMaxLength(500);
        builder.Property(x => x.ContentType).IsRequired().HasMaxLength(200);
        builder.Property(x => x.ContentHash).HasMaxLength(128);

        builder.Property(x => x.Folder).HasMaxLength(200);

        builder.Property(x => x.EntityType).HasMaxLength(100);
        builder.Property(x => x.EntityId).HasMaxLength(200);
        builder.Property(x => x.SubEntityType).HasMaxLength(100);
        builder.Property(x => x.SubEntityId).HasMaxLength(200);

        // Entity-reference protection columns (opt-in per provider)
        builder.Property(x => x.EntityIdHash).HasMaxLength(128);
        builder.Property(x => x.SubEntityIdHash).HasMaxLength(128);
        builder.Property(x => x.EntityIdProtected).HasMaxLength(4000);
        builder.Property(x => x.SubEntityIdProtected).HasMaxLength(4000);
        builder.Property(x => x.FileNameProtected).HasMaxLength(4000);
        builder.Property(x => x.ProtectionPurpose).HasMaxLength(64);

        // Primary: get all attachments for an entity (plaintext path)
        builder.HasIndex(x => new { x.TenantId, x.EntityType, x.EntityId })
            .HasFilter(null);

        // Protected path: lookup by HMAC blind index — separate index so the
        // query planner doesn't have to scan rows where EntityIdHash is null.
        builder.HasIndex(x => new { x.TenantId, x.EntityType, x.EntityIdHash });

        // Personal files: user's own documents
        builder.HasIndex(x => new { x.TenantId, x.UploadedByUserId, x.Folder });

        // Soft delete filter
        builder.HasQueryFilter(x => !x.IsDeleted);
    }
}
