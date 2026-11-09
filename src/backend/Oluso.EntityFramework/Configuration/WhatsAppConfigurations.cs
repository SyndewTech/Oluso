using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Oluso.Core.Domain.Entities;

namespace Oluso.EntityFramework.Configuration;

public class WhatsAppContactConfiguration : IEntityTypeConfiguration<WhatsAppContact>
{
    public void Configure(EntityTypeBuilder<WhatsAppContact> builder)
    {
        builder.ToTable("WhatsAppContacts");
        builder.HasKey(c => c.Id);
        builder.Property(c => c.Id).IsRequired().HasMaxLength(64);
        builder.Property(c => c.TenantId).IsRequired().HasMaxLength(128);
        builder.Property(c => c.PhoneNumber).IsRequired().HasMaxLength(20);
        builder.Property(c => c.DisplayName).HasMaxLength(200);
        builder.Property(c => c.ConsentStatus).HasConversion<string>().HasMaxLength(16);
        builder.Property(c => c.ConsentSource).HasMaxLength(40);
        builder.Property(c => c.LastInboundMessageId).HasMaxLength(128);

        builder.HasIndex(c => new { c.TenantId, c.PhoneNumber }).IsUnique();
        builder.HasIndex(c => new { c.TenantId, c.ConsentStatus });
    }
}

public class WhatsAppTemplateConfiguration : IEntityTypeConfiguration<WhatsAppTemplate>
{
    public void Configure(EntityTypeBuilder<WhatsAppTemplate> builder)
    {
        builder.ToTable("WhatsAppTemplates");
        builder.HasKey(t => t.Id);
        builder.Property(t => t.Id).IsRequired().HasMaxLength(64);
        builder.Property(t => t.TenantId).IsRequired().HasMaxLength(128);
        builder.Property(t => t.ProviderTemplateId).IsRequired().HasMaxLength(64);
        builder.Property(t => t.Name).IsRequired().HasMaxLength(512);
        builder.Property(t => t.Language).IsRequired().HasMaxLength(16);
        builder.Property(t => t.Category).HasMaxLength(32);
        builder.Property(t => t.Status).IsRequired().HasMaxLength(32);
        builder.Property(t => t.BodyText).HasMaxLength(2048);
        builder.Property(t => t.HeaderFormat).HasMaxLength(16);

        builder.HasIndex(t => new { t.TenantId, t.Name, t.Language }).IsUnique();
    }
}

public class WhatsAppInboundMessageConfiguration : IEntityTypeConfiguration<WhatsAppInboundMessage>
{
    public void Configure(EntityTypeBuilder<WhatsAppInboundMessage> builder)
    {
        builder.ToTable("WhatsAppInboundMessages");
        builder.HasKey(m => m.Id);
        builder.Property(m => m.Id).IsRequired().HasMaxLength(64);
        builder.Property(m => m.TenantId).IsRequired().HasMaxLength(128);
        builder.Property(m => m.ProviderMessageId).IsRequired().HasMaxLength(128);
        builder.Property(m => m.ContactId).IsRequired().HasMaxLength(64);
        builder.Property(m => m.From).IsRequired().HasMaxLength(20);
        builder.Property(m => m.MessageType).IsRequired().HasMaxLength(32);
        builder.Property(m => m.Text).HasMaxLength(4096);
        builder.Property(m => m.Keyword).HasMaxLength(32);
        builder.Property(m => m.InReplyToMessageId).HasMaxLength(128);

        builder.HasIndex(m => new { m.TenantId, m.ProviderMessageId }).IsUnique();
        builder.HasIndex(m => new { m.TenantId, m.ContactId, m.ReceivedAt });
    }
}

public class WhatsAppAccountConfiguration : IEntityTypeConfiguration<WhatsAppAccount>
{
    public void Configure(EntityTypeBuilder<WhatsAppAccount> builder)
    {
        builder.ToTable("WhatsAppAccounts");
        builder.HasKey(a => a.Id);
        builder.Property(a => a.Id).IsRequired().HasMaxLength(64);
        builder.Property(a => a.TenantId).IsRequired().HasMaxLength(128);
        builder.Property(a => a.PhoneNumberId).IsRequired().HasMaxLength(64);
        builder.Property(a => a.BusinessAccountId).HasMaxLength(64);
        builder.Property(a => a.DisplayPhoneNumber).HasMaxLength(32);
        builder.Property(a => a.VerifiedName).HasMaxLength(200);
        builder.Property(a => a.QualityRating).HasMaxLength(32);

        builder.HasIndex(a => a.PhoneNumberId).IsUnique();
        builder.HasIndex(a => a.TenantId).IsUnique();
    }
}
