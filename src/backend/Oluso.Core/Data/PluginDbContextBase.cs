using System.Linq.Expressions;
using Microsoft.EntityFrameworkCore;
using Oluso.Core.Domain.Entities;
using Oluso.Core.Domain.Interfaces;

namespace Oluso.Core.Data;

/// <summary>
/// Base DbContext for Oluso plugins with built-in:
/// - Multi-tenant query filtering
/// - Auto TenantId assignment on save
/// - Automatic separate migration history table per plugin
/// - Optional table prefixing for plugin isolation
///
/// Usage:
/// <code>
/// // Just use the connection string - migrations table is automatic!
/// services.AddDbContext&lt;WorkflowDbContext&gt;(options =>
///     options.UseSqlite(connectionString));
/// </code>
/// </summary>
/// <typeparam name="TContext">The derived context type</typeparam>
public abstract class PluginDbContextBase<TContext> : DbContext, ITenantFilteredDbContext, IMigratableDbContext
    where TContext : DbContext
{
    private readonly ITenantContext? _tenantContext;
    private readonly string? _tenantId;

    /// <summary>
    /// The plugin name used for migration table isolation.
    /// Override this to provide a unique name (e.g., "Workflows", "Billing").
    /// This creates a migration table like "__EFMigrationsHistory_Workflows".
    /// </summary>
    protected abstract string PluginName { get; }

    /// <inheritdoc />
    string IMigratableDbContext.MigrationName => PluginName;

    /// <inheritdoc />
    /// <remarks>
    /// Plugin contexts default to order 100. Override to change migration order.
    /// </remarks>
    public virtual int MigrationOrder => 100;

    /// <summary>
    /// The plugin identifier used for table prefixing.
    /// Override this to provide a unique prefix for your plugin tables (e.g., "Wf_", "Bill_").
    /// Return null or empty to disable prefixing.
    /// </summary>
    protected virtual string? TablePrefix => null;

    /// <summary>
    /// Current tenant ID
    /// </summary>
    public string? TenantId => _tenantId;

    protected PluginDbContextBase(DbContextOptions<TContext> options) : base(options)
    {
    }

    /// <summary>
    /// Constructor for derived provider-specific contexts (e.g., ScimDbContextSqlite).
    /// </summary>
    protected PluginDbContextBase(DbContextOptions options) : base(options)
    {
    }

    protected PluginDbContextBase(
        DbContextOptions<TContext> options,
        ITenantContext tenantContext) : base(options)
    {
        _tenantContext = tenantContext;
        _tenantId = tenantContext.TenantId;
    }

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        base.OnModelCreating(modelBuilder);

        // Apply table prefix if specified
        if (!string.IsNullOrEmpty(TablePrefix))
        {
            foreach (var entity in modelBuilder.Model.GetEntityTypes())
            {
                var tableName = entity.GetTableName();
                if (tableName != null && !tableName.StartsWith(TablePrefix))
                {
                    entity.SetTableName($"{TablePrefix}{tableName}");
                }
            }
        }

        // Apply tenant query filters to all TenantEntity derived types
        ApplyTenantQueryFilters(modelBuilder);
    }

    /// <summary>
    /// Apply tenant query filters automatically to all TenantEntity-derived types
    /// </summary>
    private void ApplyTenantQueryFilters(ModelBuilder builder)
    {
        foreach (var entityType in builder.Model.GetEntityTypes())
        {
            if (typeof(TenantEntity).IsAssignableFrom(entityType.ClrType))
            {
                var parameter = Expression.Parameter(entityType.ClrType, "e");
                var tenantProperty = Expression.Property(parameter, nameof(TenantEntity.TenantId));
                var tenantValue = Expression.Constant(_tenantId, typeof(string));
                var tenantIdIsNull = Expression.Constant(_tenantId == null, typeof(bool));

                // Filter: _tenantId == null || e.TenantId == _tenantId || e.TenantId == null
                var equalTenant = Expression.Equal(tenantProperty, tenantValue);
                var entityIsNull = Expression.Equal(tenantProperty, Expression.Constant(null, typeof(string)));
                var tenantFilter = Expression.OrElse(equalTenant, entityIsNull);
                var filter = Expression.OrElse(tenantIdIsNull, tenantFilter);

                var lambda = Expression.Lambda(filter, parameter);
                builder.Entity(entityType.ClrType).HasQueryFilter(lambda);
            }
        }
    }

    public override int SaveChanges()
    {
        SetTenantId();
        return base.SaveChanges();
    }

    public override Task<int> SaveChangesAsync(CancellationToken cancellationToken = default)
    {
        SetTenantId();
        return base.SaveChangesAsync(cancellationToken);
    }

    /// <summary>
    /// Auto-assign TenantId to new entities
    /// </summary>
    private void SetTenantId()
    {
        if (_tenantId == null) return;

        var tenantEntities = ChangeTracker.Entries<TenantEntity>()
            .Where(e => e.State == EntityState.Added && e.Entity.TenantId == null);

        foreach (var entry in tenantEntities)
        {
            entry.Entity.TenantId = _tenantId;
        }
    }

    /// <summary>
    /// Query entities without tenant filter (for admin/cross-tenant operations)
    /// </summary>
    public IQueryable<TEntity> IgnoreTenantFilter<TEntity>() where TEntity : class
    {
        return Set<TEntity>().IgnoreQueryFilters();
    }
}

/// <summary>
/// Extension methods for configuring plugin DbContexts
/// </summary>
public static class PluginDbContextExtensions
{
    /// <summary>
    /// Gets the standard migration history table name for a plugin.
    /// </summary>
    /// <param name="pluginName">Unique plugin name (e.g., "Workflows", "Billing")</param>
    /// <returns>Migration history table name like "__EFMigrationsHistory_Workflows"</returns>
    public static string GetMigrationsTableName(string pluginName)
    {
        return $"__EFMigrationsHistory_{pluginName}";
    }

    /// <summary>
    /// Gets the PostgreSQL-friendly migration history table name (lowercase).
    /// </summary>
    public static string GetMigrationsTableNamePostgres(string pluginName)
    {
        return $"__efmigrationshistory_{pluginName.ToLowerInvariant()}";
    }
}
