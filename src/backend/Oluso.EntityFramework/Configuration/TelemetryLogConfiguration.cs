using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Oluso.Core.Domain.Interfaces;

namespace Oluso.EntityFramework.Configuration;

public class TelemetryLogConfiguration : IEntityTypeConfiguration<TelemetryLog>
{
    public void Configure(EntityTypeBuilder<TelemetryLog> builder)
    {
        builder.ToTable("TelemetryLogs");

        builder.HasKey(x => x.Id);

        builder.Property(x => x.Timestamp)
            .IsRequired();

        builder.Property(x => x.Level)
            .IsRequired()
            .HasMaxLength(20);

        builder.Property(x => x.Message)
            .IsRequired()
            .HasMaxLength(4000);

        builder.Property(x => x.Category)
            .IsRequired()
            .HasMaxLength(500);

        builder.Property(x => x.Exception)
            .HasMaxLength(4000);

        builder.Property(x => x.ExceptionType)
            .HasMaxLength(500);

        builder.Property(x => x.StackTrace);
        // No max length - can be long

        builder.Property(x => x.TraceId)
            .HasMaxLength(128);

        builder.Property(x => x.SpanId)
            .HasMaxLength(64);

        builder.Property(x => x.RequestPath)
            .HasMaxLength(2000);

        builder.Property(x => x.RequestMethod)
            .HasMaxLength(10);

        builder.Property(x => x.UserId)
            .HasMaxLength(200);

        builder.Property(x => x.ClientId)
            .HasMaxLength(200);

        builder.Property(x => x.MachineName)
            .HasMaxLength(200);

        builder.Property(x => x.Properties);
        // No max length - JSON

        // Indexes for common queries
        builder.HasIndex(x => x.Timestamp);
        builder.HasIndex(x => x.Level);
        builder.HasIndex(x => x.Category);
        builder.HasIndex(x => x.TraceId);
        builder.HasIndex(x => x.UserId);
        builder.HasIndex(x => x.ClientId);

        // Tenant isolation index
        builder.HasIndex(x => x.TenantId);
        builder.HasIndex(x => new { x.TenantId, x.Timestamp });
        builder.HasIndex(x => new { x.TenantId, x.Level, x.Timestamp });
    }
}
