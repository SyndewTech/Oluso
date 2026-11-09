using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Oluso.Core.Domain.Entities;

namespace Oluso.EntityFramework.Configuration;

public class UserNotificationProfileConfiguration : IEntityTypeConfiguration<UserNotificationProfile>
{
    public void Configure(EntityTypeBuilder<UserNotificationProfile> builder)
    {
        builder.ToTable("UserNotificationProfiles");
        builder.HasKey(x => x.Id);

        builder.Property(x => x.TenantId).IsRequired().HasMaxLength(200);
        builder.Property(x => x.UserId).IsRequired().HasMaxLength(200);
        builder.Property(x => x.SnoozeReason).HasMaxLength(500);
        builder.Property(x => x.TimeZoneId).HasMaxLength(100);
        builder.Property(x => x.MutedEventTypes).HasMaxLength(2000);

        // Uniqueness — one profile row per user per tenant. Lookups by
        // (TenantId, UserId) are the hot path (every notification check
        // resolves the row by these two columns).
        builder.HasIndex(x => new { x.TenantId, x.UserId }).IsUnique();
    }
}
