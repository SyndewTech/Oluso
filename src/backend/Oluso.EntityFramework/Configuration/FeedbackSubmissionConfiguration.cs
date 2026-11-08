using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Oluso.Core.Domain.Entities;

namespace Oluso.EntityFramework.Configuration;

public class FeedbackSubmissionConfiguration : IEntityTypeConfiguration<FeedbackSubmission>
{
    public void Configure(EntityTypeBuilder<FeedbackSubmission> builder)
    {
        builder.ToTable("FeedbackSubmissions");

        builder.HasKey(x => x.Id);

        builder.Property(x => x.Title)
            .IsRequired()
            .HasMaxLength(200);

        builder.Property(x => x.Description)
            .IsRequired();

        builder.Property(x => x.Category)
            .IsRequired()
            .HasMaxLength(20);

        builder.Property(x => x.Url)
            .HasMaxLength(2000);

        builder.Property(x => x.ScreenshotPath)
            .HasMaxLength(500);

        builder.Property(x => x.ElementSelector)
            .HasMaxLength(500);

        builder.Property(x => x.UserAgent)
            .HasMaxLength(500);

        builder.Property(x => x.ViewportSize)
            .HasMaxLength(20);

        builder.Property(x => x.Status)
            .IsRequired()
            .HasMaxLength(20);

        builder.Property(x => x.SubmittedByUserId)
            .IsRequired()
            .HasMaxLength(200);

        builder.Property(x => x.SubmittedByEmail)
            .HasMaxLength(256);

        builder.Property(x => x.SubmittedByName)
            .HasMaxLength(200);

        builder.Property(x => x.Portal)
            .IsRequired()
            .HasMaxLength(20);

        // Indexes
        builder.HasIndex(x => new { x.TenantId, x.CreatedAt });
        builder.HasIndex(x => new { x.TenantId, x.Category });
        builder.HasIndex(x => new { x.TenantId, x.Status });
    }
}
