using System.Linq.Expressions;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
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
    private ITenantContext? _tenantContext;
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
    /// Table prefix for this plugin's entities (e.g. "Wf_", "Bill_"). Return
    /// empty string to opt out of prefixing — required for protocol-store
    /// contexts whose table names follow an external spec (SCIM, FIDO2 etc).
    /// Abstract so plugin authors can't silently inherit a null and ship with
    /// unprefixed tables alongside other modules in a shared schema.
    /// </summary>
    protected abstract string? TablePrefix { get; }

    /// <summary>
    /// Current tenant ID
    /// </summary>
    public string? TenantId => _tenantId;

    /// <summary>
    /// Live tenant id read at query time from the application service provider
    /// configured on DbContextOptions. Filter lambdas reference this so EF
    /// parameterizes the property access and reads the current value per query —
    /// derived provider-specific subclasses don't need to take ITenantContext
    /// in their constructors.
    /// </summary>
    public string? CurrentTenantId
    {
        get
        {
            _tenantContext ??= this.GetService<IDbContextOptions>()
                .FindExtension<CoreOptionsExtension>()?
                .ApplicationServiceProvider?
                .GetService<ITenantContext>();
            return _tenantContext?.TenantId;
        }
    }

    /// <summary>
    /// Set of FK references that have already been validated by the caller (e.g., a controller).
    /// Call <see cref="MarkForeignKeyValidated{TPrincipal}"/> to add entries.
    /// Cleared after each SaveChanges call.
    /// </summary>
    private HashSet<(Type entityType, string id)>? _preValidatedForeignKeys;

    private ILogger? _validationLogger;

    protected PluginDbContextBase(DbContextOptions<TContext> options) : base(options)
    {
    }

    /// <summary>
    /// Constructor for derived provider-specific contexts (e.g., ScimDbContextSqlite).
    /// </summary>
    protected PluginDbContextBase(DbContextOptions options) : base(options)
    {
    }

    /// <summary>
    /// Constructor for derived provider-specific contexts with tenant support.
    /// </summary>
    protected PluginDbContextBase(DbContextOptions options, ITenantContext tenantContext) : base(options)
    {
        _tenantContext = tenantContext;
        _tenantId = tenantContext.TenantId;
    }

    protected PluginDbContextBase(
        DbContextOptions<TContext> options,
        ITenantContext tenantContext) : base(options)
    {
        _tenantContext = tenantContext;
        _tenantId = tenantContext.TenantId;
    }

    protected sealed override void OnModelCreating(ModelBuilder modelBuilder)
    {
        base.OnModelCreating(modelBuilder);

        // Let derived classes apply their entity configurations
        ConfigureModel(modelBuilder);

        // Apply tenant query filters to all TenantEntity derived types
        ApplyTenantQueryFilters(modelBuilder);

        // Apply module table naming AFTER all configurations have been applied
        ApplyModuleTableNaming(modelBuilder);
    }

    /// <summary>
    /// Override this to apply entity configurations for this module.
    /// This is called by OnModelCreating before table naming and tenant filters are applied.
    /// </summary>
    protected virtual void ConfigureModel(ModelBuilder modelBuilder)
    {
    }

    /// <summary>
    /// Applies module-level table and index naming based on the database provider:
    /// - SQLite: prefixes table names with <see cref="TablePrefix"/> (e.g., "Sales_Orders")
    /// - SQL Server: sets schema from prefix (e.g., schema "Sales", table "Orders")
    /// - PostgreSQL: sets lowercase schema (e.g., schema "sales", table "Orders")
    ///
    /// Also prefixes index names with the module prefix to avoid cross-module collisions
    /// (e.g., "IX_Products_Sku" becomes "IX_HR_Products_Sku"), since SQLite index names
    /// are global and case-insensitive.
    /// </summary>
    private void ApplyModuleTableNaming(ModelBuilder modelBuilder)
    {
        if (string.IsNullOrEmpty(TablePrefix)) return;

        var providerName = Database.ProviderName ?? "";

        if (providerName.Contains("Sqlite", StringComparison.OrdinalIgnoreCase))
        {
            foreach (var entity in modelBuilder.Model.GetEntityTypes())
            {
                var tableName = entity.GetTableName();
                if (tableName != null && !tableName.StartsWith(TablePrefix))
                    entity.SetTableName($"{TablePrefix}{tableName}");
            }
        }
        else
        {
            var schema = TablePrefix.TrimEnd('_');
            if (providerName.Contains("Npgsql", StringComparison.OrdinalIgnoreCase))
                schema = schema.ToLowerInvariant();

            foreach (var entity in modelBuilder.Model.GetEntityTypes())
            {
                if (entity.GetSchema() == null)
                    entity.SetSchema(schema);
            }
        }

        // Prefix index names to avoid cross-module collisions.
        // SQLite indexes are global (not scoped to a table) and case-insensitive,
        // so two modules with e.g. a "Products" entity would collide on "IX_Products_...".
        foreach (var entity in modelBuilder.Model.GetEntityTypes())
        {
            foreach (var index in entity.GetIndexes())
            {
                var indexName = index.GetDatabaseName();
                if (indexName != null && indexName.StartsWith("IX_") && !indexName.StartsWith($"IX_{TablePrefix}"))
                {
                    index.SetDatabaseName($"IX_{TablePrefix}{indexName.Substring(3)}");
                }
            }
        }
    }

    /// <summary>
    /// Apply tenant query filters automatically to all TenantEntity-derived types.
    /// References CurrentTenantId via closure capture of `this`; EF parameterizes
    /// the property access at query time, reading the live value from the
    /// executing DbContext on every query — no model cache invalidation needed.
    /// </summary>
    private void ApplyTenantQueryFilters(ModelBuilder builder)
    {
        foreach (var entityType in builder.Model.GetEntityTypes())
        {
            if (typeof(TenantEntity).IsAssignableFrom(entityType.ClrType))
            {
                var parameter = Expression.Parameter(entityType.ClrType, "e");
                var entityTenant = Expression.Property(parameter, nameof(TenantEntity.TenantId));
                var ctxTenant = Expression.Property(
                    Expression.Constant(this, typeof(PluginDbContextBase<TContext>)),
                    nameof(CurrentTenantId));
                var nullString = Expression.Constant(null, typeof(string));

                var ctxIsNull = Expression.Equal(ctxTenant, nullString);
                var equalTenant = Expression.Equal(entityTenant, ctxTenant);
                var entityIsNull = Expression.Equal(entityTenant, nullString);
                var filter = Expression.OrElse(ctxIsNull,
                    Expression.OrElse(equalTenant, entityIsNull));

                builder.Entity(entityType.ClrType)
                    .HasQueryFilter(Expression.Lambda(filter, parameter));
            }
        }
    }

    /// <summary>
    /// Marks a foreign key reference as already validated by the caller.
    /// The tenant FK validator will skip checking this (Type, Id) pair on save.
    /// </summary>
    /// <typeparam name="TPrincipal">The principal entity type being referenced</typeparam>
    /// <param name="id">The FK value (entity ID). Null values are ignored.</param>
    public void MarkForeignKeyValidated<TPrincipal>(string? id) where TPrincipal : TenantEntity
    {
        if (id == null) return;
        _preValidatedForeignKeys ??= new();
        _preValidatedForeignKeys.Add((typeof(TPrincipal), id));
    }

    /// <summary>
    /// Sets an optional logger for FK validation warnings.
    /// </summary>
    public void SetValidationLogger(ILogger logger) => _validationLogger = logger;

    public override int SaveChanges()
    {
        SetTenantId();
        // Run async validation synchronously — FK validation is critical for data integrity
        ValidateTenantForeignKeysAsync(default).GetAwaiter().GetResult();
        return base.SaveChanges();
    }

    public override async Task<int> SaveChangesAsync(CancellationToken cancellationToken = default)
    {
        SetTenantId();
        await ValidateTenantForeignKeysAsync(cancellationToken);
        return await base.SaveChangesAsync(cancellationToken);
    }

    private async Task ValidateTenantForeignKeysAsync(CancellationToken cancellationToken)
    {
        try
        {
            await TenantForeignKeyValidator.ValidateAsync(
                this, _tenantId, _preValidatedForeignKeys, _validationLogger, cancellationToken);
        }
        finally
        {
            // Clear pre-validated set after each save so stale entries don't leak
            _preValidatedForeignKeys?.Clear();
        }
    }

    /// <summary>
    /// Auto-assign TenantId to new entities. Reads the live tenant via
    /// CurrentTenantId rather than the constructor-captured `_tenantId`
    /// snapshot — `_tenantId` is null whenever the context was constructed
    /// outside a request scope (e.g. background imports, hosted services),
    /// even if the caller later sets a tenant via ITenantContextAccessor.
    /// Falling back to the live property keeps the autostamper working in
    /// both request-scoped and background-task scopes.
    /// </summary>
    private void SetTenantId()
    {
        var tenantId = CurrentTenantId;
        if (tenantId == null) return;

        var tenantEntities = ChangeTracker.Entries<TenantEntity>()
            .Where(e => e.State == EntityState.Added && e.Entity.TenantId == null);

        foreach (var entry in tenantEntities)
        {
            entry.Entity.TenantId = tenantId;
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
