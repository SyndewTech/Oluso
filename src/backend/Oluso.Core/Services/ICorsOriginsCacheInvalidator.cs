namespace Oluso.Core.Services;

/// <summary>
/// Clears the cached CORS origins so changes apply at once instead of after the cache expires:
/// call it when a client's CORS origins or a tenant's CORS settings change.
/// </summary>
public interface ICorsOriginsCacheInvalidator
{
    /// <summary>The origins collected from every client's CORS list.</summary>
    Task InvalidateCacheAsync(CancellationToken cancellationToken = default);

    /// <summary>One tenant's own CORS settings.</summary>
    Task InvalidateTenantAsync(string tenantId, CancellationToken cancellationToken = default);
}

/// <summary>
/// A tenant's CORS settings (Settings → CORS), stored in the tenant configuration under
/// <see cref="SectionKey"/>. They apply on the tenant's own host.
/// </summary>
public sealed class TenantCorsSettings
{
    public const string SectionKey = "CorsSettings";

    /// <summary>Any origin may call the tenant's host, without credentials.</summary>
    public bool AllowAllOrigins { get; set; }

    /// <summary>Origins (scheme://host[:port]) that may call the tenant's host, with credentials.</summary>
    public List<string> AllowedOrigins { get; set; } = new();
}
