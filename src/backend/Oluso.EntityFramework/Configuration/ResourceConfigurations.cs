using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Oluso.Core.Domain.Entities;

namespace Oluso.EntityFramework.Configuration;

/// <summary>
/// Entity Framework configuration for Resource (RFC 8707)
/// </summary>
public class ResourceConfiguration : IEntityTypeConfiguration<Resource>
{
    public void Configure(EntityTypeBuilder<Resource> builder)
    {
        builder.ToTable("Resources");
        builder.HasKey(r => r.Id);

        builder.Property(r => r.TenantId).HasMaxLength(128);
        builder.Property(r => r.Uri).IsRequired().HasMaxLength(2000);
        builder.Property(r => r.DisplayName).HasMaxLength(200);
        builder.Property(r => r.Description).HasMaxLength(1000);

        // Unique index on URI within tenant (resources are identified by URI per RFC 8707)
        builder.HasIndex(r => new { r.TenantId, r.Uri }).IsUnique();

        builder.HasMany(r => r.AllowedScopes).WithOne(s => s.Resource).HasForeignKey(s => s.ResourceId).OnDelete(DeleteBehavior.Cascade);
        builder.HasMany(r => r.UserClaims).WithOne(c => c.Resource).HasForeignKey(c => c.ResourceId).OnDelete(DeleteBehavior.Cascade);
    }
}

public class ApiScopeConfiguration : IEntityTypeConfiguration<ApiScope>
{
    public void Configure(EntityTypeBuilder<ApiScope> builder)
    {
        builder.ToTable("ApiScopes");
        builder.HasKey(s => s.Id);

        builder.Property(s => s.TenantId).HasMaxLength(128);
        builder.Property(s => s.Name).IsRequired().HasMaxLength(200);
        builder.Property(s => s.DisplayName).HasMaxLength(200);
        builder.Property(s => s.Description).HasMaxLength(1000);

        builder.HasIndex(s => new { s.TenantId, s.Name }).IsUnique();

        builder.HasMany(s => s.UserClaims).WithOne(c => c.Scope).HasForeignKey(c => c.ScopeId).OnDelete(DeleteBehavior.Cascade);
        builder.HasMany(s => s.Properties).WithOne(p => p.Scope).HasForeignKey(p => p.ScopeId).OnDelete(DeleteBehavior.Cascade);
    }
}

public class IdentityResourceConfiguration : IEntityTypeConfiguration<IdentityResource>
{
    public void Configure(EntityTypeBuilder<IdentityResource> builder)
    {
        builder.ToTable("IdentityResources");
        builder.HasKey(r => r.Id);

        builder.Property(r => r.TenantId).HasMaxLength(128);
        builder.Property(r => r.Name).IsRequired().HasMaxLength(200);
        builder.Property(r => r.DisplayName).HasMaxLength(200);
        builder.Property(r => r.Description).HasMaxLength(1000);

        builder.HasIndex(r => new { r.TenantId, r.Name }).IsUnique();

        builder.HasMany(r => r.UserClaims).WithOne(c => c.IdentityResource).HasForeignKey(c => c.IdentityResourceId).OnDelete(DeleteBehavior.Cascade);
        builder.HasMany(r => r.Properties).WithOne(p => p.IdentityResource).HasForeignKey(p => p.IdentityResourceId).OnDelete(DeleteBehavior.Cascade);
    }
}
