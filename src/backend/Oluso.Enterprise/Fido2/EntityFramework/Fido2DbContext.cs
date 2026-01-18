using Microsoft.EntityFrameworkCore;
using Oluso.Core.Data;
using Oluso.Core.Domain.Interfaces;
using Oluso.Enterprise.Fido2.Entities;

namespace Oluso.Enterprise.Fido2.EntityFramework;

/// <summary>
/// DbContext for FIDO2/WebAuthn entities. Uses separate migrations history table.
/// Can share the same database as the host application or use a separate database.
/// Includes automatic tenant filtering and multi-tenant support.
/// </summary>
public class Fido2DbContext : PluginDbContextBase<Fido2DbContext>
{
    /// <summary>
    /// Plugin name for migration table isolation
    /// </summary>
    public const string PluginIdentifier = "Fido2";

    /// <inheritdoc />
    protected override string PluginName => PluginIdentifier;

    /// <inheritdoc />
    /// <remarks>
    /// FIDO2 context has order 80 (after SCIM=50, LDAP=60, SAML=70)
    /// </remarks>
    public override int MigrationOrder => 80;

    public Fido2DbContext(DbContextOptions<Fido2DbContext> options) : base(options)
    {
    }

    /// <summary>
    /// Constructor for derived provider-specific contexts (e.g., Fido2DbContextSqlite).
    /// </summary>
    protected Fido2DbContext(DbContextOptions options) : base(options)
    {
    }

    public Fido2DbContext(
        DbContextOptions<Fido2DbContext> options,
        ITenantContext tenantContext) : base(options, tenantContext)
    {
    }

    public DbSet<Fido2CredentialEntity> Fido2Credentials => Set<Fido2CredentialEntity>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        base.OnModelCreating(modelBuilder);
        modelBuilder.ApplyFido2Configuration();
    }
}

/// <summary>
/// Extension methods for applying FIDO2 entity configurations
/// </summary>
public static class Fido2ModelBuilderExtensions
{
    /// <summary>
    /// Apply FIDO2 entity configurations to the model builder.
    /// </summary>
    public static ModelBuilder ApplyFido2Configuration(this ModelBuilder modelBuilder)
    {
        modelBuilder.ApplyConfiguration(new Fido2CredentialConfiguration());
        return modelBuilder;
    }
}
