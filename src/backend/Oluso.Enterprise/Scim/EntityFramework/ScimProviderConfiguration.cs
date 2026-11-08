using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Oluso.Enterprise.Scim.Entities;

namespace Oluso.Enterprise.Scim.EntityFramework;

public class ScimProviderConfiguration : IEntityTypeConfiguration<ScimProvider>
{
    public void Configure(EntityTypeBuilder<ScimProvider> builder)
    {
        builder.ToTable("ScimProviders");

        builder.HasKey(x => x.Id);

        builder.Property(x => x.Id).HasMaxLength(128);
        builder.Property(x => x.TenantId).HasMaxLength(128).IsRequired();
        builder.Property(x => x.Name).HasMaxLength(200).IsRequired();
        builder.Property(x => x.Description).HasMaxLength(1000);
        builder.Property(x => x.Flavor).HasMaxLength(50).IsRequired();
        builder.Property(x => x.BaseUrl).HasMaxLength(2000).IsRequired();
        builder.Property(x => x.AuthType).HasMaxLength(50).IsRequired();
        builder.Property(x => x.OAuthTokenEndpoint).HasMaxLength(2000);
        builder.Property(x => x.OAuthClientId).HasMaxLength(256);
        builder.Property(x => x.OAuthScope).HasMaxLength(1000);
        builder.Property(x => x.LinkedScimClientId).HasMaxLength(128);
        builder.Property(x => x.GroupSource).HasMaxLength(20).IsRequired();
        builder.Property(x => x.ScopeRoleIds).HasMaxLength(4000);
        builder.Property(x => x.ScopeTeamIds).HasMaxLength(4000);
        builder.Ignore(x => x.HasUserScope);
        builder.Property(x => x.UserNameSource).HasMaxLength(50).IsRequired();
        builder.Property(x => x.UserNameExpression).HasMaxLength(2000);
        builder.Property(x => x.DeprovisionMode).HasMaxLength(50).IsRequired();
        builder.Property(x => x.LastReconcileSummary).HasMaxLength(1000);
        builder.Property(x => x.LastError).HasMaxLength(2000);

        builder.HasIndex(x => x.TenantId);
        builder.HasIndex(x => new { x.TenantId, x.Name }).IsUnique();
    }
}

public class ScimProviderResourceMappingConfiguration : IEntityTypeConfiguration<ScimProviderResourceMapping>
{
    public void Configure(EntityTypeBuilder<ScimProviderResourceMapping> builder)
    {
        builder.ToTable("ScimProviderResourceMappings");

        builder.HasKey(x => x.Id);

        builder.Property(x => x.Id).HasMaxLength(128);
        builder.Property(x => x.TenantId).HasMaxLength(128).IsRequired();
        builder.Property(x => x.ScimProviderId).HasMaxLength(128).IsRequired();
        builder.Property(x => x.ResourceType).HasMaxLength(50).IsRequired();
        builder.Property(x => x.InternalId).HasMaxLength(128).IsRequired();
        builder.Property(x => x.RemoteId).HasMaxLength(256).IsRequired();
        builder.Property(x => x.RemoteVersion).HasMaxLength(256);
        builder.Property(x => x.PayloadHash).HasMaxLength(128);

        builder.HasIndex(x => new { x.ScimProviderId, x.ResourceType, x.InternalId }).IsUnique();
        builder.HasIndex(x => new { x.ScimProviderId, x.ResourceType, x.RemoteId });
        builder.HasIndex(x => x.TenantId);
    }
}

public class ScimProviderAttributeMappingConfiguration : IEntityTypeConfiguration<ScimProviderAttributeMapping>
{
    public void Configure(EntityTypeBuilder<ScimProviderAttributeMapping> builder)
    {
        builder.ToTable("ScimProviderAttributeMappings");

        builder.HasKey(x => x.Id);

        builder.Property(x => x.Id).HasMaxLength(128);
        builder.Property(x => x.TenantId).HasMaxLength(128).IsRequired();
        builder.Property(x => x.ScimProviderId).HasMaxLength(128).IsRequired();
        builder.Property(x => x.ScimAttribute).HasMaxLength(256).IsRequired();
        builder.Property(x => x.InternalProperty).HasMaxLength(128).IsRequired();
        builder.Property(x => x.Expression).HasMaxLength(2000);
        builder.Property(x => x.Condition).HasMaxLength(1000);
        builder.Property(x => x.Transformation).HasMaxLength(100);
        builder.Property(x => x.DefaultValue).HasMaxLength(500);

        builder.HasIndex(x => new { x.ScimProviderId, x.ScimAttribute }).IsUnique();
        builder.HasIndex(x => x.TenantId);
    }
}

public class ScimProviderOutboxItemConfiguration : IEntityTypeConfiguration<ScimProviderOutboxItem>
{
    public void Configure(EntityTypeBuilder<ScimProviderOutboxItem> builder)
    {
        builder.ToTable("ScimProviderOutbox");

        builder.HasKey(x => x.Id);

        builder.Property(x => x.Id).HasMaxLength(128);
        builder.Property(x => x.TenantId).HasMaxLength(128).IsRequired();
        builder.Property(x => x.ScimProviderId).HasMaxLength(128).IsRequired();
        builder.Property(x => x.ResourceType).HasMaxLength(50).IsRequired();
        builder.Property(x => x.InternalId).HasMaxLength(128).IsRequired();
        builder.Property(x => x.Operation).HasMaxLength(20).IsRequired();
        builder.Property(x => x.Status).HasMaxLength(20).IsRequired();
        builder.Property(x => x.LockedBy).HasMaxLength(128);
        builder.Property(x => x.LastError).HasMaxLength(2000);
        builder.Property(x => x.Reason).HasMaxLength(200);

        builder.HasIndex(x => new { x.Status, x.NextAttemptAt });
        builder.HasIndex(x => new { x.ScimProviderId, x.Status, x.Priority, x.CreatedAt });
        builder.HasIndex(x => new { x.ScimProviderId, x.ResourceType, x.InternalId });
        builder.HasIndex(x => x.TenantId);
    }
}

public class ScimProviderLogConfiguration : IEntityTypeConfiguration<ScimProviderLog>
{
    public void Configure(EntityTypeBuilder<ScimProviderLog> builder)
    {
        builder.ToTable("ScimProviderLogs");

        builder.HasKey(x => x.Id);

        builder.Property(x => x.Id).HasMaxLength(128);
        builder.Property(x => x.TenantId).HasMaxLength(128).IsRequired();
        builder.Property(x => x.ScimProviderId).HasMaxLength(128).IsRequired();
        builder.Property(x => x.Operation).HasMaxLength(50).IsRequired();
        builder.Property(x => x.ResourceType).HasMaxLength(50);
        builder.Property(x => x.InternalId).HasMaxLength(128);
        builder.Property(x => x.RemoteId).HasMaxLength(256);
        builder.Property(x => x.Method).HasMaxLength(10).IsRequired();
        builder.Property(x => x.Path).HasMaxLength(2000).IsRequired();
        builder.Property(x => x.ErrorMessage).HasMaxLength(2000);

        builder.HasIndex(x => new { x.ScimProviderId, x.Timestamp });
        builder.HasIndex(x => x.Timestamp);
        builder.HasIndex(x => x.TenantId);
    }
}
