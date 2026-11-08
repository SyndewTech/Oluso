using System.Collections.Concurrent;
using System.Linq.Expressions;
using System.Reflection;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.ChangeTracking;
using Microsoft.Extensions.Logging;
using Oluso.Core.Domain.Entities;

namespace Oluso.Core.Data;

/// <summary>
/// Validates that foreign keys on TenantEntity types reference entities
/// within the same tenant. Runs automatically during SaveChanges.
/// </summary>
public static class TenantForeignKeyValidator
{
    /// <summary>
    /// Cache of FK metadata per entity type, built once on first access.
    /// </summary>
    private static readonly ConcurrentDictionary<Type, IReadOnlyList<TenantForeignKeyDescriptor>> _fkCache = new();

    /// <summary>
    /// Cache of the compiled "exists" query delegates per principal entity type.
    /// Avoids rebuilding expression trees on every validation call.
    /// </summary>
    private static readonly ConcurrentDictionary<Type, Func<DbContext, string, string, CancellationToken, Task<bool>>>
        _existsQueryCache = new();

    /// <summary>
    /// Validates all tenant-scoped foreign keys for Added/Modified entities.
    /// Call this from SaveChanges/SaveChangesAsync before base.SaveChanges.
    /// </summary>
    public static async Task ValidateAsync(
        DbContext context,
        string? tenantId,
        HashSet<(Type entityType, string id)>? preValidated = null,
        ILogger? logger = null,
        CancellationToken cancellationToken = default)
    {
        if (tenantId == null) return;

        var entries = context.ChangeTracker.Entries<TenantEntity>()
            .Where(e => e.State == EntityState.Added || e.State == EntityState.Modified)
            .ToList();

        if (entries.Count == 0) return;

        foreach (var entry in entries)
        {
            var descriptors = GetForeignKeyDescriptors(context, entry.Entity.GetType());

            foreach (var fk in descriptors)
            {
                var fkValue = fk.ForeignKeyProperty.GetValue(entry.Entity) as string;

                // Skip null FK values
                if (string.IsNullOrEmpty(fkValue)) continue;

                // Skip if the value didn't change on a Modified entity
                if (entry.State == EntityState.Modified && !IsForeignKeyModified(entry, fk.ForeignKeyPropertyName))
                    continue;

                // Skip if controller already validated this
                if (preValidated?.Contains((fk.PrincipalEntityType, fkValue)) == true)
                    continue;

                // Accept a principal that is being inserted in this same SaveChanges
                // (e.g. a parent and its children added together) — it won't be in
                // the database yet, so the existence query below would wrongly fail.
                if (ExistsInChangeTracker(context, fk, fkValue, tenantId))
                    continue;

                // Validate: does an entity with this ID exist for this tenant?
                var existsQuery = GetOrBuildExistsQuery(fk);
                var exists = await existsQuery(context, fkValue, tenantId, cancellationToken);

                if (!exists)
                {
                    logger?.LogWarning(
                        "Tenant FK violation: {EntityType}.{FKProperty} = '{FKValue}' does not reference a valid {PrincipalType} in tenant '{TenantId}'",
                        entry.Entity.GetType().Name, fk.ForeignKeyPropertyName, fkValue, fk.PrincipalEntityType.Name, tenantId);

                    throw new TenantForeignKeyViolationException(
                        entry.Entity.GetType(),
                        fk.ForeignKeyPropertyName,
                        fkValue,
                        fk.PrincipalEntityType,
                        tenantId);
                }
            }
        }
    }

    /// <summary>
    /// Returns true if a principal entity with the given key and tenant is being
    /// added in the current change set (not yet persisted). Lets parent+child
    /// inserts within a single SaveChanges validate against each other.
    /// </summary>
    private static bool ExistsInChangeTracker(
        DbContext context, TenantForeignKeyDescriptor fk, string fkValue, string tenantId)
    {
        foreach (var entry in context.ChangeTracker.Entries())
        {
            if (entry.State != EntityState.Added)
                continue;
            if (!fk.PrincipalEntityType.IsInstanceOfType(entry.Entity))
                continue;

            var keyProp = entry.Properties.FirstOrDefault(p => p.Metadata.Name == fk.PrincipalKeyPropertyName);
            if (keyProp?.CurrentValue as string != fkValue)
                continue;

            var tenant = (entry.Entity as TenantEntity)?.TenantId;
            if (tenant == tenantId)
                return true;
        }

        return false;
    }

    private static bool IsForeignKeyModified(EntityEntry entry, string propertyName)
    {
        var prop = entry.Properties.FirstOrDefault(p => p.Metadata.Name == propertyName);
        return prop?.IsModified == true;
    }

    /// <summary>
    /// Builds (or retrieves from cache) a compiled query that checks:
    /// context.Set&lt;TPrincipal&gt;().IgnoreQueryFilters().Any(e => e.PK == fkValue &amp;&amp; e.TenantId == tenantId)
    /// </summary>
    private static Func<DbContext, string, string, CancellationToken, Task<bool>> GetOrBuildExistsQuery(
        TenantForeignKeyDescriptor fk)
    {
        return _existsQueryCache.GetOrAdd(fk.PrincipalEntityType, principalType =>
        {
            // Build: (DbContext ctx, string fkValue, string tenantId, CancellationToken cancellationToken) =>
            //   ctx.Set<TPrincipal>().IgnoreQueryFilters()
            //      .AnyAsync(e => e.PK == fkValue && e.TenantId == tenantId, cancellationToken)

            var ctxParam = Expression.Parameter(typeof(DbContext), "ctx");
            var fkValueParam = Expression.Parameter(typeof(string), "fkValue");
            var tenantIdParam = Expression.Parameter(typeof(string), "tenantId");
            var ctParam = Expression.Parameter(typeof(CancellationToken), "cancellationToken");

            // ctx.Set<TPrincipal>()
            var setMethod = typeof(DbContext).GetMethod(nameof(DbContext.Set), Type.EmptyTypes)!
                .MakeGenericMethod(principalType);
            var setCall = Expression.Call(ctxParam, setMethod);

            // .IgnoreQueryFilters()
            //
            // EF Core 9 introduced a second IgnoreQueryFilters overload that
            // accepts an IReadOnlyCollection<string> of filter names. Calling
            // GetMethod(name) is now AmbiguousMatchException — we have to
            // pick the parameter-less generic overload explicitly.
            var ignoreFiltersMethod = typeof(EntityFrameworkQueryableExtensions)
                .GetMethods()
                .Single(m => m.Name == nameof(EntityFrameworkQueryableExtensions.IgnoreQueryFilters)
                    && m.IsGenericMethodDefinition
                    && m.GetParameters().Length == 1)
                .MakeGenericMethod(principalType);
            var ignoreCall = Expression.Call(null, ignoreFiltersMethod, setCall);

            // Build predicate: e => e.PK == fkValue && e.TenantId == tenantId
            var entityParam = Expression.Parameter(principalType, "e");

            // e.TenantId == tenantId
            var tenantProp = Expression.Property(entityParam, nameof(TenantEntity.TenantId));
            var tenantEqual = Expression.Equal(tenantProp, tenantIdParam);

            // e.PK == fkValue
            var pkProp = Expression.Property(entityParam, fk.PrincipalKeyPropertyName);
            var pkEqual = Expression.Equal(pkProp, fkValueParam);

            var body = Expression.AndAlso(tenantEqual, pkEqual);
            var predicateType = typeof(Expression<>).MakeGenericType(
                typeof(Func<,>).MakeGenericType(principalType, typeof(bool)));
            var predicate = Expression.Lambda(
                typeof(Func<,>).MakeGenericType(principalType, typeof(bool)),
                body, entityParam);

            // Wrap predicate in Expression.Quote for use in AnyAsync
            var quotedPredicate = Expression.Quote(predicate);

            // .AnyAsync(predicate, cancellationToken)
            var anyAsyncMethod = typeof(EntityFrameworkQueryableExtensions)
                .GetMethods(BindingFlags.Public | BindingFlags.Static)
                .First(m => m.Name == nameof(EntityFrameworkQueryableExtensions.AnyAsync)
                            && m.GetParameters().Length == 3
                            && m.GetParameters()[1].ParameterType.IsGenericType)
                .MakeGenericMethod(principalType);

            var anyCall = Expression.Call(null, anyAsyncMethod, ignoreCall, quotedPredicate, ctParam);

            // Compile the whole thing
            var lambda = Expression.Lambda<Func<DbContext, string, string, CancellationToken, Task<bool>>>(
                anyCall, ctxParam, fkValueParam, tenantIdParam, ctParam);

            return lambda.Compile();
        });
    }

    /// <summary>
    /// Gets (from cache) the FK descriptors for a given entity type within a DbContext.
    /// </summary>
    private static IReadOnlyList<TenantForeignKeyDescriptor> GetForeignKeyDescriptors(DbContext context, Type entityType)
    {
        return _fkCache.GetOrAdd(entityType, type =>
        {
            var efEntityType = context.Model.FindEntityType(type);
            if (efEntityType == null) return Array.Empty<TenantForeignKeyDescriptor>();

            var descriptors = new List<TenantForeignKeyDescriptor>();

            foreach (var foreignKey in efEntityType.GetForeignKeys())
            {
                var principalType = foreignKey.PrincipalEntityType.ClrType;

                // Only validate FKs that point to TenantEntity types
                if (!typeof(TenantEntity).IsAssignableFrom(principalType))
                    continue;

                // Single-column string FK only (composite FKs are rare and complex)
                var fkProperties = foreignKey.Properties;
                if (fkProperties.Count != 1) continue;

                var fkProp = fkProperties[0];
                if (fkProp.ClrType != typeof(string)) continue;

                // Check for opt-out attribute on the CLR property
                var clrProperty = type.GetProperty(fkProp.Name, BindingFlags.Public | BindingFlags.Instance);
                if (clrProperty?.GetCustomAttribute<SkipTenantForeignKeyValidationAttribute>() != null)
                    continue;

                // Get the PK property name on the principal
                var principalKey = foreignKey.PrincipalKey.Properties;
                if (principalKey.Count != 1) continue;
                if (principalKey[0].ClrType != typeof(string)) continue;

                descriptors.Add(new TenantForeignKeyDescriptor
                {
                    ForeignKeyPropertyName = fkProp.Name,
                    ForeignKeyProperty = clrProperty ?? type.GetProperty(fkProp.Name)!,
                    PrincipalEntityType = principalType,
                    PrincipalKeyPropertyName = principalKey[0].Name,
                });
            }

            return descriptors;
        });
    }

    private sealed class TenantForeignKeyDescriptor
    {
        public required string ForeignKeyPropertyName { get; init; }
        public required PropertyInfo ForeignKeyProperty { get; init; }
        public required Type PrincipalEntityType { get; init; }
        public required string PrincipalKeyPropertyName { get; init; }
    }
}

/// <summary>
/// Thrown when a foreign key references an entity that doesn't belong to the current tenant.
/// </summary>
public class TenantForeignKeyViolationException : InvalidOperationException
{
    public Type EntityType { get; }
    public string ForeignKeyProperty { get; }
    public string ForeignKeyValue { get; }
    public Type PrincipalType { get; }
    public string TenantId { get; }

    public TenantForeignKeyViolationException(
        Type entityType,
        string foreignKeyProperty,
        string foreignKeyValue,
        Type principalType,
        string tenantId)
        : base($"Foreign key violation: {entityType.Name}.{foreignKeyProperty} = '{foreignKeyValue}' " +
               $"does not reference a valid {principalType.Name} in tenant '{tenantId}'.")
    {
        EntityType = entityType;
        ForeignKeyProperty = foreignKeyProperty;
        ForeignKeyValue = foreignKeyValue;
        PrincipalType = principalType;
        TenantId = tenantId;
    }
}
