namespace Oluso.Middleware;

/// <summary>
/// Service to invalidate the host validation cache when tenants change.
/// Extends the Core interface for backward compatibility.
/// </summary>
public interface IHostValidationCacheInvalidator : Oluso.Core.Services.IHostValidationCacheInvalidator
{
}
