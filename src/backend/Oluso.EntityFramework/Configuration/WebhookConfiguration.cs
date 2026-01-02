using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Oluso.Core.Domain.Entities;

namespace Oluso.EntityFramework.Configuration;

public class WebhookEndpointConfiguration : IEntityTypeConfiguration<WebhookEndpointEntity>
{
    public void Configure(EntityTypeBuilder<WebhookEndpointEntity> builder)
    {
        builder.ToTable("WebhookEndpoints");

        builder.HasKey(x => x.Id);

        builder.Property(x => x.Id)
            .HasMaxLength(128);

        builder.Property(x => x.TenantId)
            .HasMaxLength(128);

        builder.Property(x => x.Name)
            .IsRequired()
            .HasMaxLength(200);

        builder.Property(x => x.Description)
            .HasMaxLength(500);

        builder.Property(x => x.Url)
            .IsRequired()
            .HasMaxLength(2000);

        builder.Property(x => x.SecretHash)
            .IsRequired()
            .HasMaxLength(256);

        builder.Property(x => x.ApiVersion)
            .HasMaxLength(20);

        builder.Property(x => x.HeadersJson)
            .HasMaxLength(4000);

        // Indexes
        builder.HasIndex(x => x.TenantId);
        builder.HasIndex(x => new { x.TenantId, x.Enabled });

        // Relationships
        builder.HasMany(x => x.EventSubscriptions)
            .WithOne(x => x.Endpoint)
            .HasForeignKey(x => x.EndpointId)
            .OnDelete(DeleteBehavior.Cascade);

        builder.HasMany(x => x.Deliveries)
            .WithOne(x => x.Endpoint)
            .HasForeignKey(x => x.EndpointId)
            .OnDelete(DeleteBehavior.Cascade);
    }
}

public class WebhookEventSubscriptionConfiguration : IEntityTypeConfiguration<WebhookEventSubscriptionEntity>
{
    public void Configure(EntityTypeBuilder<WebhookEventSubscriptionEntity> builder)
    {
        builder.ToTable("WebhookEventSubscriptions");

        builder.HasKey(x => x.Id);

        builder.Property(x => x.EndpointId)
            .IsRequired()
            .HasMaxLength(128);

        builder.Property(x => x.EventType)
            .IsRequired()
            .HasMaxLength(100);

        // Indexes
        builder.HasIndex(x => x.EndpointId);
        builder.HasIndex(x => new { x.EndpointId, x.EventType, x.Enabled });
    }
}

public class WebhookDeliveryConfiguration : IEntityTypeConfiguration<WebhookDeliveryEntity>
{
    public void Configure(EntityTypeBuilder<WebhookDeliveryEntity> builder)
    {
        builder.ToTable("WebhookDeliveries");

        builder.HasKey(x => x.Id);

        builder.Property(x => x.Id)
            .HasMaxLength(128);

        builder.Property(x => x.TenantId)
            .HasMaxLength(128);

        builder.Property(x => x.EndpointId)
            .IsRequired()
            .HasMaxLength(128);

        builder.Property(x => x.EventType)
            .IsRequired()
            .HasMaxLength(100);

        builder.Property(x => x.PayloadId)
            .IsRequired()
            .HasMaxLength(128);

        builder.Property(x => x.Payload)
            .IsRequired();
        // No max length - can be large JSON

        builder.Property(x => x.ResponseBody)
            .HasMaxLength(2000);

        builder.Property(x => x.ErrorMessage)
            .HasMaxLength(1000);

        // Indexes
        builder.HasIndex(x => x.TenantId);
        builder.HasIndex(x => x.EndpointId);
        builder.HasIndex(x => x.CreatedAt);
        builder.HasIndex(x => new { x.Status, x.NextRetryAt });
        builder.HasIndex(x => new { x.EndpointId, x.CreatedAt });
    }
}
