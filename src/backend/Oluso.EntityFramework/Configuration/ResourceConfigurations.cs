using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Oluso.Core.Domain.Entities;

namespace Oluso.EntityFramework.Configuration;

public class ApiResourceConfiguration : IEntityTypeConfiguration<ApiResource>
{
    public void Configure(EntityTypeBuilder<ApiResource> builder)
    {
        builder.ToTable("ApiResources");
        builder.HasKey(r => r.Id);

        builder.Property(r => r.TenantId).HasMaxLength(128);
        builder.Property(r => r.Name).IsRequired().HasMaxLength(200);
        builder.Property(r => r.DisplayName).HasMaxLength(200);
        builder.Property(r => r.Description).HasMaxLength(1000);
        builder.Property(r => r.AllowedAccessTokenSigningAlgorithms).HasMaxLength(100);

        builder.HasIndex(r => new { r.TenantId, r.Name }).IsUnique();

        builder.HasMany(r => r.Secrets).WithOne(s => s.ApiResource).HasForeignKey(s => s.ApiResourceId).OnDelete(DeleteBehavior.Cascade);
        builder.HasMany(r => r.Scopes).WithOne(s => s.ApiResource).HasForeignKey(s => s.ApiResourceId).OnDelete(DeleteBehavior.Cascade);
        builder.HasMany(r => r.UserClaims).WithOne(c => c.ApiResource).HasForeignKey(c => c.ApiResourceId).OnDelete(DeleteBehavior.Cascade);
        builder.HasMany(r => r.Properties).WithOne(p => p.ApiResource).HasForeignKey(p => p.ApiResourceId).OnDelete(DeleteBehavior.Cascade);
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

