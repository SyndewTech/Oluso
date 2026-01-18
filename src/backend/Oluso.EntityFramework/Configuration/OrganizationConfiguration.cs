using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Oluso.Core.Domain.Entities;

namespace Oluso.EntityFramework.Configuration;

public class OrganizationConfiguration : IEntityTypeConfiguration<Organization>
{
    public void Configure(EntityTypeBuilder<Organization> builder)
    {
        builder.ToTable("Organizations");
        builder.HasKey(o => o.Id);

        builder.Property(o => o.Id).HasMaxLength(128);
        builder.Property(o => o.Name).IsRequired().HasMaxLength(200);
        builder.Property(o => o.Slug).IsRequired().HasMaxLength(100);
        builder.Property(o => o.Description).HasMaxLength(1000);
        builder.Property(o => o.LogoUrl).HasMaxLength(2000);
        builder.Property(o => o.WebsiteUrl).HasMaxLength(2000);
        builder.Property(o => o.BillingCustomerId).HasMaxLength(200);
        builder.Property(o => o.PlanId).HasMaxLength(100);
        builder.Property(o => o.Metadata);

        // Unique slug
        builder.HasIndex(o => o.Slug).IsUnique();

        // Index for enabled organizations
        builder.HasIndex(o => o.Enabled);

        // Relationship to Tenants
        builder.HasMany(o => o.Tenants)
            .WithOne(t => t.Organization)
            .HasForeignKey(t => t.OrganizationId)
            .OnDelete(DeleteBehavior.Restrict);

        // Relationship to Memberships
        builder.HasMany(o => o.Memberships)
            .WithOne(m => m.Organization)
            .HasForeignKey(m => m.OrganizationId)
            .OnDelete(DeleteBehavior.Cascade);

        // Relationship to Invitations
        builder.HasMany(o => o.Invitations)
            .WithOne(i => i.Organization)
            .HasForeignKey(i => i.OrganizationId)
            .OnDelete(DeleteBehavior.Cascade);
    }
}

public class OrganizationMembershipConfiguration : IEntityTypeConfiguration<OrganizationMembership>
{
    public void Configure(EntityTypeBuilder<OrganizationMembership> builder)
    {
        builder.ToTable("OrganizationMemberships");
        builder.HasKey(m => m.Id);

        builder.Property(m => m.Id).HasMaxLength(128);
        builder.Property(m => m.OrganizationId).IsRequired().HasMaxLength(128);
        builder.Property(m => m.UserId).IsRequired().HasMaxLength(128);
        builder.Property(m => m.AllowedTenantIdsJson).HasMaxLength(4000);
        builder.Property(m => m.InvitedByUserId).HasMaxLength(128);
        builder.Property(m => m.InvitationId).HasMaxLength(128);

        // Role stored as int
        builder.Property(m => m.Role)
            .HasConversion<int>();

        // Unique constraint: user can only be member of an org once
        builder.HasIndex(m => new { m.OrganizationId, m.UserId }).IsUnique();

        // Index for querying user's organizations
        builder.HasIndex(m => m.UserId);

        // Index for role queries
        builder.HasIndex(m => new { m.OrganizationId, m.Role });
    }
}

public class OrganizationInvitationConfiguration : IEntityTypeConfiguration<OrganizationInvitation>
{
    public void Configure(EntityTypeBuilder<OrganizationInvitation> builder)
    {
        builder.ToTable("OrganizationInvitations");
        builder.HasKey(i => i.Id);

        builder.Property(i => i.Id).HasMaxLength(128);
        builder.Property(i => i.OrganizationId).IsRequired().HasMaxLength(128);
        builder.Property(i => i.Email).IsRequired().HasMaxLength(256);
        builder.Property(i => i.AllowedTenantIdsJson).HasMaxLength(4000);
        builder.Property(i => i.Token).IsRequired().HasMaxLength(256);
        builder.Property(i => i.InvitedByUserId).IsRequired().HasMaxLength(128);
        builder.Property(i => i.AcceptedByUserId).HasMaxLength(128);
        builder.Property(i => i.Message).HasMaxLength(1000);

        // Role and Status stored as int
        builder.Property(i => i.Role)
            .HasConversion<int>();

        builder.Property(i => i.Status)
            .HasConversion<int>();

        // Unique token for invitation links
        builder.HasIndex(i => i.Token).IsUnique();

        // Query pending invitations for an email
        builder.HasIndex(i => new { i.Email, i.Status });

        // Query invitations by organization
        builder.HasIndex(i => new { i.OrganizationId, i.Status });

        // Cleanup expired invitations
        builder.HasIndex(i => i.ExpiresAt);
    }
}
