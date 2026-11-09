using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Oluso.Core.Domain.Entities;

namespace Oluso.EntityFramework.Configuration;

public class TeamConfiguration : IEntityTypeConfiguration<Team>
{
    public void Configure(EntityTypeBuilder<Team> builder)
    {
        builder.ToTable("Teams");
        builder.HasKey(e => e.Id);

        builder.Property(e => e.Id).HasMaxLength(36).IsRequired();
        builder.Property(e => e.TenantId).HasMaxLength(36).IsRequired();
        builder.Property(e => e.Code).HasMaxLength(50).IsRequired();
        builder.Property(e => e.Name).HasMaxLength(200).IsRequired();
        builder.Property(e => e.Description).HasMaxLength(2000);
        builder.Property(e => e.Type).HasConversion<string>().HasMaxLength(20).IsRequired();
        builder.Property(e => e.ParentTeamId).HasMaxLength(36);
        builder.Property(e => e.OwnerUserId).HasMaxLength(450); // matches IdentityUser key length
        builder.Property(e => e.Visibility).HasConversion<string>().HasMaxLength(20).IsRequired();
        builder.Property(e => e.CreatedBy).HasMaxLength(256);
        builder.Property(e => e.UpdatedBy).HasMaxLength(256);
        builder.Property(e => e.ManagedBy).HasMaxLength(64);
        builder.Ignore(e => e.IsManaged);

        // Code is the stable user-facing identifier — unique per tenant.
        builder.HasIndex(e => new { e.TenantId, e.Code })
            .IsUnique()
            .HasDatabaseName("IX_Teams_TenantId_Code");

        builder.HasIndex(e => new { e.TenantId, e.Type })
            .HasDatabaseName("IX_Teams_TenantId_Type");

        builder.HasIndex(e => new { e.TenantId, e.ParentTeamId })
            .HasDatabaseName("IX_Teams_TenantId_ParentTeamId");

        builder.HasIndex(e => new { e.TenantId, e.IsActive })
            .HasDatabaseName("IX_Teams_TenantId_IsActive");

        // Self-FK for parent. Restrict delete so an accidental parent delete
        // doesn't cascade-wipe the whole subtree; service-layer ArchiveAsync
        // handles cascade soft-archive explicitly.
        builder.HasOne(e => e.ParentTeam)
            .WithMany(p => p.ChildTeams)
            .HasForeignKey(e => e.ParentTeamId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasMany(e => e.Members)
            .WithOne(m => m.Team)
            .HasForeignKey(m => m.TeamId)
            .OnDelete(DeleteBehavior.Cascade);
    }
}

public class TeamMembershipConfiguration : IEntityTypeConfiguration<TeamMembership>
{
    public void Configure(EntityTypeBuilder<TeamMembership> builder)
    {
        builder.ToTable("TeamMemberships");
        builder.HasKey(e => e.Id);

        builder.Property(e => e.Id).HasMaxLength(36).IsRequired();
        builder.Property(e => e.TenantId).HasMaxLength(36).IsRequired();
        builder.Property(e => e.TeamId).HasMaxLength(36).IsRequired();
        builder.Property(e => e.UserId).HasMaxLength(450).IsRequired(); // IdentityUser key length
        builder.Property(e => e.Role).HasConversion<string>().HasMaxLength(20).IsRequired();
        builder.Property(e => e.CreatedBy).HasMaxLength(256);
        builder.Property(e => e.UpdatedBy).HasMaxLength(256);

        // One row per (Tenant, Team, User); rejoin reactivates the existing row
        // rather than inserting a duplicate. Keeps the uniqueness portable
        // across SQLite / Postgres / SqlServer (filtered indexes on bool are
        // provider-specific).
        builder.HasIndex(e => new { e.TenantId, e.TeamId, e.UserId })
            .IsUnique()
            .HasDatabaseName("IX_TeamMemberships_Tenant_Team_User");

        builder.HasIndex(e => new { e.TenantId, e.UserId, e.IsActive })
            .HasDatabaseName("IX_TeamMemberships_Tenant_User_Active");
    }
}
