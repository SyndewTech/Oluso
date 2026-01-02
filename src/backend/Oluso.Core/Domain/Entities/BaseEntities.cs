namespace Oluso.Core.Domain.Entities;

/// <summary>
/// Base entity with tenant isolation for multi-tenant scenarios
/// </summary>
public abstract class TenantEntity
{
    /// <summary>
    /// The tenant identifier. Null for global/shared resources.
    /// </summary>
    public string? TenantId { get; set; }
}

/// <summary>
/// Base entity with tenant isolation and audit fields
/// </summary>
public abstract class AuditableTenantEntity : TenantEntity
{
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public string? CreatedBy { get; set; }
    public DateTime? UpdatedAt { get; set; }
    public string? UpdatedBy { get; set; }
}

public enum TokenUsage
{
    ReUse = 0,
    OneTimeOnly = 1
}

public enum TokenExpiration
{
    Sliding = 0,
    Absolute = 1
}

public enum AccessTokenType
{
    Jwt = 0,
    Reference = 1
}
