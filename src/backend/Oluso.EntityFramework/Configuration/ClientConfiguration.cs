using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.ChangeTracking;
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

        // Stored as a JSON array, like the other list fields, with an empty list for existing rows
        // on every provider.
        builder.Property(c => c.AllowedResources)
            .HasConversion(
                v => JsonSerializer.Serialize(v, (JsonSerializerOptions?)null),
                v => string.IsNullOrEmpty(v) ? new List<string>() : JsonSerializer.Deserialize<List<string>>(v, (JsonSerializerOptions?)null) ?? new List<string>(),
                new ValueComparer<List<string>>(
                    (a, b) => (a ?? new List<string>()).SequenceEqual(b ?? new List<string>()),
                    v => v.Aggregate(0, (h, s) => HashCode.Combine(h, s.GetHashCode())),
                    v => v.ToList()))
            .IsRequired()
            .HasDefaultValueSql("'[]'");
        builder.Property(c => c.IntrospectionResources)
            .HasConversion(
                v => JsonSerializer.Serialize(v, (JsonSerializerOptions?)null),
                v => string.IsNullOrEmpty(v) ? new List<string>() : JsonSerializer.Deserialize<List<string>>(v, (JsonSerializerOptions?)null) ?? new List<string>(),
                new ValueComparer<List<string>>(
                    (a, b) => (a ?? new List<string>()).SequenceEqual(b ?? new List<string>()),
                    v => v.Aggregate(0, (h, s) => HashCode.Combine(h, s.GetHashCode())),
                    v => v.ToList()))
            .IsRequired()
            .HasDefaultValueSql("'[]'");
        builder.Property(c => c.FrontChannelLogoutUri).HasMaxLength(2000);
        builder.Property(c => c.BackChannelLogoutUri).HasMaxLength(2000);
        builder.Property(c => c.ClientClaimsPrefix).HasMaxLength(200);
        builder.Property(c => c.PairWiseSubjectSalt).HasMaxLength(200);
        builder.Property(c => c.UserCodeType).HasMaxLength(100);
        builder.Property(c => c.AllowedIdentityTokenSigningAlgorithms).HasMaxLength(100);

        // mTLS (RFC 8705)
        builder.Property(c => c.TokenEndpointAuthMethod).HasMaxLength(100);
        builder.Property(c => c.TlsClientAuthSubjectDn).HasMaxLength(1000);
        builder.Property(c => c.TlsClientAuthSanDns).HasMaxLength(500);
        builder.Property(c => c.TlsClientAuthSanUri).HasMaxLength(2000);
        builder.Property(c => c.TlsClientAuthSanEmail).HasMaxLength(500);
        builder.Property(c => c.TlsClientAuthSanIp).HasMaxLength(100);

        // Composite unique index on TenantId + ClientId
        builder.HasIndex(c => new { c.TenantId, c.ClientId }).IsUnique();

        builder.HasMany(c => c.ClientSecrets).WithOne(s => s.Client).HasForeignKey(s => s.ClientId).OnDelete(DeleteBehavior.Cascade);
        builder.HasMany(c => c.AllowedGrantTypes).WithOne(g => g.Client).HasForeignKey(g => g.ClientId).OnDelete(DeleteBehavior.Cascade);
        builder.HasMany(c => c.RedirectUris).WithOne(r => r.Client).HasForeignKey(r => r.ClientId).OnDelete(DeleteBehavior.Cascade);
        builder.HasMany(c => c.PostLogoutRedirectUris).WithOne(p => p.Client).HasForeignKey(p => p.ClientId).OnDelete(DeleteBehavior.Cascade);
        builder.HasMany(c => c.AllowedScopes).WithOne(s => s.Client).HasForeignKey(s => s.ClientId).OnDelete(DeleteBehavior.Cascade);
        builder.HasMany(c => c.Claims).WithOne(cl => cl.Client).HasForeignKey(cl => cl.ClientId).OnDelete(DeleteBehavior.Cascade);
        builder.HasMany(c => c.AllowedAuthorizeParameters).WithOne(p => p.Client).HasForeignKey(p => p.ClientId).OnDelete(DeleteBehavior.Cascade);
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
        builder.Property(s => s.TenantId).HasMaxLength(128);
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
        builder.Property(g => g.TenantId).HasMaxLength(128);
        builder.Property(g => g.GrantType).IsRequired().HasMaxLength(250);
    }
}

public class ClientScopeConfiguration : IEntityTypeConfiguration<ClientScope>
{
    public void Configure(EntityTypeBuilder<ClientScope> builder)
    {
        builder.ToTable("ClientScopes");
        builder.HasKey(s => s.Id);
        builder.Property(s => s.TenantId).HasMaxLength(128);
        builder.Property(s => s.Scope).IsRequired().HasMaxLength(200);
    }
}

public class ClientRedirectUriConfiguration : IEntityTypeConfiguration<ClientRedirectUri>
{
    public void Configure(EntityTypeBuilder<ClientRedirectUri> builder)
    {
        builder.ToTable("ClientRedirectUris");
        builder.HasKey(r => r.Id);
        builder.Property(r => r.TenantId).HasMaxLength(128);
        builder.Property(r => r.RedirectUri).IsRequired().HasMaxLength(2000);
    }
}

public class ClientPostLogoutRedirectUriConfiguration : IEntityTypeConfiguration<ClientPostLogoutRedirectUri>
{
    public void Configure(EntityTypeBuilder<ClientPostLogoutRedirectUri> builder)
    {
        builder.ToTable("ClientPostLogoutRedirectUris");
        builder.HasKey(p => p.Id);
        builder.Property(p => p.TenantId).HasMaxLength(128);
        builder.Property(p => p.PostLogoutRedirectUri).IsRequired().HasMaxLength(2000);
    }
}

public class ClientClaimConfiguration : IEntityTypeConfiguration<ClientClaim>
{
    public void Configure(EntityTypeBuilder<ClientClaim> builder)
    {
        builder.ToTable("ClientClaims");
        builder.HasKey(c => c.Id);
        builder.Property(c => c.TenantId).HasMaxLength(128);
        builder.Property(c => c.Type).IsRequired().HasMaxLength(250);
        builder.Property(c => c.Value).IsRequired().HasMaxLength(250);
    }
}

public class ClientAuthorizeParameterConfiguration : IEntityTypeConfiguration<ClientAuthorizeParameter>
{
    public void Configure(EntityTypeBuilder<ClientAuthorizeParameter> builder)
    {
        builder.ToTable("ClientAuthorizeParameters");
        builder.HasKey(p => p.Id);
        builder.Property(p => p.TenantId).HasMaxLength(128);
        builder.Property(p => p.Parameter).IsRequired().HasMaxLength(100);
        builder.Property(p => p.ClaimType).IsRequired().HasMaxLength(250);
        builder.HasIndex(p => new { p.ClientId, p.Parameter }).IsUnique();
    }
}

public class ClientCorsOriginConfiguration : IEntityTypeConfiguration<ClientCorsOrigin>
{
    public void Configure(EntityTypeBuilder<ClientCorsOrigin> builder)
    {
        builder.ToTable("ClientCorsOrigins");
        builder.HasKey(o => o.Id);
        builder.Property(o => o.TenantId).HasMaxLength(128);
        builder.Property(o => o.Origin).IsRequired().HasMaxLength(150);
    }
}

public class ClientPropertyConfiguration : IEntityTypeConfiguration<ClientProperty>
{
    public void Configure(EntityTypeBuilder<ClientProperty> builder)
    {
        builder.ToTable("ClientProperties");
        builder.HasKey(p => p.Id);
        builder.Property(p => p.TenantId).HasMaxLength(128);
        builder.Property(p => p.Key).IsRequired().HasMaxLength(250);
        builder.Property(p => p.Value).IsRequired().HasMaxLength(2000);
    }
}

public class ClientIdPRestrictionConfiguration : IEntityTypeConfiguration<ClientIdPRestriction>
{
    public void Configure(EntityTypeBuilder<ClientIdPRestriction> builder)
    {
        builder.ToTable("ClientIdPRestrictions");
        builder.HasKey(r => r.Id);
        builder.Property(r => r.TenantId).HasMaxLength(128);
        builder.Property(r => r.Provider).IsRequired().HasMaxLength(200);
    }
}

public class ClientAllowedRoleConfiguration : IEntityTypeConfiguration<ClientAllowedRole>
{
    public void Configure(EntityTypeBuilder<ClientAllowedRole> builder)
    {
        builder.ToTable("ClientAllowedRoles");
        builder.HasKey(r => r.Id);
        builder.Property(r => r.TenantId).HasMaxLength(128);
        builder.Property(r => r.Role).IsRequired().HasMaxLength(200);
    }
}

public class ClientAllowedUserConfiguration : IEntityTypeConfiguration<ClientAllowedUser>
{
    public void Configure(EntityTypeBuilder<ClientAllowedUser> builder)
    {
        builder.ToTable("ClientAllowedUsers");
        builder.HasKey(u => u.Id);
        builder.Property(u => u.TenantId).HasMaxLength(128);
        builder.Property(u => u.SubjectId).IsRequired().HasMaxLength(200);
        builder.Property(u => u.DisplayName).HasMaxLength(200);
    }
}
