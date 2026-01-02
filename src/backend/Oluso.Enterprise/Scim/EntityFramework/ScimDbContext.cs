using System.Security.Cryptography;
using System.Text;
using Microsoft.EntityFrameworkCore;
using Oluso.Core.Data;
using Oluso.Core.Domain.Interfaces;
using Oluso.Enterprise.Scim.Entities;

namespace Oluso.Enterprise.Scim.EntityFramework;

/// <summary>
/// DbContext for SCIM entities. Uses separate migrations history table.
/// Can share the same database as the host application or use a separate database.
/// Includes automatic tenant filtering and multi-tenant support.
/// </summary>
public class ScimDbContext : PluginDbContextBase<ScimDbContext>, ISeedableDbContext
{
    /// <summary>
    /// Plugin name for migration table isolation
    /// </summary>
    public const string PluginIdentifier = "Scim";

    /// <inheritdoc />
    protected override string PluginName => PluginIdentifier;

    /// <inheritdoc />
    /// <remarks>
    /// SCIM context has order 50 (after core, before other plugins at 100)
    /// </remarks>
    public override int MigrationOrder => 50;

    /// <inheritdoc />
    public string SeedName => "SCIM";

    public ScimDbContext(DbContextOptions<ScimDbContext> options) : base(options)
    {
    }

    public ScimDbContext(
        DbContextOptions<ScimDbContext> options,
        ITenantContext tenantContext) : base(options, tenantContext)
    {
    }

    public DbSet<ScimClient> ScimClients => Set<ScimClient>();
    public DbSet<ScimProvisioningLog> ScimProvisioningLogs => Set<ScimProvisioningLog>();
    public DbSet<ScimResourceMapping> ScimResourceMappings => Set<ScimResourceMapping>();
    public DbSet<ScimAttributeMapping> ScimAttributeMappings => Set<ScimAttributeMapping>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        base.OnModelCreating(modelBuilder);
        modelBuilder.ApplyScimConfiguration();
    }

    /// <inheritdoc />
    public async Task SeedAsync(CancellationToken cancellationToken = default)
    {
        // No seed data currently needed
        await Task.CompletedTask;
    }
}

/// <summary>
/// Extension methods for applying SCIM entity configurations
/// </summary>
public static class ScimModelBuilderExtensions
{
    /// <summary>
    /// Apply SCIM entity configurations to the model builder.
    /// </summary>
    public static ModelBuilder ApplyScimConfiguration(this ModelBuilder modelBuilder)
    {
        modelBuilder.ApplyConfiguration(new ScimClientConfiguration());
        modelBuilder.ApplyConfiguration(new ScimProvisioningLogConfiguration());
        modelBuilder.ApplyConfiguration(new ScimResourceMappingConfiguration());
        modelBuilder.ApplyConfiguration(new ScimAttributeMappingConfiguration());
        return modelBuilder;
    }
}