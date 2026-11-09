namespace Oluso.Core.Terminology;

/// <summary>
/// Persistence for tenant terminology overrides (the Terminology Center's
/// edits). Keyed by (tenant, locale, key). Platform-owned: the backing table
/// lives on the platform DbContext so overrides survive independently of which
/// modules a deployment enables.
/// </summary>
public interface ITerminologyOverrideStore
{
    /// <summary>All overrides for a tenant in a given locale.</summary>
    Task<IReadOnlyList<TerminologyOverride>> GetForTenantAsync(
        string tenantId, string locale, CancellationToken cancellationToken = default);

    /// <summary>Create or update the override for a single key.</summary>
    Task<TerminologyOverride> UpsertAsync(
        TerminologyOverride entity, CancellationToken cancellationToken = default);

    /// <summary>Remove an override, reverting the key to its inherited value. Returns true if one was removed.</summary>
    Task<bool> DeleteAsync(
        string tenantId, string locale, string key, CancellationToken cancellationToken = default);
}
