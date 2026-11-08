using Oluso.Core.Domain.Entities;

namespace Oluso.Core.Domain.Interfaces;

/// <summary>
/// Store for the email domains tenants claim and verify.
/// </summary>
public interface ITenantEmailDomainStore
{
    Task<TenantEmailDomain?> GetByIdAsync(string id, CancellationToken cancellationToken = default);

    /// <summary>The tenant's claims, newest first, removed ones left out.</summary>
    Task<IReadOnlyList<TenantEmailDomain>> GetByTenantIdAsync(string tenantId, CancellationToken cancellationToken = default);

    /// <summary>The tenant's claim on <paramref name="domain"/> in any status, removed included.</summary>
    Task<TenantEmailDomain?> GetByTenantAndDomainAsync(string tenantId, string domain, CancellationToken cancellationToken = default);

    /// <summary>The verified claim on <paramref name="domain"/>, whichever tenant holds it.</summary>
    Task<TenantEmailDomain?> GetVerifiedByDomainAsync(string domain, CancellationToken cancellationToken = default);

    /// <summary>
    /// Whether <paramref name="email"/> is at a domain, or a subdomain of one, that <paramref name="tenantId"/> has
    /// verified.
    /// </summary>
    Task<bool> IsVerifiedForTenantAsync(string tenantId, string? email, CancellationToken cancellationToken = default);

    Task<IReadOnlyList<TenantEmailDomain>> GetPendingAsync(CancellationToken cancellationToken = default);

    /// <summary>Verified domains last revalidated before <paramref name="revalidateBefore"/> (or never).</summary>
    Task<IReadOnlyList<TenantEmailDomain>> GetVerifiedForRevalidationAsync(DateTime revalidateBefore, CancellationToken cancellationToken = default);

    Task<TenantEmailDomain> CreateAsync(TenantEmailDomain domain, CancellationToken cancellationToken = default);
    Task<TenantEmailDomain> UpdateAsync(TenantEmailDomain domain, CancellationToken cancellationToken = default);
}
