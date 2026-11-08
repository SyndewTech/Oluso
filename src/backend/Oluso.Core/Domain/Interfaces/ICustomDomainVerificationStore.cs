using Oluso.Core.Domain.Entities;

namespace Oluso.Core.Domain.Interfaces;

/// <summary>
/// Store for custom domain verification records.
/// </summary>
public interface ICustomDomainVerificationStore
{
    Task<CustomDomainVerification?> GetByIdAsync(string id, CancellationToken cancellationToken = default);
    Task<IEnumerable<CustomDomainVerification>> GetByTenantIdAsync(string tenantId, CancellationToken cancellationToken = default);
    Task<CustomDomainVerification?> GetByDomainAsync(string domain, CancellationToken cancellationToken = default);
    Task<IEnumerable<CustomDomainVerification>> GetPendingVerificationsAsync(CancellationToken cancellationToken = default);
    Task<IEnumerable<CustomDomainVerification>> GetActiveDomainsForRevalidationAsync(DateTime revalidateBefore, CancellationToken cancellationToken = default);

    /// <summary>
    /// Domains allowed to serve traffic: Verified or Active.
    /// <para>
    /// Verified counts because DNS ownership is already proven, and the host has
    /// to answer before a certificate can be issued for it — gating on Active
    /// alone deadlocks, since Active is what issuing a certificate produces.
    /// </para>
    /// </summary>
    Task<IEnumerable<CustomDomainVerification>> GetServableDomainsAsync(CancellationToken cancellationToken = default);
    Task<CustomDomainVerification> CreateAsync(CustomDomainVerification verification, CancellationToken cancellationToken = default);
    Task<CustomDomainVerification> UpdateAsync(CustomDomainVerification verification, CancellationToken cancellationToken = default);
    Task<bool> DeleteAsync(string id, CancellationToken cancellationToken = default);
}
