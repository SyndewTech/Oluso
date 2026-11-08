using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Oluso.Enterprise.Verification.EntityFramework;

public class VerifiedBusinessConfiguration : IEntityTypeConfiguration<VerifiedBusiness>
{
    public void Configure(EntityTypeBuilder<VerifiedBusiness> builder)
    {
        builder.ToTable("Businesses");
        builder.HasKey(b => b.Id);
        builder.Property(b => b.Id).IsRequired().HasMaxLength(64);
        builder.Property(b => b.TenantId).IsRequired().HasMaxLength(128);
        builder.Property(b => b.Country).IsRequired().HasMaxLength(2);
        builder.Property(b => b.RegistrationNumber).IsRequired().HasMaxLength(64);
        builder.Property(b => b.RegistrationType).HasMaxLength(16);
        builder.Property(b => b.RegisteredName).HasMaxLength(300);
        builder.Property(b => b.EntityType).HasMaxLength(120);
        builder.Property(b => b.RegistrationDate).HasMaxLength(40);
        builder.Property(b => b.RegisteredAddress).HasMaxLength(500);
        builder.Property(b => b.ActivityStatus).HasConversion<string>().HasMaxLength(16);
        builder.Property(b => b.RawStatus).HasMaxLength(80);
        builder.Property(b => b.RegistryCheckId).HasMaxLength(64);
        builder.Property(b => b.RegistryProviderId).HasMaxLength(64);
        builder.Property(b => b.RegistryEnvironment).HasConversion<string>().HasMaxLength(16);
        builder.Property(b => b.Status).HasConversion<string>().HasMaxLength(24);
        builder.Property(b => b.StatusReason).HasMaxLength(40);
        builder.Property(b => b.StatusMessage).HasMaxLength(1000);
        builder.Property(b => b.LatestCheckId).HasMaxLength(64);
        builder.Property(b => b.ReviewedBy).HasMaxLength(128);
        builder.Property(b => b.ReviewNote).HasMaxLength(1000);
        builder.Ignore(b => b.People);
        builder.Ignore(b => b.HasRegistrySnapshot);

        builder.HasIndex(b => new { b.TenantId, b.Country, b.RegistrationNumber }).IsUnique();
        builder.HasIndex(b => new { b.TenantId, b.Status, b.UpdatedAt });
    }
}

public class BusinessApplicantConfiguration : IEntityTypeConfiguration<BusinessApplicant>
{
    public void Configure(EntityTypeBuilder<BusinessApplicant> builder)
    {
        builder.ToTable("BusinessApplicants");
        builder.HasKey(a => a.Id);
        builder.Property(a => a.Id).IsRequired().HasMaxLength(64);
        builder.Property(a => a.TenantId).IsRequired().HasMaxLength(128);
        builder.Property(a => a.BusinessId).IsRequired().HasMaxLength(64);
        builder.Property(a => a.SubjectId).HasMaxLength(128);
        builder.Property(a => a.JourneyId).HasMaxLength(64);
        builder.Property(a => a.StepId).HasMaxLength(128);
        builder.Property(a => a.Relationship).IsRequired().HasMaxLength(48);
        builder.Property(a => a.EvidenceRequirement).HasConversion<string>().HasMaxLength(32);
        builder.Property(a => a.ProvidedBusinessName).HasMaxLength(300);
        builder.Property(a => a.MatchedPersonName).HasMaxLength(300);
        builder.Property(a => a.MatchedPersonRole).HasMaxLength(120);
        builder.Property(a => a.DocumentIdsJson).HasMaxLength(2000);
        builder.Property(a => a.Status).HasConversion<string>().HasMaxLength(24);
        builder.Property(a => a.StatusReason).HasMaxLength(40);
        builder.Property(a => a.StatusMessage).HasMaxLength(1000);
        builder.Property(a => a.NotifyEmail).HasMaxLength(320);
        builder.Property(a => a.ResumeUrl).HasMaxLength(1000);
        builder.Property(a => a.ReviewedBy).HasMaxLength(128);
        builder.Property(a => a.ReviewNote).HasMaxLength(1000);
        builder.Ignore(a => a.DocumentIds);

        builder.HasOne<VerifiedBusiness>().WithMany().HasForeignKey(a => a.BusinessId).OnDelete(DeleteBehavior.Cascade);

        builder.HasIndex(a => new { a.TenantId, a.BusinessId, a.SubjectId });
        builder.HasIndex(a => new { a.TenantId, a.SubjectId });
        builder.HasIndex(a => new { a.TenantId, a.JourneyId });
        builder.HasIndex(a => new { a.TenantId, a.Status, a.UpdatedAt });
    }
}

public class BusinessVerificationCheckConfiguration : IEntityTypeConfiguration<BusinessVerificationCheck>
{
    public void Configure(EntityTypeBuilder<BusinessVerificationCheck> builder)
    {
        builder.ToTable("BusinessChecks");
        builder.HasKey(c => c.Id);
        builder.Property(c => c.Id).IsRequired().HasMaxLength(64);
        builder.Property(c => c.TenantId).IsRequired().HasMaxLength(128);
        builder.Property(c => c.BusinessId).HasMaxLength(64);
        builder.Property(c => c.ApplicantId).HasMaxLength(64);
        builder.Property(c => c.Country).IsRequired().HasMaxLength(2);
        builder.Property(c => c.RegistrationNumber).IsRequired().HasMaxLength(64);
        builder.Property(c => c.Kind).IsRequired().HasMaxLength(24);
        builder.Property(c => c.ProviderId).HasMaxLength(64);
        builder.Property(c => c.Environment).HasConversion<string>().HasMaxLength(16);
        builder.Property(c => c.Outcome).IsRequired().HasMaxLength(32);
        builder.Property(c => c.SessionReference).HasMaxLength(128);
        builder.Property(c => c.JourneyId).HasMaxLength(64);
        builder.Property(c => c.StepId).HasMaxLength(128);
        builder.Property(c => c.ProviderReference).HasMaxLength(128);
        builder.Property(c => c.Message).HasMaxLength(1000);
        builder.Property(c => c.ReusedFromCheckId).HasMaxLength(64);
        builder.Property(c => c.OriginKind).HasMaxLength(32);
        builder.Property(c => c.OriginId).HasMaxLength(128);
        builder.Property(c => c.OriginName).HasMaxLength(200);

        builder.HasIndex(c => new { c.TenantId, c.BusinessId, c.CreatedAt });
        builder.HasIndex(c => new { c.TenantId, c.ProviderId, c.SessionReference });
    }
}
