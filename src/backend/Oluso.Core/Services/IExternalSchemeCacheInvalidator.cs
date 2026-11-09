namespace Oluso.Core.Services;

/// <summary>
/// Drops cached state for a single external auth scheme so the next request
/// rebuilds it from the DB. Called by admin endpoints after IdP create /
/// update / delete — without this, edits don't take effect until the per-app
/// IOptionsMonitorCache slot expires (which is never, in practice).
/// </summary>
public interface IExternalSchemeCacheInvalidator
{
    void Invalidate(string scheme, string? tenantId);
}
