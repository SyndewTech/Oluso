namespace Oluso.Middleware;

/// <summary>
/// Service to invalidate the host validation cache when tenants change
/// </summary>
public interface IHostValidationCacheInvalidator
{
    /// <summary>
    /// Invalidates the cached allowed hosts, forcing a refresh on next request
    /// </summary>
    Task InvalidateCacheAsync(CancellationToken cancellationToken = default);
}
