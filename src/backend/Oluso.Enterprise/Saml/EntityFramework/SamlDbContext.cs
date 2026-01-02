using Microsoft.EntityFrameworkCore;
using Oluso.Core.Data;
using Oluso.Core.Domain.Interfaces;
using Oluso.Enterprise.Saml.Entities;

namespace Oluso.Enterprise.Saml.EntityFramework;

/// <summary>
/// DbContext for SAML entities. Uses separate migrations history table.
/// Can share the same database as the host application or use a separate database.
/// Includes automatic tenant filtering and multi-tenant support.
/// </summary>
public class SamlDbContext : PluginDbContextBase<SamlDbContext>
{
    /// <summary>
    /// Plugin name for migration table isolation
    /// </summary>
    public const string PluginIdentifier = "Saml";

    /// <inheritdoc />
    protected override string PluginName => PluginIdentifier;

    /// <inheritdoc />
    /// <remarks>
    /// SAML context has order 70 (after SCIM=50, LDAP=60, before others at 100)
    /// </remarks>
    public override int MigrationOrder => 70;

    public SamlDbContext(DbContextOptions<SamlDbContext> options) : base(options)
    {
    }

    public SamlDbContext(
        DbContextOptions<SamlDbContext> options,
        ITenantContext tenantContext) : base(options, tenantContext)
    {
    }

    public DbSet<SamlServiceProvider> SamlServiceProviders => Set<SamlServiceProvider>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        base.OnModelCreating(modelBuilder);
        modelBuilder.ApplySamlConfiguration();
    }
}

/// <summary>
/// Extension methods for applying SAML entity configurations
/// </summary>
public static class SamlModelBuilderExtensions
{
    /// <summary>
    /// Apply SAML entity configurations to the model builder.
    /// </summary>
    public static ModelBuilder ApplySamlConfiguration(this ModelBuilder modelBuilder)
    {
        modelBuilder.ApplyConfiguration(new SamlServiceProviderConfiguration());
        return modelBuilder;
    }
}
