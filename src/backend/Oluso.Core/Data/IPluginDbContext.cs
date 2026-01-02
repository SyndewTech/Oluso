using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.ChangeTracking;
using Oluso.Core.Domain.Entities;

namespace Oluso.Core.Data;

/// <summary>
/// Minimal interface for plugin DbContext integration.
/// Provides tenant context and basic EF operations without requiring
/// knowledge of core Oluso entities.
/// </summary>
public interface IPluginDbContext
{
    /// <summary>
    /// Current tenant ID for filtering and auto-assignment
    /// </summary>
    string? TenantId { get; }

    /// <summary>
    /// Save changes to the database
    /// </summary>
    Task<int> SaveChangesAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// Access to change tracker for entity state management
    /// </summary>
    ChangeTracker ChangeTracker { get; }

    /// <summary>
    /// Get a DbSet for a specific entity type
    /// </summary>
    DbSet<TEntity> Set<TEntity>() where TEntity : class;
}

/// <summary>
/// Extended interface for plugins that need tenant-filtered queries
/// </summary>
public interface ITenantFilteredDbContext : IPluginDbContext
{
    /// <summary>
    /// Query entities without tenant filter (for admin/cross-tenant operations)
    /// </summary>
    IQueryable<TEntity> IgnoreTenantFilter<TEntity>() where TEntity : class;
}
