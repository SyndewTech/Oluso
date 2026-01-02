using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Oluso.Core.Domain.Interfaces;

namespace Oluso.EntityFramework.Configuration;

public class AuditLogConfiguration : IEntityTypeConfiguration<AuditLog>
{
    public void Configure(EntityTypeBuilder<AuditLog> builder)
    {
        builder.ToTable("AuditLogs");

        builder.HasKey(x => x.Id);

        builder.Property(x => x.Timestamp)
            .IsRequired();

        builder.Property(x => x.EventType)
            .IsRequired()
            .HasMaxLength(100);

        builder.Property(x => x.Category)
            .IsRequired()
            .HasMaxLength(50);

        builder.Property(x => x.Action)
            .HasMaxLength(100);

        builder.Property(x => x.SubjectId)
            .HasMaxLength(200);

        builder.Property(x => x.SubjectName)
            .HasMaxLength(200);

        builder.Property(x => x.SubjectEmail)
            .HasMaxLength(256);

        builder.Property(x => x.ResourceType)
            .HasMaxLength(100);

        builder.Property(x => x.ResourceId)
            .HasMaxLength(200);

        builder.Property(x => x.ResourceName)
            .HasMaxLength(200);

        builder.Property(x => x.ClientId)
            .HasMaxLength(200);

        builder.Property(x => x.IpAddress)
            .HasMaxLength(45); // IPv6 max length

        builder.Property(x => x.UserAgent)
            .HasMaxLength(500);

        builder.Property(x => x.ErrorMessage)
            .HasMaxLength(2000);

        builder.Property(x => x.Details);
        // No max length - can store JSON

        builder.Property(x => x.Reason)
            .HasMaxLength(500);

        builder.Property(x => x.ActivityId)
            .HasMaxLength(128);

        // Indexes for common queries
        builder.HasIndex(x => x.Timestamp);
        builder.HasIndex(x => x.Category);
        builder.HasIndex(x => x.EventType);
        builder.HasIndex(x => x.SubjectId);
        builder.HasIndex(x => new { x.ResourceType, x.ResourceId });
        builder.HasIndex(x => x.ClientId);
        builder.HasIndex(x => x.ActivityId);

        // Tenant isolation index
        builder.HasIndex(x => x.TenantId);
        builder.HasIndex(x => new { x.TenantId, x.Timestamp });
    }
}
