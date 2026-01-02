using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Oluso.Enterprise.Scim.Entities;

namespace Oluso.Enterprise.Scim.EntityFramework;

public class ScimClientConfiguration : IEntityTypeConfiguration<ScimClient>
{
    public void Configure(EntityTypeBuilder<ScimClient> builder)
    {
        builder.ToTable("ScimClients");

        builder.HasKey(x => x.Id);

        builder.Property(x => x.Id).HasMaxLength(128);
        builder.Property(x => x.TenantId).HasMaxLength(128).IsRequired();
        builder.Property(x => x.Name).HasMaxLength(200).IsRequired();
        builder.Property(x => x.Description).HasMaxLength(1000);
        builder.Property(x => x.TokenHash).HasMaxLength(128).IsRequired();
        builder.Property(x => x.AllowedIpRanges).HasMaxLength(2000);
        builder.Property(x => x.DefaultRoleId).HasMaxLength(128);

        builder.HasIndex(x => x.TenantId);
        builder.HasIndex(x => x.TokenHash).IsUnique();
        builder.HasIndex(x => new { x.TenantId, x.Name }).IsUnique();
    }
}

public class ScimProvisioningLogConfiguration : IEntityTypeConfiguration<ScimProvisioningLog>
{
    public void Configure(EntityTypeBuilder<ScimProvisioningLog> builder)
    {
        builder.ToTable("ScimProvisioningLogs");

        builder.HasKey(x => x.Id);

        builder.Property(x => x.Id).HasMaxLength(128);
        builder.Property(x => x.TenantId).HasMaxLength(128).IsRequired();
        builder.Property(x => x.ScimClientId).HasMaxLength(128).IsRequired();
        builder.Property(x => x.Method).HasMaxLength(10).IsRequired();
        builder.Property(x => x.Path).HasMaxLength(500).IsRequired();
        builder.Property(x => x.ResourceType).HasMaxLength(50);
        builder.Property(x => x.ResourceId).HasMaxLength(128);
        builder.Property(x => x.Operation).HasMaxLength(50).IsRequired();
        builder.Property(x => x.ErrorMessage).HasMaxLength(2000);
        builder.Property(x => x.ClientIp).HasMaxLength(50);

        builder.HasIndex(x => x.ScimClientId);
        builder.HasIndex(x => x.TenantId);
        builder.HasIndex(x => x.Timestamp);
        builder.HasIndex(x => new { x.ScimClientId, x.Timestamp });
    }
}

public class ScimResourceMappingConfiguration : IEntityTypeConfiguration<ScimResourceMapping>
{
    public void Configure(EntityTypeBuilder<ScimResourceMapping> builder)
    {
        builder.ToTable("ScimResourceMappings");

        builder.HasKey(x => x.Id);

        builder.Property(x => x.Id).HasMaxLength(128);
        builder.Property(x => x.TenantId).HasMaxLength(128).IsRequired();
        builder.Property(x => x.ScimClientId).HasMaxLength(128).IsRequired();
        builder.Property(x => x.ResourceType).HasMaxLength(50).IsRequired();
        builder.Property(x => x.ExternalId).HasMaxLength(256).IsRequired();
        builder.Property(x => x.InternalId).HasMaxLength(128).IsRequired();

        builder.HasIndex(x => new { x.ScimClientId, x.ResourceType, x.ExternalId }).IsUnique();
        builder.HasIndex(x => new { x.ScimClientId, x.ResourceType, x.InternalId });
        builder.HasIndex(x => x.TenantId);
    }
}

public class ScimAttributeMappingConfiguration : IEntityTypeConfiguration<ScimAttributeMapping>
{
    public void Configure(EntityTypeBuilder<ScimAttributeMapping> builder)
    {
        builder.ToTable("ScimAttributeMappings");

        builder.HasKey(x => x.Id);

        builder.Property(x => x.Id).HasMaxLength(128);
        builder.Property(x => x.TenantId).HasMaxLength(128).IsRequired();
        builder.Property(x => x.ScimClientId).HasMaxLength(128).IsRequired();
        builder.Property(x => x.ScimAttribute).HasMaxLength(256).IsRequired();
        builder.Property(x => x.InternalProperty).HasMaxLength(128).IsRequired();
        builder.Property(x => x.Direction).HasMaxLength(20).IsRequired();
        builder.Property(x => x.DefaultValue).HasMaxLength(500);
        builder.Property(x => x.Transformation).HasMaxLength(100);

        builder.HasIndex(x => x.ScimClientId);
        builder.HasIndex(x => x.TenantId);
        builder.HasIndex(x => new { x.ScimClientId, x.ScimAttribute }).IsUnique();
    }
}
