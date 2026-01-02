using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Oluso.Core.Domain.Entities;

namespace Oluso.EntityFramework.Configuration;

public class ClientConfiguration : IEntityTypeConfiguration<Client>
{
    public void Configure(EntityTypeBuilder<Client> builder)
    {
        builder.ToTable("Clients");
        builder.HasKey(c => c.Id);

        builder.Property(c => c.TenantId).HasMaxLength(128);
        builder.Property(c => c.ClientId).IsRequired().HasMaxLength(200);
        builder.Property(c => c.ProtocolType).IsRequired().HasMaxLength(200);
        builder.Property(c => c.ClientName).HasMaxLength(200);
        builder.Property(c => c.Description).HasMaxLength(1000);
        builder.Property(c => c.ClientUri).HasMaxLength(2000);
        builder.Property(c => c.LogoUri).HasMaxLength(2000);
        builder.Property(c => c.FrontChannelLogoutUri).HasMaxLength(2000);
        builder.Property(c => c.BackChannelLogoutUri).HasMaxLength(2000);
        builder.Property(c => c.ClientClaimsPrefix).HasMaxLength(200);
        builder.Property(c => c.PairWiseSubjectSalt).HasMaxLength(200);
        builder.Property(c => c.UserCodeType).HasMaxLength(100);
        builder.Property(c => c.AllowedIdentityTokenSigningAlgorithms).HasMaxLength(100);

        // Composite unique index on TenantId + ClientId
        builder.HasIndex(c => new { c.TenantId, c.ClientId }).IsUnique();

        builder.HasMany(c => c.ClientSecrets).WithOne(s => s.Client).HasForeignKey(s => s.ClientId).OnDelete(DeleteBehavior.Cascade);
        builder.HasMany(c => c.AllowedGrantTypes).WithOne(g => g.Client).HasForeignKey(g => g.ClientId).OnDelete(DeleteBehavior.Cascade);
        builder.HasMany(c => c.RedirectUris).WithOne(r => r.Client).HasForeignKey(r => r.ClientId).OnDelete(DeleteBehavior.Cascade);
        builder.HasMany(c => c.PostLogoutRedirectUris).WithOne(p => p.Client).HasForeignKey(p => p.ClientId).OnDelete(DeleteBehavior.Cascade);
        builder.HasMany(c => c.AllowedScopes).WithOne(s => s.Client).HasForeignKey(s => s.ClientId).OnDelete(DeleteBehavior.Cascade);
        builder.HasMany(c => c.Claims).WithOne(cl => cl.Client).HasForeignKey(cl => cl.ClientId).OnDelete(DeleteBehavior.Cascade);
        builder.HasMany(c => c.AllowedCorsOrigins).WithOne(o => o.Client).HasForeignKey(o => o.ClientId).OnDelete(DeleteBehavior.Cascade);
        builder.HasMany(c => c.Properties).WithOne(p => p.Client).HasForeignKey(p => p.ClientId).OnDelete(DeleteBehavior.Cascade);
        builder.HasMany(c => c.IdentityProviderRestrictions).WithOne(r => r.Client).HasForeignKey(r => r.ClientId).OnDelete(DeleteBehavior.Cascade);
        builder.HasMany(c => c.AllowedRoles).WithOne(r => r.Client).HasForeignKey(r => r.ClientId).OnDelete(DeleteBehavior.Cascade);
        builder.HasMany(c => c.AllowedUsers).WithOne(u => u.Client).HasForeignKey(u => u.ClientId).OnDelete(DeleteBehavior.Cascade);
    }
}

public class ClientSecretConfiguration : IEntityTypeConfiguration<ClientSecret>
{
    public void Configure(EntityTypeBuilder<ClientSecret> builder)
    {
        builder.ToTable("ClientSecrets");
        builder.HasKey(s => s.Id);
        builder.Property(s => s.Value).IsRequired().HasMaxLength(4000);
        builder.Property(s => s.Type).IsRequired().HasMaxLength(250);
        builder.Property(s => s.Description).HasMaxLength(2000);
    }
}

public class ClientGrantTypeConfiguration : IEntityTypeConfiguration<ClientGrantType>
{
    public void Configure(EntityTypeBuilder<ClientGrantType> builder)
    {
        builder.ToTable("ClientGrantTypes");
        builder.HasKey(g => g.Id);
        builder.Property(g => g.GrantType).IsRequired().HasMaxLength(250);
    }
}

public class ClientScopeConfiguration : IEntityTypeConfiguration<ClientScope>
{
    public void Configure(EntityTypeBuilder<ClientScope> builder)
    {
        builder.ToTable("ClientScopes");
        builder.HasKey(s => s.Id);
        builder.Property(s => s.Scope).IsRequired().HasMaxLength(200);
    }
}
