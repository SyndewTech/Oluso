using Microsoft.EntityFrameworkCore;
using Oluso.Core.Data;
using Oluso.Core.Domain.Interfaces;

namespace Oluso.Enterprise.Verification.EntityFramework;

/// <summary>
/// Business verification's own context: businesses, applicants and checks, with its own
/// migrations history table. Shares the host's database by default.
/// </summary>
public class VerificationDbContext : PluginDbContextBase<VerificationDbContext>
{
    public const string PluginIdentifier = "Verification";

    protected override string PluginName => PluginIdentifier;

    /// <summary>SQLite tables are prefixed Kyb_; SQL Server and PostgreSQL use a kyb schema.</summary>
    protected override string? TablePrefix => "Kyb_";

    public override int MigrationOrder => 70;

    public VerificationDbContext(DbContextOptions<VerificationDbContext> options) : base(options)
    {
    }

    /// <summary>For the provider-specific contexts.</summary>
    protected VerificationDbContext(DbContextOptions options) : base(options)
    {
    }

    public VerificationDbContext(DbContextOptions<VerificationDbContext> options, ITenantContext tenantContext)
        : base(options, tenantContext)
    {
    }

    public DbSet<VerifiedBusiness> Businesses => Set<VerifiedBusiness>();
    public DbSet<BusinessApplicant> Applicants => Set<BusinessApplicant>();
    public DbSet<BusinessVerificationCheck> Checks => Set<BusinessVerificationCheck>();

    protected override void ConfigureModel(ModelBuilder modelBuilder)
    {
        modelBuilder.ApplyConfiguration(new VerifiedBusinessConfiguration());
        modelBuilder.ApplyConfiguration(new BusinessApplicantConfiguration());
        modelBuilder.ApplyConfiguration(new BusinessVerificationCheckConfiguration());
    }
}
