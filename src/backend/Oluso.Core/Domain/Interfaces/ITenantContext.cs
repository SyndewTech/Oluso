using Oluso.Core.Domain.Entities;

namespace Oluso.Core.Domain.Interfaces;

/// <summary>
/// Provides access to the current tenant context
/// </summary>
public interface ITenantContext
{
    /// <summary>
    /// Gets the current tenant ID. Null if no tenant context.
    /// </summary>
    string? TenantId { get; }

    /// <summary>
    /// Gets the current tenant. Null if no tenant context.
    /// </summary>
    Tenant? Tenant { get; }

    /// <summary>
    /// Whether a valid tenant context exists
    /// </summary>
    bool HasTenant { get; }
}

/// <summary>
/// Allows setting the tenant context (for middleware/resolution)
/// </summary>
public interface ITenantContextAccessor : ITenantContext
{
    /// <summary>
    /// Sets the current tenant context
    /// </summary>
    void SetTenant(Tenant tenant);

    /// <summary>
    /// Clears the current tenant context
    /// </summary>
    void ClearTenant();
}
