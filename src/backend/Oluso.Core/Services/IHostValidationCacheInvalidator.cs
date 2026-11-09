namespace Oluso.Core.Services;

/// <summary>
/// Service to invalidate the host validation cache when tenants change.
/// </summary>
public interface IHostValidationCacheInvalidator
{
    Task InvalidateCacheAsync(CancellationToken cancellationToken = default);
}
