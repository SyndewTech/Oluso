using Microsoft.EntityFrameworkCore;
using Oluso.Core.Data;
using Oluso.Core.Domain.Interfaces;
using Oluso.Enterprise.Ldap.Entities;

namespace Oluso.Enterprise.Ldap.EntityFramework;

/// <summary>
/// DbContext for LDAP entities. Uses separate migrations history table.
/// Can share the same database as the host application or use a separate database.
/// Includes automatic tenant filtering and multi-tenant support.
/// </summary>
public class LdapDbContext : PluginDbContextBase<LdapDbContext>
{
    /// <summary>
    /// Plugin name for migration table isolation
    /// </summary>
    public const string PluginIdentifier = "Ldap";

    /// <inheritdoc />
    protected override string PluginName => PluginIdentifier;

    /// <inheritdoc />
    /// <remarks>
    /// LDAP context has order 60 (after core and SCIM, before other plugins at 100)
    /// </remarks>
    public override int MigrationOrder => 60;

    public LdapDbContext(DbContextOptions<LdapDbContext> options) : base(options)
    {
    }

    /// <summary>
    /// Constructor for derived provider-specific contexts (e.g., LdapDbContextSqlite).
    /// </summary>
    protected LdapDbContext(DbContextOptions options) : base(options)
    {
    }

    public LdapDbContext(
        DbContextOptions<LdapDbContext> options,
        ITenantContext tenantContext) : base(options, tenantContext)
    {
    }

    public DbSet<LdapServiceAccount> LdapServiceAccounts => Set<LdapServiceAccount>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        base.OnModelCreating(modelBuilder);
        modelBuilder.ApplyLdapConfiguration();
    }
}

/// <summary>
/// Extension methods for applying LDAP entity configurations
/// </summary>
public static class LdapModelBuilderExtensions
{
    /// <summary>
    /// Apply LDAP entity configurations to the model builder.
    /// </summary>
    public static ModelBuilder ApplyLdapConfiguration(this ModelBuilder modelBuilder)
    {
        modelBuilder.ApplyConfiguration(new LdapServiceAccountConfiguration());
        return modelBuilder;
    }
}
