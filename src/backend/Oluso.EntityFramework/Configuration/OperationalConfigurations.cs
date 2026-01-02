using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Oluso.Core.Domain.Entities;

namespace Oluso.EntityFramework.Configuration;

public class PersistedGrantConfiguration : IEntityTypeConfiguration<PersistedGrant>
{
    public void Configure(EntityTypeBuilder<PersistedGrant> builder)
    {
        builder.ToTable("PersistedGrants");
        builder.HasKey(g => g.Key);

        builder.Property(g => g.Key).HasMaxLength(200);
        builder.Property(g => g.TenantId).HasMaxLength(128);
        builder.Property(g => g.Type).IsRequired().HasMaxLength(50);
        builder.Property(g => g.SubjectId).HasMaxLength(200);
        builder.Property(g => g.SessionId).HasMaxLength(100);
        builder.Property(g => g.ClientId).IsRequired().HasMaxLength(200);
        builder.Property(g => g.Description).HasMaxLength(200);
        builder.Property(g => g.Data).IsRequired();

        builder.HasIndex(g => g.SubjectId);
        builder.HasIndex(g => new { g.SubjectId, g.ClientId, g.Type });
        builder.HasIndex(g => new { g.SubjectId, g.SessionId, g.Type });
        builder.HasIndex(g => g.Expiration);
    }
}

public class DeviceFlowCodeConfiguration : IEntityTypeConfiguration<DeviceFlowCode>
{
    public void Configure(EntityTypeBuilder<DeviceFlowCode> builder)
    {
        builder.ToTable("DeviceFlowCodes");
        builder.HasKey(d => d.UserCode);

        builder.Property(d => d.TenantId).HasMaxLength(128);
        builder.Property(d => d.DeviceCode).IsRequired().HasMaxLength(200);
        builder.Property(d => d.UserCode).HasMaxLength(200);
        builder.Property(d => d.SubjectId).HasMaxLength(200);
        builder.Property(d => d.SessionId).HasMaxLength(100);
        builder.Property(d => d.ClientId).IsRequired().HasMaxLength(200);
        builder.Property(d => d.Description).HasMaxLength(200);
        builder.Property(d => d.Data).IsRequired();

        builder.HasIndex(d => d.DeviceCode).IsUnique();
        builder.HasIndex(d => d.Expiration);
    }
}

public class ConsentConfiguration : IEntityTypeConfiguration<Consent>
{
    public void Configure(EntityTypeBuilder<Consent> builder)
    {
        builder.ToTable("Consents");
        builder.HasKey(c => c.Id);

        builder.Property(c => c.TenantId).HasMaxLength(128);
        builder.Property(c => c.SubjectId).IsRequired().HasMaxLength(200);
        builder.Property(c => c.ClientId).IsRequired().HasMaxLength(200);
        builder.Property(c => c.Scopes).IsRequired();

        builder.HasIndex(c => new { c.SubjectId, c.ClientId, c.TenantId }).IsUnique();
    }
}

public class SigningKeyConfiguration : IEntityTypeConfiguration<SigningKey>
{
    public void Configure(EntityTypeBuilder<SigningKey> builder)
    {
        builder.ToTable("SigningKeys");
        builder.HasKey(k => k.Id);

        builder.Property(k => k.Id).HasMaxLength(64);
        builder.Property(k => k.TenantId).HasMaxLength(128);
        builder.Property(k => k.Name).IsRequired().HasMaxLength(200);
        builder.Property(k => k.KeyId).IsRequired().HasMaxLength(100);
        builder.Property(k => k.Algorithm).IsRequired().HasMaxLength(50);
        builder.Property(k => k.ClientId).HasMaxLength(200);
        builder.Property(k => k.PrivateKeyData).IsRequired();
        builder.Property(k => k.PublicKeyData).IsRequired();
        builder.Property(k => k.KeyVaultUri).HasMaxLength(500);
        builder.Property(k => k.RevocationReason).HasMaxLength(500);
        builder.Property(k => k.X5t).HasMaxLength(100);
        builder.Property(k => k.X5tS256).HasMaxLength(100);
        builder.Property(k => k.CertificateSubject).HasMaxLength(500);
        builder.Property(k => k.CertificateIssuer).HasMaxLength(500);
        builder.Property(k => k.CertificateSerialNumber).HasMaxLength(100);

        builder.HasIndex(k => new { k.TenantId, k.KeyId }).IsUnique();
        builder.HasIndex(k => new { k.TenantId, k.ClientId, k.Status });
        builder.HasIndex(k => k.ExpiresAt);

        // Ignore computed properties
        builder.Ignore(k => k.CanSign);
        builder.Ignore(k => k.CanVerify);
        builder.Ignore(k => k.IsExpiringSoon);
        builder.Ignore(k => k.IsExpired);
        builder.Ignore(k => k.HasCertificate);
    }
}

public class KeyRotationConfigConfiguration : IEntityTypeConfiguration<KeyRotationConfig>
{
    public void Configure(EntityTypeBuilder<KeyRotationConfig> builder)
    {
        builder.ToTable("KeyRotationConfigs");
        builder.HasKey(k => k.Id);

        builder.Property(k => k.Id).HasMaxLength(64);
        builder.Property(k => k.TenantId).HasMaxLength(128);
        builder.Property(k => k.ClientId).HasMaxLength(200);
        builder.Property(k => k.Algorithm).IsRequired().HasMaxLength(50);

        builder.HasIndex(k => new { k.TenantId, k.ClientId }).IsUnique();
    }
}

public class PushedAuthorizationRequestConfiguration : IEntityTypeConfiguration<PushedAuthorizationRequest>
{
    public void Configure(EntityTypeBuilder<PushedAuthorizationRequest> builder)
    {
        builder.ToTable("PushedAuthorizationRequests");
        builder.HasKey(p => p.Id);

        builder.Property(p => p.TenantId).HasMaxLength(128);
        builder.Property(p => p.RequestUri).IsRequired().HasMaxLength(500);
        builder.Property(p => p.ReferenceValueHash).IsRequired().HasMaxLength(100);
        builder.Property(p => p.ClientId).IsRequired().HasMaxLength(200);
        builder.Property(p => p.Parameters).IsRequired();

        builder.HasIndex(p => p.RequestUri).IsUnique();
        builder.HasIndex(p => p.ExpiresAtUtc);
        builder.HasIndex(p => new { p.TenantId, p.ClientId });
    }
}

public class TenantConfiguration : IEntityTypeConfiguration<Tenant>
{
    public void Configure(EntityTypeBuilder<Tenant> builder)
    {
        builder.ToTable("Tenants");
        builder.HasKey(t => t.Id);

        builder.Property(t => t.Id).HasMaxLength(128);
        builder.Property(t => t.Name).IsRequired().HasMaxLength(200);
        builder.Property(t => t.DisplayName).HasMaxLength(200);
        builder.Property(t => t.Identifier).IsRequired().HasMaxLength(100);
        builder.Property(t => t.Description).HasMaxLength(1000);
        builder.Property(t => t.Configuration);
        builder.Property(t => t.ConnectionString).HasMaxLength(2000);
        builder.Property(t => t.CustomDomain).HasMaxLength(500);
        builder.Property(t => t.PlanId).HasMaxLength(100);
        builder.Property(t => t.TermsOfServiceUrl).HasMaxLength(2000);
        builder.Property(t => t.PrivacyPolicyUrl).HasMaxLength(2000);
        builder.Property(t => t.AllowedEmailDomains).HasMaxLength(4000);

        builder.HasIndex(t => t.Identifier).IsUnique();

        // Configure TenantBranding as owned entity
        builder.OwnsOne(t => t.Branding, branding =>
        {
            branding.Property(b => b.LogoUrl).HasMaxLength(2000);
            branding.Property(b => b.FaviconUrl).HasMaxLength(2000);
            branding.Property(b => b.PrimaryColor).HasMaxLength(20);
            branding.Property(b => b.SecondaryColor).HasMaxLength(20);
            branding.Property(b => b.BackgroundColor).HasMaxLength(20);
            branding.Property(b => b.CustomCss);
        });

        // Configure TenantPasswordPolicy as owned entity
        builder.OwnsOne(t => t.PasswordPolicy, policy =>
        {
            policy.Property(p => p.CustomRegexPattern).HasMaxLength(1000);
            policy.Property(p => p.CustomRegexErrorMessage).HasMaxLength(500);
        });

        // Configure TenantProtocolConfiguration as owned entity
        builder.OwnsOne(t => t.ProtocolConfiguration, protocol =>
        {
            protocol.Property(p => p.AllowedGrantTypesJson).HasMaxLength(2000);
            protocol.Property(p => p.AllowedResponseTypesJson).HasMaxLength(2000);
            protocol.Property(p => p.AllowedTokenEndpointAuthMethodsJson).HasMaxLength(2000);
            protocol.Property(p => p.SubjectTypesSupportedJson).HasMaxLength(500);
            protocol.Property(p => p.IdTokenSigningAlgValuesSupportedJson).HasMaxLength(500);
            protocol.Property(p => p.CodeChallengeMethodsSupportedJson).HasMaxLength(500);
            protocol.Property(p => p.DPoPSigningAlgValuesSupportedJson).HasMaxLength(500);
        });
    }
}

public class OlusoUserConfiguration : IEntityTypeConfiguration<OlusoUser>
{
    public void Configure(EntityTypeBuilder<OlusoUser> builder)
    {
        builder.Property(u => u.TenantId).HasMaxLength(128);
        builder.Property(u => u.FirstName).HasMaxLength(100);
        builder.Property(u => u.LastName).HasMaxLength(100);
        builder.Property(u => u.DisplayName).HasMaxLength(200);
        builder.Property(u => u.ProfilePictureUrl).HasMaxLength(2000);
        builder.Property(u => u.Locale).HasMaxLength(20);
        builder.Property(u => u.TimeZone).HasMaxLength(100);

        // Tenant-scoped unique constraints
        // This replaces the default Identity global unique index on NormalizedUserName
        builder.HasIndex(u => new { u.TenantId, u.NormalizedUserName })
            .IsUnique()
            .HasDatabaseName("IX_AspNetUsers_TenantId_NormalizedUserName");

        builder.HasIndex(u => new { u.TenantId, u.NormalizedEmail })
            .IsUnique()
            .HasDatabaseName("IX_AspNetUsers_TenantId_NormalizedEmail");

        // Index for tenant queries
        builder.HasIndex(u => u.TenantId);

        // Navigation properties - configure relationships
        builder.HasMany(u => u.Claims)
            .WithOne(c => c.User)
            .HasForeignKey(c => c.UserId)
            .IsRequired();

        builder.HasMany(u => u.Logins)
            .WithOne(l => l.User)
            .HasForeignKey(l => l.UserId)
            .IsRequired();

        builder.HasMany(u => u.Tokens)
            .WithOne(t => t.User)
            .HasForeignKey(t => t.UserId)
            .IsRequired();

        builder.HasMany(u => u.UserRoles)
            .WithOne(ur => ur.User)
            .HasForeignKey(ur => ur.UserId)
            .IsRequired();
    }
}

public class OlusoRoleConfiguration : IEntityTypeConfiguration<OlusoRole>
{
    public void Configure(EntityTypeBuilder<OlusoRole> builder)
    {
        builder.Property(r => r.TenantId).HasMaxLength(128);
        builder.Property(r => r.DisplayName).HasMaxLength(200);
        builder.Property(r => r.Description).HasMaxLength(1000);
        builder.Property(r => r.Permissions).HasMaxLength(4000);

        // Tenant-scoped unique constraint on role name
        // This replaces the default Identity global unique index on NormalizedName
        builder.HasIndex(r => new { r.TenantId, r.NormalizedName })
            .IsUnique()
            .HasDatabaseName("IX_AspNetRoles_TenantId_NormalizedName");

        // Index for tenant queries
        builder.HasIndex(r => r.TenantId);
        builder.HasIndex(r => r.IsSystemRole);

        // Navigation properties - configure relationships
        builder.HasMany(r => r.UserRoles)
            .WithOne(ur => ur.Role)
            .HasForeignKey(ur => ur.RoleId)
            .IsRequired();

        builder.HasMany(r => r.RoleClaims)
            .WithOne(rc => rc.Role)
            .HasForeignKey(rc => rc.RoleId)
            .IsRequired();
    }
}

public class OlusoUserLoginConfiguration : IEntityTypeConfiguration<OlusoUserLogin>
{
    public void Configure(EntityTypeBuilder<OlusoUserLogin> builder)
    {
        builder.Property(l => l.ProviderDisplayName).HasMaxLength(200);

        // Index for tracking when logins were used
        builder.HasIndex(l => l.LastUsedAt);
    }
}

public class OlusoUserClaimConfiguration : IEntityTypeConfiguration<OlusoUserClaim>
{
    public void Configure(EntityTypeBuilder<OlusoUserClaim> builder)
    {
        builder.Property(c => c.Source).HasMaxLength(100);
        builder.Property(c => c.Description).HasMaxLength(500);
    }
}

public class OlusoUserRoleConfiguration : IEntityTypeConfiguration<OlusoUserRole>
{
    public void Configure(EntityTypeBuilder<OlusoUserRole> builder)
    {
        builder.Property(ur => ur.AssignedBy).HasMaxLength(128);

        // Index for querying role assignments by date
        builder.HasIndex(ur => ur.AssignedAt);
    }
}

public class OlusoRoleClaimConfiguration : IEntityTypeConfiguration<OlusoRoleClaim>
{
    public void Configure(EntityTypeBuilder<OlusoRoleClaim> builder)
    {
        builder.Property(c => c.Description).HasMaxLength(500);

        // Index for querying claims by creation date
        builder.HasIndex(c => c.CreatedAt);
    }
}

public class OlusoUserTokenConfiguration : IEntityTypeConfiguration<OlusoUserToken>
{
    public void Configure(EntityTypeBuilder<OlusoUserToken> builder)
    {
        // Index for cleanup of expired tokens
        builder.HasIndex(t => t.ExpiresAt);
    }
}

public class IdentityProviderConfiguration : IEntityTypeConfiguration<IdentityProvider>
{
    public void Configure(EntityTypeBuilder<IdentityProvider> builder)
    {
        builder.ToTable("IdentityProviders");
        builder.HasKey(p => p.Id);

        builder.Property(p => p.TenantId).HasMaxLength(128);
        builder.Property(p => p.Scheme).IsRequired().HasMaxLength(200);
        builder.Property(p => p.DisplayName).HasMaxLength(200);
        builder.Property(p => p.IconUrl).HasMaxLength(2000);
        builder.Property(p => p.Properties);

        builder.HasIndex(p => p.Scheme);
        builder.HasIndex(p => new { p.TenantId, p.Scheme }).IsUnique();
    }
}

public class JourneyPolicyEntityConfiguration : IEntityTypeConfiguration<JourneyPolicyEntity>
{
    public void Configure(EntityTypeBuilder<JourneyPolicyEntity> builder)
    {
        builder.ToTable("JourneyPolicies");
        builder.HasKey(p => p.Id);

        builder.Property(p => p.Id).HasMaxLength(64);
        builder.Property(p => p.TenantId).HasMaxLength(128);
        builder.Property(p => p.Name).IsRequired().HasMaxLength(200);
        builder.Property(p => p.Type).IsRequired().HasMaxLength(50);
        builder.Property(p => p.Description).HasMaxLength(1000);
        builder.Property(p => p.Steps).IsRequired();
        builder.Property(p => p.Conditions);
        builder.Property(p => p.OutputClaims);
        builder.Property(p => p.SessionConfig);
        builder.Property(p => p.UiConfig);
        builder.Property(p => p.Tags).HasMaxLength(500);

        // Data collection journey properties
        builder.Property(p => p.SubmissionCollection).HasMaxLength(200);
        builder.Property(p => p.DuplicateCheckFields).HasMaxLength(500);
        builder.Property(p => p.SuccessRedirectUrl).HasMaxLength(2000);
        builder.Property(p => p.SuccessMessage).HasMaxLength(1000);

        builder.HasIndex(p => new { p.TenantId, p.Type });
        builder.HasIndex(p => new { p.TenantId, p.Enabled, p.Priority });
    }
}

public class JourneyStateEntityConfiguration : IEntityTypeConfiguration<JourneyStateEntity>
{
    public void Configure(EntityTypeBuilder<JourneyStateEntity> builder)
    {
        builder.ToTable("JourneyStates");
        builder.HasKey(s => s.Id);

        builder.Property(s => s.Id).HasMaxLength(64);
        builder.Property(s => s.TenantId).HasMaxLength(128);
        builder.Property(s => s.ClientId).IsRequired().HasMaxLength(200);
        builder.Property(s => s.UserId).HasMaxLength(200);
        builder.Property(s => s.PolicyId).IsRequired().HasMaxLength(64);
        builder.Property(s => s.CurrentStepId).IsRequired().HasMaxLength(100);
        builder.Property(s => s.Status).IsRequired().HasMaxLength(50);
        builder.Property(s => s.Data);
        builder.Property(s => s.ClaimsBag);
        builder.Property(s => s.SessionId).HasMaxLength(100);
        builder.Property(s => s.AuthenticatedUserId).HasMaxLength(200);
        builder.Property(s => s.CorrelationId).HasMaxLength(100);
        builder.Property(s => s.CallbackUrl).HasMaxLength(2000);

        builder.HasIndex(s => new { s.TenantId, s.ClientId });
        builder.HasIndex(s => new { s.TenantId, s.UserId });
        builder.HasIndex(s => s.ExpiresAt);
        builder.HasIndex(s => s.Status);
    }
}

public class JourneySubmissionEntityConfiguration : IEntityTypeConfiguration<JourneySubmissionEntity>
{
    public void Configure(EntityTypeBuilder<JourneySubmissionEntity> builder)
    {
        builder.ToTable("JourneySubmissions");
        builder.HasKey(s => s.Id);

        builder.Property(s => s.Id).HasMaxLength(64);
        builder.Property(s => s.TenantId).HasMaxLength(128);
        builder.Property(s => s.PolicyId).IsRequired().HasMaxLength(64);
        builder.Property(s => s.PolicyName).HasMaxLength(200);
        builder.Property(s => s.JourneyId).HasMaxLength(64);
        builder.Property(s => s.Data).IsRequired();
        builder.Property(s => s.IpAddress).HasMaxLength(50);
        builder.Property(s => s.UserAgent).HasMaxLength(1000);
        builder.Property(s => s.Referrer).HasMaxLength(2000);
        builder.Property(s => s.UtmParameters).HasMaxLength(2000);
        builder.Property(s => s.Country).HasMaxLength(100);
        builder.Property(s => s.Locale).HasMaxLength(20);
        builder.Property(s => s.Status).IsRequired().HasMaxLength(50);
        builder.Property(s => s.Notes).HasMaxLength(4000);
        builder.Property(s => s.Tags).HasMaxLength(500);
        builder.Property(s => s.ReviewedBy).HasMaxLength(200);

        builder.HasIndex(s => new { s.TenantId, s.PolicyId });
        builder.HasIndex(s => new { s.TenantId, s.PolicyId, s.Status });
        builder.HasIndex(s => s.CreatedAt);
        builder.HasIndex(s => s.Status);
    }
}

public class Fido2CredentialEntityConfiguration : IEntityTypeConfiguration<Fido2CredentialEntity>
{
    public void Configure(EntityTypeBuilder<Fido2CredentialEntity> builder)
    {
        builder.ToTable("Fido2Credentials");
        builder.HasKey(c => c.Id);

        builder.Property(c => c.Id).HasMaxLength(64);
        builder.Property(c => c.TenantId).HasMaxLength(128);
        builder.Property(c => c.UserId).IsRequired().HasMaxLength(200);
        builder.Property(c => c.CredentialId).IsRequired().HasMaxLength(1024);
        builder.Property(c => c.PublicKey).IsRequired();
        builder.Property(c => c.UserHandle).IsRequired().HasMaxLength(200);
        builder.Property(c => c.DisplayName).HasMaxLength(200);
        builder.Property(c => c.AttestationFormat).HasMaxLength(50);
        builder.Property(c => c.Transports).HasMaxLength(200);

        // Credential ID must be unique per tenant
        builder.HasIndex(c => new { c.TenantId, c.CredentialId }).IsUnique();

        // Query by user
        builder.HasIndex(c => new { c.TenantId, c.UserId });

        // Query active credentials
        builder.HasIndex(c => new { c.TenantId, c.UserId, c.IsActive });
    }
}
