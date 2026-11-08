namespace Oluso.Core.Services;

/// <summary>
/// The web origins (scheme://host[:port]) of the applications a tenant has registered: its
/// enabled clients' redirect, post-logout, CORS and home addresses, the platform's first-party
/// clients, and the tenant's configured CORS origins.
/// </summary>
public interface ITenantAppOrigins
{
    Task<IReadOnlySet<string>> GetOriginsAsync(string tenantId, CancellationToken cancellationToken = default);
}
